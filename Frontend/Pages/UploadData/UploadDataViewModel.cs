using System.Globalization;
using System.IO;
using System.Text;
using Frontend.Api.Measurements;
using Frontend.Pages.Measurement;
using Frontend.Shared.Infrastructure;

namespace Frontend.Pages.UploadData;

/// <summary>
/// Upload Data page: import a CSV exported by the Measurement page, review it (rows can only be removed, never edited),
/// and upload it to the central database through the backend. The CSV file is only read; removing a row changes the
/// import in memory only. A failed upload keeps everything for a retry; a successful one keeps the rows for review and
/// is not repeated until another file is imported.
/// </summary>
public sealed class UploadDataViewModel : ObservableObject
{
    public const string NotAvailable = "Not available";

    // Larger files are not measurement exports (a long drillhole is a few thousand rows, well under 1 MB).
    private const long MaxFileBytes = 50L * 1024 * 1024;

    private readonly MeasurementUploadClient client;
    private readonly IDialogService dialogs;
    private readonly Func<string, CancellationToken, Task<string>> readFile;
    private ImportedMetadata? metadata;
    private string fileName = "";
    private bool isUploading;
    private bool isUploaded;

    /// <param name="readFile">Reads the chosen file's text; null reads it from disk (tests pass their own).</param>
    public UploadDataViewModel(MeasurementUploadClient client, IDialogService dialogs, ToastService toasts,
        Func<string, CancellationToken, Task<string>>? readFile = null)
    {
        this.client = client;
        this.dialogs = dialogs;
        this.readFile = readFile ?? ReadFileAsync;
        Toasts = toasts;
        UploadingUsername = WindowsAccount.Current();

        ImportCommand = new AsyncCommand(ImportAsync, error => Toasts.Error($"The CSV file could not be imported ({error.Message})."),
            () => !IsUploading);
        UploadCommand = new AsyncCommand(UploadAsync, error => Toasts.Error($"Upload failed ({error.Message}). The data is kept; try again."),
            () => CanUpload);
        DeleteCommand = new RelayCommand(Delete, parameter => parameter is MeasurementResult row && Results.Contains(row) && !IsUploading && !IsUploaded);

        Results.CollectionChanged += (_, _) =>
        {
            Changed(nameof(TotalText));
            RefreshCommands();
        };
    }

    public ToastService Toasts { get; }

    /// <summary>Imported rows in Measurement page order; the table's DataContext is this ViewModel, as on that page.</summary>
    public BulkObservableCollection<MeasurementResult> Results { get; } = [];

    public AsyncCommand ImportCommand { get; }

    public AsyncCommand UploadCommand { get; }

    public RelayCommand DeleteCommand { get; }

    public bool HasImport => metadata is not null;

    public string FileName
    {
        get => fileName;
        private set => Set(ref fileName, value);
    }

    public string Drillhole => Show(metadata?.Drillhole);

    public string Project => Show(metadata?.Project);

    public string Campaign => Show(metadata?.Campaign);

    public string Operator => Show(metadata?.Operator);

    public string Notes => Show(metadata?.Notes);

    /// <summary>Who created the measurements, from the CSV; files exported before it was recorded show "Not available".</summary>
    public string CreatedUsername => Show(metadata?.CreatedUsername);

    /// <summary>The Windows account using this app now (DOMAIN\user when on a domain).</summary>
    public string UploadingUsername { get; }

    public string TotalText => $"Total measurements: {Results.Count.ToString(CultureInfo.CurrentCulture)}";

    public bool IsUploading
    {
        get => isUploading;
        private set
        {
            if (Set(ref isUploading, value))
            {
                Changed(nameof(UploadLabel));
                RefreshCommands();
            }
        }
    }

    /// <summary>This import was stored in the central database; uploading it again needs a new import.</summary>
    public bool IsUploaded
    {
        get => isUploaded;
        private set
        {
            if (Set(ref isUploaded, value))
            {
                Changed(nameof(UploadLabel));
                RefreshCommands();
            }
        }
    }

    public string UploadLabel => IsUploading ? "Uploading…" : IsUploaded ? "✓   Uploaded" : "↑   Upload Data";

    private bool CanUpload => HasImport && Results.Count > 0 && !IsUploading && !IsUploaded;

    private static string Show(string? value) => string.IsNullOrWhiteSpace(value) ? NotAvailable : value;

    private async Task ImportAsync()
    {
        if (dialogs.PickCsvFile() is not { } path)
        {
            return;
        }

        string text;

        try
        {
            text = await readFile(path, CancellationToken.None);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Toasts.Error($"{Path.GetFileName(path)} could not be read ({error.Message}).");
            return;
        }

        ImportedMeasurements imported;

        try
        {
            imported = await Task.Run(() => MeasurementCsvReader.Read(text));
        }
        catch (CsvImportException error)
        {
            // The previous import (if any) stays as it was.
            Toasts.Error($"{Path.GetFileName(path)} was not imported. {error.Message}");
            return;
        }

        metadata = imported.Metadata;
        FileName = Path.GetFileName(path);
        Results.ReplaceAll(imported.Rows);
        IsUploaded = false;
        Changed(nameof(HasImport));
        Changed(nameof(Drillhole));
        Changed(nameof(Project));
        Changed(nameof(Campaign));
        Changed(nameof(Operator));
        Changed(nameof(Notes));
        Changed(nameof(CreatedUsername));
        RefreshCommands();

        if (imported.Rows.Any(r => r.FromMm is null || r.ToMm is null))
        {
            Toasts.Warning("Imported, but some rows have no depth (the file is from an older export). Export it again from the Measurement page to upload it.");
        }
        else
        {
            Toasts.Success($"Imported {Count(imported.Rows.Count)} from {FileName}.");
        }
    }

    private void Delete(object? parameter)
    {
        if (parameter is not MeasurementResult row || !dialogs.ConfirmRemoveImported())
        {
            return;
        }

        Results.Remove(row);

        for (var i = 0; i < Results.Count; i++)
        {
            Results[i].Number = i + 1;
        }
    }

    // Checks what the backend would reject, asks for confirmation, then uploads without blocking the UI.
    private async Task UploadAsync()
    {
        if (metadata is not { } info || Results.Count == 0)
        {
            Toasts.Error("Import a CSV file with at least one measurement first.");
            return;
        }

        if (UploadProblem(info) is { } problem)
        {
            Toasts.Error(problem);
            return;
        }

        if (!dialogs.ConfirmUpload(Results.Count, info.Drillhole!))
        {
            return;
        }

        var request = new MeasurementUploadRequest(
            new MeasurementUploadInformation(info.SessionId, info.Drillhole, info.Project, info.Campaign, info.Operator, info.Notes,
                info.CreatedUsername, UploadingUsername),
            Results.Select(r => new MeasurementUploadRecord(r.CaptureNumber, r.StartTime, r.EndTime, r.SampleCount, r.FromMetres, r.ToMetres,
                r.Reading.TsMs, r.Reading.Count0, r.Reading.Count0Raw, r.Reading.Hdm, r.Reading.HdmRaw, r.Reading.DoseRate)).ToList());

        IsUploading = true;

        try
        {
            var result = await client.UploadAsync(request, CancellationToken.None);

            if (!result.IsOk)
            {
                // Retrying is safe: the backend recognises a dataset it already stored and never inserts it twice.
                Toasts.Error($"Upload failed: {result.Error} The data is kept; try again.");
                return;
            }

            var response = result.Value!;
            IsUploaded = true;

            if (response.AlreadyUploaded)
            {
                Toasts.Warning($"These {Count(response.RecordCount)} were already uploaded on " +
                               $"{response.UploadedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}. Nothing was added again.");
            }
            else
            {
                Toasts.Success($"Uploaded {Count(response.RecordCount)} of {info.Drillhole}.");
            }
        }
        finally
        {
            IsUploading = false;
        }
    }

    // Client-side copy of the backend's required fields, so the user hears about them before the request.
    private string? UploadProblem(ImportedMetadata info)
    {
        if (string.IsNullOrWhiteSpace(info.Drillhole))
        {
            return "The CSV has no Drillhole, so it cannot be uploaded. Export it again from the Measurement page.";
        }

        if (string.IsNullOrWhiteSpace(info.Operator))
        {
            return "The CSV has no Operator, so it cannot be uploaded. Export it again from the Measurement page.";
        }

        return Results.FirstOrDefault(r => r.FromMm is null || r.ToMm is null) is { } row
            ? $"Row {row.Number} has no depth (from_m / to_m), so the file cannot be uploaded. Export it again from the Measurement page."
            : null;
    }

    private void RefreshCommands()
    {
        ImportCommand.Refresh();
        UploadCommand.Refresh();
        DeleteCommand.Refresh();
    }

    private static string Count(int records) => records == 1 ? "1 measurement" : $"{records.ToString(CultureInfo.CurrentCulture)} measurements";

    // Read-only and shared, so a file open in Excel can still be imported; it is never written.
    private static async Task<string> ReadFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: true);

        if (stream.Length > MaxFileBytes)
        {
            throw new InvalidDataException($"the file is larger than {MaxFileBytes / 1024 / 1024} MB");
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }
}
