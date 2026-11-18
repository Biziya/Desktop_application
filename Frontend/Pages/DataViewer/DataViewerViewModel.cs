using System.Globalization;
using Frontend.Api;
using Frontend.Api.Measurements;
using Frontend.Pages.Measurement;
using Frontend.Shared.Infrastructure;

namespace Frontend.Pages.DataViewer;

/// <summary>Data Viewer page: read-only view of uploaded measurements, by drillhole.</summary>
public sealed class DataViewerViewModel : ObservableObject, INavigationAware
{
    private readonly UploadedMeasurementsClient client;
    private IReadOnlyList<UploadedDrillhole> drillholes = [];
    private string drillholeSearch = "";
    private UploadedDrillhole? pickedDrillhole;
    private UploadedDrillhole? shown;
    private string message = "";
    private bool canRetry;
    private bool isLoading;
    private bool hasLoaded;
    private int loadVersion;
    private CancellationTokenSource? loads;
    private string? recordOperator;
    private bool widening;

    /// <param name="backend">Reloads after the backend comes back online; null in tests.</param>
    /// <param name="toasts">Says when a Refresh or Retry click worked; null in tests that do not check it.</param>
    public DataViewerViewModel(UploadedMeasurementsClient client, BackendConnection? backend = null, ToastService? toasts = null)
    {
        this.client = client;
        Toasts = toasts ?? new ToastService();
        RefreshCommand = new AsyncCommand(() => RefreshAsync(announce: true), LoadFailed);
        reloadCommand = new AsyncCommand(() => RefreshAsync(), LoadFailed);

        if (backend is not null)
        {
            backend.CameOnline += () =>
            {
                if (CanRetry)
                {
                    reloadCommand.Execute(null);
                }
            };
        }
    }

    public ToastService Toasts { get; }

    // Loads the page starts by itself (opening it, the backend coming back): no success toast.
    private readonly AsyncCommand reloadCommand;

    /// <summary>Drillholes matching the search text (all of them when it is empty or is the shown Hole ID).</summary>
    public BulkObservableCollection<UploadedDrillhole> DrillholeOptions { get; } = [];

    /// <summary>The shown drillhole's rows (MeasurementGrid binds Results).</summary>
    public BulkObservableCollection<MeasurementResult> Results { get; } = [];

    public AsyncCommand RefreshCommand { get; }

    public string DrillholeSearch
    {
        get => drillholeSearch;
        set
        {
            if (Set(ref drillholeSearch, value ?? ""))
            {
                Filter();
            }
        }
    }

    /// <summary>The dropdown selection. Picking a drillhole loads it; typing (which clears the selection) keeps the table.</summary>
    public UploadedDrillhole? PickedDrillhole
    {
        get => pickedDrillhole;
        set
        {
            if (Set(ref pickedDrillhole, value) && value is not null && value != shown)
            {
                _ = ShowAsync(value);
            }
        }
    }

    public UploadedDrillhole? Shown
    {
        get => shown;
        private set
        {
            if (Set(ref shown, value))
            {
                Changed(nameof(Project));
                Changed(nameof(Campaign));
                Changed(nameof(Operator));
                Changed(nameof(Notes));
                Changed(nameof(Summary));
            }
        }
    }

    public string Project => Shown?.ProjectCode is { Length: > 0 } project ? project : "";

    public string Campaign => Shown?.Campaign is { Length: > 0 } campaign ? campaign : "";

    /// <summary>Operator and Notes saved with the drillhole's latest upload. An older backend sends no Operator with the
    /// drillhole; the newest uploaded record's Operator is shown then.</summary>
    public string Operator => Shown?.Operator is { Length: > 0 } name ? name : Shown is null ? "" : recordOperator ?? "";

    public string Notes => Shown?.Notes is { Length: > 0 } notes ? notes : "";

    /// <summary>"2 uploads · 30 rows · last upload 2026-11-10 09:30".</summary>
    public string Summary => Shown is { } d
        ? $"{Count(d.UploadCount, "upload")} · {Count(Results.Count, "row")} · last upload {d.LastUploadedAt.ToLocalTime():yyyy-MM-dd HH:mm}"
        : "";

    public string Message { get => message; private set => Set(ref message, value); }

    /// <summary>The last load failed.</summary>
    public bool CanRetry { get => canRetry; private set => Set(ref canRetry, value); }

    public bool IsLoading { get => isLoading; private set => Set(ref isLoading, value); }

    public string EmptyText => Shown is null ? "Pick a drillhole to see its uploaded measurements." : "No measurements were uploaded for this drillhole.";

    /// <summary>Coming back to the page within this time shows the data already loaded; Refresh always reloads.</summary>
    public static readonly TimeSpan ReloadAfter = TimeSpan.FromSeconds(60);

    private DateTime loadedAt = DateTime.MinValue;

    public void OnShown()
    {
        if (!hasLoaded || CanRetry || DateTime.UtcNow - loadedAt >= ReloadAfter)
        {
            reloadCommand.Execute(null);
        }
    }

    public void OnHidden()
    {
    }

    /// <summary>Reloads the drillhole list, then the picked (or shown) drillhole's rows.</summary>
    /// <param name="announce">A Refresh or Retry click: say so when it worked.</param>
    public async Task RefreshAsync(bool announce = false)
    {
        var version = ++loadVersion;
        var token = NextLoad();
        IsLoading = true;
        Message = hasLoaded ? "" : "Loading uploaded drillholes…";

        try
        {
            var result = await client.GetDrillholesAsync(token);

            if (version != loadVersion)
            {
                return;
            }

            if (!result.IsOk)
            {
                ShowProblem(Problem(result));
                return;
            }

            hasLoaded = true;
            loadedAt = DateTime.UtcNow;
            CanRetry = false;
            drillholes = result.Value!;
            Filter();
            Message = drillholes.Count == 0 ? "No measurements have been uploaded yet. Upload a CSV on the Upload Data page." : "";

            // Keep the drillhole picked last (with its fresh counts); it may also have been removed from the database.
            var wanted = PickedDrillhole ?? Shown;
            var current = wanted is null ? null : drillholes.FirstOrDefault(d => d.HoleId == wanted.HoleId);

            if (current is null)
            {
                recordOperator = null;
                Shown = null;
                Results.ReplaceAll([]);
                Changed(nameof(EmptyText));
            }
            else if (!await LoadRecordsAsync(current, version, token))
            {
                return;
            }

            if (announce)
            {
                var listed = $"Refreshed: {Count(drillholes.Count, "uploaded drillhole")}.";
                Toasts.Success(Shown is { } d ? $"{listed} {d.HoleId}: {Count(Results.Count, "row")}." : listed);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // A newer pick or refresh took over.
        }
        finally
        {
            if (version == loadVersion)
            {
                IsLoading = false;
            }
        }
    }

    /// <summary>The table rows for uploaded records: Measurement page order (by depth), numbered from 1.</summary>
    public static IReadOnlyList<MeasurementResult> ToRows(IEnumerable<UploadedRecord> records)
    {
        var rows = records
            .OrderBy(r => r.DepthFrom).ThenBy(r => r.DepthTo).ThenBy(r => r.UploadedAt).ThenBy(r => r.CaptureNumber)
            .Select(r => new MeasurementResult
            {
                SessionId = r.SessionId,
                CaptureNumber = r.CaptureNumber,
                StartTime = r.StartTime,
                EndTime = r.EndTime,
                SampleCount = r.SampleCount,
                DurationMs = (long)(r.EndTime - r.StartTime).TotalMilliseconds,
                Reading = new DeviceReading(r.TsMs, r.Count0, r.Count0Raw, r.Hdm, r.HdmRaw, r.DoseRate),
                FromMm = ToMillimetres(r.DepthFrom),
                ToMm = ToMillimetres(r.DepthTo),
                StepMm = ToMillimetres(r.DepthTo) - ToMillimetres(r.DepthFrom)
            })
            .ToList();

        for (var i = 0; i < rows.Count; i++)
        {
            rows[i].Number = i + 1;
        }

        return rows;
    }

    // A pick loads the drillhole's rows. Never throws: it runs unobserved from the PickedDrillhole setter.
    private async Task ShowAsync(UploadedDrillhole drillhole)
    {
        var version = ++loadVersion;
        var token = NextLoad();
        IsLoading = true;

        try
        {
            await LoadRecordsAsync(drillhole, version, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // A newer pick or refresh took over.
        }
        catch (Exception error)
        {
            if (version == loadVersion)
            {
                ShowProblem($"Could not load {drillhole.HoleId} ({error.Message}).");
            }
        }
        finally
        {
            if (version == loadVersion)
            {
                IsLoading = false;
            }
        }
    }

    /// <summary>Loads and shows the drillhole's rows; false when a newer load took over or the request failed.</summary>
    private async Task<bool> LoadRecordsAsync(UploadedDrillhole drillhole, int version, CancellationToken token)
    {
        var result = await client.GetRecordsAsync(drillhole.HoleId, token);

        if (version != loadVersion)
        {
            return false;
        }

        if (!result.IsOk)
        {
            ShowProblem(Problem(result));
            return false;
        }

        // Up to 10,000 rows: sorted and built off the UI thread, then shown in one go.
        var records = result.Value!;
        var rows = await Task.Run(() => ToRows(records), token);

        if (version != loadVersion)
        {
            return false;
        }

        CanRetry = false;
        Message = "";
        recordOperator = records.OrderByDescending(r => r.UploadedAt).Select(r => r.Operator).FirstOrDefault(o => !string.IsNullOrWhiteSpace(o));
        Results.ReplaceAll(rows);
        Shown = drillhole;
        Changed(nameof(Operator));
        Changed(nameof(Summary));
        Changed(nameof(EmptyText));
        return true;
    }

    // A new load cancels the one in progress: its request and its result.
    private CancellationToken NextLoad()
    {
        loads?.Cancel();
        loads = new CancellationTokenSource();
        return loads.Token;
    }

    private void LoadFailed(Exception error) => ShowProblem($"Could not load the uploaded data ({error.Message}).");

    private void Filter()
    {
        var text = DrillholeSearch.Trim();
        var all = text.Length == 0 || text == Shown?.HoleId || text == PickedDrillhole?.HoleId;
        IReadOnlyList<UploadedDrillhole> wanted = all ? drillholes : drillholes.Where(d => d.HoleId.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();

        // Unchanged: no reset, so the open dropdown is left alone.
        if (DrillholeOptions.SequenceEqual(wanted, ReferenceEqualityComparer.Instance))
        {
            return;
        }

        // A pick sets the text to its Hole ID while the dropdown is still selecting; resetting its list in the middle of
        // that can make the selection jump. The list widens back to every drillhole just after.
        if (all && text.Length > 0 && text == PickedDrillhole?.HoleId && SynchronizationContext.Current is { } ui)
        {
            if (!widening)
            {
                widening = true;
                ui.Post(_ =>
                {
                    widening = false;

                    if (DrillholeSearch.Trim() == text)
                    {
                        DrillholeOptions.ReplaceAll(drillholes);
                    }
                }, null);
            }

            return;
        }

        DrillholeOptions.ReplaceAll(wanted);
    }

    private void ShowProblem(string text)
    {
        Message = text;
        CanRetry = true;
    }

    private static string Problem<T>(ApiResult<T> result) => result.Status switch
    {
        ApiStatus.Unreachable => $"{result.Error} The uploaded data cannot be shown until the backend is reachable.",
        _ => result.Error
    };

    private static long ToMillimetres(decimal metres) => (long)Math.Round(metres * 1000m, MidpointRounding.AwayFromZero);

    private static string Count(int count, string noun) =>
        $"{count.ToString(CultureInfo.CurrentCulture)} {noun}{(count == 1 ? "" : "s")}";
}
