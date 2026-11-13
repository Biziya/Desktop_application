using System.Globalization;
using Frontend.Api;
using Frontend.Api.Measurements;
using Frontend.Pages.Measurement;
using Frontend.Shared.Infrastructure;

namespace Frontend.Pages.DataViewer;

/// <summary>
/// Data Viewer page: the measurements stored in the central database by the Upload Data page, by drillhole. Pick a
/// drillhole to see its project, campaign and every uploaded row in the Measurement page's table. Read-only.
/// The drillhole list reloads whenever the page is shown and when the backend comes back online after a failure.
/// </summary>
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

    /// <param name="backend">Reloads after the backend comes back online; null in tests.</param>
    public DataViewerViewModel(UploadedMeasurementsClient client, BackendConnection? backend = null)
    {
        this.client = client;
        RefreshCommand = new AsyncCommand(RefreshAsync, error => ShowProblem($"Could not load the uploaded data ({error.Message})."));

        if (backend is not null)
        {
            backend.CameOnline += () =>
            {
                if (CanRetry)
                {
                    RefreshCommand.Execute(null);
                }
            };
        }
    }

    /// <summary>Drillholes matching the search text (all of them when it is empty or is the shown Hole ID).</summary>
    public BulkObservableCollection<UploadedDrillhole> DrillholeOptions { get; } = [];

    /// <summary>The shown drillhole's rows, by depth, as on the Measurement page (MeasurementGrid binds Results).</summary>
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

    /// <summary>The drillhole whose rows are in the table.</summary>
    public UploadedDrillhole? Shown
    {
        get => shown;
        private set
        {
            if (Set(ref shown, value))
            {
                Changed(nameof(Project));
                Changed(nameof(Campaign));
                Changed(nameof(Summary));
            }
        }
    }

    public string Project => Shown?.ProjectCode is { Length: > 0 } project ? project : "";

    public string Campaign => Shown?.Campaign is { Length: > 0 } campaign ? campaign : "";

    /// <summary>"2 uploads · 30 rows · last upload 2026-11-10 09:30".</summary>
    public string Summary => Shown is { } d
        ? $"{Count(d.UploadCount, "upload")} · {Count(Results.Count, "row")} · last upload {d.LastUploadedAt.ToLocalTime():yyyy-MM-dd HH:mm}"
        : "";

    /// <summary>Loading, error or empty-state text under the drillhole box; empty when there is nothing to say.</summary>
    public string Message { get => message; private set => Set(ref message, value); }

    /// <summary>The last load failed: Retry is shown.</summary>
    public bool CanRetry { get => canRetry; private set => Set(ref canRetry, value); }

    public bool IsLoading { get => isLoading; private set => Set(ref isLoading, value); }

    public string EmptyText => Shown is null ? "Pick a drillhole to see its uploaded measurements." : "No measurements were uploaded for this drillhole.";

    public void OnShown() => RefreshCommand.Execute(null);

    public void OnHidden()
    {
    }

    /// <summary>Reloads the drillhole list, then the shown drillhole's rows.</summary>
    public async Task RefreshAsync()
    {
        var version = ++loadVersion;
        IsLoading = true;
        Message = hasLoaded ? "" : "Loading uploaded drillholes…";

        try
        {
            var result = await client.GetDrillholesAsync(CancellationToken.None);

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
            CanRetry = false;
            drillholes = result.Value!;
            Filter();
            Message = drillholes.Count == 0 ? "No measurements have been uploaded yet. Upload a CSV on the Upload Data page." : "";

            // Keep the shown drillhole (with its fresh counts); it may also have been removed from the database.
            var current = Shown is null ? null : drillholes.FirstOrDefault(d => d.HoleId == Shown.HoleId);

            if (current is null)
            {
                Shown = null;
                Results.ReplaceAll([]);
                Changed(nameof(EmptyText));
            }
            else
            {
                await ShowAsync(current);
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

    private async Task ShowAsync(UploadedDrillhole drillhole)
    {
        var version = ++loadVersion;
        IsLoading = true;

        try
        {
            var result = await client.GetRecordsAsync(drillhole.HoleId, CancellationToken.None);

            if (version != loadVersion)
            {
                return;
            }

            if (!result.IsOk)
            {
                ShowProblem(Problem(result));
                return;
            }

            CanRetry = false;
            Message = "";
            Results.ReplaceAll(ToRows(result.Value!));
            Shown = drillhole;
            Changed(nameof(Summary));
            Changed(nameof(EmptyText));
        }
        finally
        {
            if (version == loadVersion)
            {
                IsLoading = false;
            }
        }
    }

    private void Filter()
    {
        var text = DrillholeSearch.Trim();
        var all = text.Length == 0 || text == Shown?.HoleId || text == PickedDrillhole?.HoleId;
        DrillholeOptions.ReplaceAll(all ? drillholes : drillholes.Where(d => d.HoleId.Contains(text, StringComparison.OrdinalIgnoreCase)));
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
