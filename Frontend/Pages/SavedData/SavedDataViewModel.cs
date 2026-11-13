using System.Collections.ObjectModel;
using Frontend.Shared.Infrastructure;

namespace Frontend.Pages.SavedData;

/// <summary>
/// Saved Data page: browse what the app stored in measurements.db — sessions (one per drillhole), their captures and
/// each capture's raw device samples. Read-only; changes are made on the Measurement page. Reloads whenever the page
/// is shown, after a save and after old data is removed.
/// </summary>
public sealed class SavedDataViewModel : ObservableObject, INavigationAware
{
    public const int PageSize = 200;

    private readonly SavedDataQueries queries;
    private readonly ToastService? toasts;
    private IReadOnlyList<SessionSummary> allSessions = [];
    private SessionSummary? selectedSession;
    private RecordRow? selectedRecord;
    private string searchText = "";
    private PeriodOption selectedPeriod;
    private int pageIndex;
    private int totalRecords;
    private bool isLoading;
    private string loadError = "";
    private int loadVersion;

    public SavedDataViewModel(SavedDataQueries queries, string retentionNote, ToastService? toasts = null)
    {
        this.queries = queries;
        this.toasts = toasts;
        RetentionNote = retentionNote;
        Periods =
        [
            new PeriodOption("All saved data", null),
            new PeriodOption("Today", TimeSpan.Zero),
            new PeriodOption("Last 7 days", TimeSpan.FromDays(7))
        ];
        selectedPeriod = Periods[0];
        RefreshCommand = new AsyncCommand(RefreshAsync, ShowError);
        NextPageCommand = new RelayCommand(_ => _ = LoadPageAsync(pageIndex + 1), _ => pageIndex + 1 < PageCount);
        PreviousPageCommand = new RelayCommand(_ => _ = LoadPageAsync(pageIndex - 1), _ => pageIndex > 0);
        ClearSearchCommand = new RelayCommand(_ => SearchText = "");
    }

    public sealed record PeriodOption(string Label, TimeSpan? Within)
    {
        public override string ToString() => Label;
    }

    public IReadOnlyList<PeriodOption> Periods { get; }

    public ObservableCollection<SessionSummary> Sessions { get; } = [];

    public ObservableCollection<RecordRow> Records { get; } = [];

    public ObservableCollection<RawSampleRow> RawSamples { get; } = [];

    public AsyncCommand RefreshCommand { get; }

    public RelayCommand NextPageCommand { get; }

    public RelayCommand PreviousPageCommand { get; }

    public RelayCommand ClearSearchCommand { get; }

    /// <summary>"Sessions with no capture in the last 30 days are deleted automatically." (or that deletion is off).</summary>
    public string RetentionNote { get; }

    public string DatabasePath => queries.DatabasePath;

    public string SearchText
    {
        get => searchText;
        set
        {
            if (Set(ref searchText, value ?? ""))
            {
                ApplyFilter();
            }
        }
    }

    public PeriodOption SelectedPeriod
    {
        get => selectedPeriod;
        set
        {
            if (value is not null && Set(ref selectedPeriod, value))
            {
                ApplyFilter();
            }
        }
    }

    public SessionSummary? SelectedSession
    {
        get => selectedSession;
        set
        {
            if (Set(ref selectedSession, value))
            {
                Changed(nameof(HasSelectedSession));
                _ = LoadPageAsync(0);
            }
        }
    }

    public bool HasSelectedSession => selectedSession is not null;

    public RecordRow? SelectedRecord
    {
        get => selectedRecord;
        set
        {
            if (Set(ref selectedRecord, value))
            {
                Changed(nameof(HasSelectedRecord));
                _ = LoadRawSamplesAsync(value);
            }
        }
    }

    public bool HasSelectedRecord => selectedRecord is not null;

    public bool IsLoading { get => isLoading; private set => Set(ref isLoading, value); }

    /// <summary>Why the database could not be read; empty when it could.</summary>
    public string LoadError { get => loadError; private set => Set(ref loadError, value); }

    public string SessionsSummary => allSessions.Count == 0
        ? "No saved sessions"
        : Sessions.Count == allSessions.Count
            ? $"{allSessions.Count} session{(allSessions.Count == 1 ? "" : "s")}"
            : $"{Sessions.Count} of {allSessions.Count} sessions";

    public int PageCount => Math.Max(1, (totalRecords + PageSize - 1) / PageSize);

    /// <summary>"1–200 of 431 captures".</summary>
    public string RecordsSummary => totalRecords == 0
        ? "No captures"
        : $"{pageIndex * PageSize + 1}–{pageIndex * PageSize + Records.Count} of {totalRecords} capture{(totalRecords == 1 ? "" : "s")}";

    public bool HasMultiplePages => PageCount > 1;

    public void OnShown() => RefreshCommand.Execute(null);

    public void OnHidden()
    {
    }

    /// <summary>Reloads the session list, keeping the selected session (and its page) when it still exists.</summary>
    public async Task RefreshAsync()
    {
        var version = ++loadVersion;
        IsLoading = true;

        try
        {
            var sessions = await Task.Run(queries.LoadSessions);

            if (version != loadVersion)
            {
                return;
            }

            LoadError = "";
            allSessions = sessions;
            var keepPage = pageIndex;
            ApplyFilter();

            if (selectedSession is not null)
            {
                await LoadPageAsync(keepPage);
            }
        }
        catch (Exception error)
        {
            LoadError = $"The measurement database could not be read: {error.Message}";
            throw;
        }
        finally
        {
            if (version == loadVersion)
            {
                IsLoading = false;
            }
        }
    }

    private void ApplyFilter()
    {
        var term = searchText.Trim();
        var since = selectedPeriod.Within is { } within ? DateTimeOffset.Now.Date - within : (DateTimeOffset?)null;

        var visible = allSessions.Where(s =>
            (since is null || s.LastActivity >= since) &&
            (term.Length == 0 || Matches(s.DrillholeId) || Matches(s.DrillholeName) || Matches(s.Project) || Matches(s.Operator)));

        Sessions.Clear();

        foreach (var session in visible)
        {
            Sessions.Add(session);
        }

        // Keep the selection across reloads by id (a reload brings new objects with updated counts).
        if (selectedSession is { } selected)
        {
            if (Sessions.FirstOrDefault(s => s.Id == selected.Id) is not { } same)
            {
                SelectedSession = null;
            }
            else if (!ReferenceEquals(same, selected))
            {
                selectedSession = same;
                Changed(nameof(SelectedSession));
            }
        }

        Changed(nameof(SessionsSummary));

        bool Matches(string? value) => value?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;
    }

    private async Task LoadPageAsync(int page)
    {
        var session = selectedSession;
        SelectedRecord = null;

        if (session is null)
        {
            Records.Clear();
            SetTotal(0, 0);
            return;
        }

        try
        {
            var result = await Task.Run(() => queries.LoadRecords(session.Id, page * PageSize, PageSize));

            if (session.Id != selectedSession?.Id)
            {
                return;
            }

            // The page no longer exists (records were removed): show the first one.
            if (result.Rows.Count == 0 && page > 0)
            {
                await LoadPageAsync(0);
                return;
            }

            Records.Clear();

            foreach (var row in result.Rows)
            {
                Records.Add(row);
            }

            SetTotal(page, result.Total);
        }
        catch (Exception error)
        {
            ShowError(error);
        }
    }

    private async Task LoadRawSamplesAsync(RecordRow? record)
    {
        RawSamples.Clear();

        if (record is null)
        {
            return;
        }

        try
        {
            var samples = await Task.Run(() => queries.LoadRawSamples(record.Id));

            if (record == selectedRecord)
            {
                foreach (var sample in samples)
                {
                    RawSamples.Add(sample);
                }
            }
        }
        catch (Exception error)
        {
            ShowError(error);
        }
    }

    private void SetTotal(int page, int total)
    {
        pageIndex = page;
        totalRecords = total;
        Changed(nameof(PageCount));
        Changed(nameof(HasMultiplePages));
        Changed(nameof(RecordsSummary));
        NextPageCommand.Refresh();
        PreviousPageCommand.Refresh();
    }

    private void ShowError(Exception error) => toasts?.Error($"Saved data could not be loaded: {error.Message}");
}
