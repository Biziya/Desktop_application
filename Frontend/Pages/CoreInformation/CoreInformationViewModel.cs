using Frontend.Api;
using Frontend.Api.CoreInformation.Drillholes;
using Frontend.Calculation.CoreInformation;
using Frontend.Pages.Measurement;
using Frontend.Shared.Infrastructure;
using Frontend.Shared.Models;

namespace Frontend.Pages.CoreInformation;

/// <summary>
/// Core Information form and Device Status. Field mapping (labels were renamed earlier; storage was not):
/// Drillhole = <see cref="DrillholeId"/> (API holeId, SQLite DrillholeID), Project = <see cref="DrillholeName"/>
/// (API projectCode, SQLite DrillholeName), Campaign = <see cref="ProjectArea"/> (API campaign, SQLite Project).
/// The drillhole list comes from the backend (shown from the local cache first); Device Status is checked locally on
/// this laptop by <see cref="DeviceStatusMonitor"/>. One failing never blocks the others or touches form values.
/// </summary>
public sealed class CoreInformationViewModel : ObservableObject
{
    // Small changes to the dropdown list are applied item by item (the selection survives); bigger ones in one Reset.
    private const int IncrementalChangeLimit = 64;

    private readonly DrillholesClient drillholesApi;
    private readonly DrillholeCache cache;
    private readonly IMeasurementRepository history;
    private readonly TimeSpan searchDelay;
    private string drillholeId = "";
    private string drillholeName = "";
    private string projectArea = "";
    private string operatorName = "";
    private string notes = "";
    private string drillholeSearch = "";
    private bool showErrors;
    private DrillholeCatalog catalog = DrillholeCatalog.Empty;
    private IReadOnlyList<MeasuredDrillhole> measured = [];
    private HashSet<DrillholeOption> shownOptions = new(ReferenceEqualityComparer.Instance);
    private DrillholeOption? closestOption;
    private bool isSelectionLocked;
    private bool cacheChecked;
    private bool emptyResponse;
    private bool rebuildingOptions;
    private int optionsVersion;
    private Task optionsUpdate = Task.CompletedTask;
    private Task? measuredLoad;
    private Task? drillholesLoad;
    private ApiStatus? drillholesStatus;
    private string drillholesError = "";

    /// <param name="searchDelay">Pause after the last keystroke before the list is filtered (default 150 ms).</param>
    public CoreInformationViewModel(
        Action<CoreSnapshot> saved,
        string databasePath,
        DrillholesClient drillholesApi,
        BackendConnection backend,
        DrillholeCache cache,
        IMeasurementRepository history,
        DeviceStatusMonitor devices,
        TimeSpan? searchDelay = null,
        ToastService? toasts = null)
    {
        Toasts = toasts ?? new ToastService();
        this.drillholesApi = drillholesApi;
        this.cache = cache;
        this.history = history;
        this.searchDelay = searchDelay ?? TimeSpan.FromMilliseconds(150);
        Devices = devices;
        DatabasePath = databasePath;
        operatorName = CurrentWindowsAccount();
        SaveCommand = new RelayCommand(_ => Save(saved));
        RefreshDrillholesCommand = new RelayCommand(_ => _ = LoadDrillholesAsync());
        ClearDrillholeCommand = new RelayCommand(_ => ClearDrillhole(), _ => CanClearDrillhole);
        RefreshBluetoothCommand = new RelayCommand(_ => _ = devices.CheckBluetoothAsync());
        RefreshMeasurementDatabaseCommand = new RelayCommand(_ => _ = devices.CheckMeasurementDatabaseAsync());
        RefreshGammaGuardDatabaseCommand = new RelayCommand(_ => _ = devices.CheckGammaGuardDatabaseAsync(rediscover: true));

        // A drillhole list that failed because the backend was offline loads again as soon as it is back.
        backend.CameOnline += () =>
        {
            if (drillholesStatus is not ApiStatus.Ok)
            {
                _ = LoadDrillholesAsync();
            }
        };

        _ = LoadDrillholesAsync();
        _ = RefreshMeasuredAsync();
    }

    public event Action? Edited;

    /// <summary>Raised with the new Drillhole ID ("" when cleared) whenever the selected drillhole changes.</summary>
    public event Action<string>? DrillholeChanged;

    /// <summary>Validation errors (5 s toasts).</summary>
    public ToastService Toasts { get; }

    /// <summary>The × in the Drillhole box: clears the search text and the selected drillhole (with Project and
    /// Campaign). Operator, Notes and every stored session and measurement stay as they are.</summary>
    public RelayCommand ClearDrillholeCommand { get; }

    public bool CanClearDrillhole => !isSelectionLocked && (drillholeSearch.Length > 0 || DrillholeId.Length > 0);

    /// <summary>
    /// True while measurement mode is active (set by MainViewModel): the drillhole cannot be changed or cleared until
    /// Stop, so a running capture can never be assigned to another drillhole.
    /// </summary>
    public bool IsSelectionLocked
    {
        get => isSelectionLocked;
        set
        {
            if (Set(ref isSelectionLocked, value))
            {
                Changed(nameof(CanClearDrillhole));
                ClearDrillholeCommand.Refresh();
            }
        }
    }

    /// <summary>
    /// Best match for the typed text — exact, then prefix, then partial — highlighted in the list. It is not picked
    /// until the user presses Enter (<see cref="CommitClosest"/>) or clicks it; the typed text is never replaced.
    /// </summary>
    public DrillholeOption? ClosestOption
    {
        get => closestOption;
        private set => Set(ref closestOption, value);
    }

    public RelayCommand SaveCommand { get; }

    public RelayCommand RefreshDrillholesCommand { get; }

    public RelayCommand RefreshBluetoothCommand { get; }

    public RelayCommand RefreshMeasurementDatabaseCommand { get; }

    public RelayCommand RefreshGammaGuardDatabaseCommand { get; }

    /// <summary>Path of the application's own measurement SQLite database (from the persistence service).</summary>
    public string DatabasePath { get; }

    /// <summary>Local Device Status of this laptop: CT-007 Bluetooth, measurement database, Gamma Guard database.</summary>
    public DeviceStatusMonitor Devices { get; }

    // ---------- Drillhole Information ----------

    /// <summary>
    /// Dropdown entries matching <see cref="DrillholeSearch"/> (partial, case-insensitive), in two groups:
    /// "Measured drillholes" (saved measurement history, most recently measured first) and "All drillholes" (the rest,
    /// alphabetical). Each drillhole appears once; a group heading is listed only when its group has entries. Every
    /// match is listed (nothing is truncated); the dropdown virtualizes, so only visible rows get controls.
    /// </summary>
    public BulkObservableCollection<DrillholeOption> DrillholeOptions { get; } = [];

    /// <summary>Text typed in the Drillhole box. Filters the list (after a short pause, off the UI thread); it never
    /// selects an entry by itself unless it is exactly an existing Hole ID.</summary>
    public string DrillholeSearch
    {
        get => drillholeSearch;
        set
        {
            // While the option list is rebuilt the dropdown loses its selected item for a moment and writes "" (or the
            // text of nothing) back here. That echo is not user input: ignore it so the pick and Project/Campaign stay.
            if (rebuildingOptions || value == drillholeSearch)
            {
                return;
            }

            if (isSelectionLocked && !string.Equals(value, DrillholeId, StringComparison.OrdinalIgnoreCase))
            {
                RejectWhileLocked();
                return;
            }

            drillholeSearch = value;
            Changed();
            Changed(nameof(CanClearDrillhole));
            ClearDrillholeCommand.Refresh();

            // Text that no longer names the chosen drillhole clears it (and its Project/Campaign). Typing never picks:
            // the closest match is only highlighted until Enter or a click.
            if (!string.Equals(value, DrillholeId, StringComparison.OrdinalIgnoreCase) && DrillholeId.Length > 0)
            {
                SetDrillhole("", "", "");
            }

            RequestOptions(debounce: true);
        }
    }

    /// <summary>Bound to the dropdown's SelectedItem. Picking fills Drillhole, Project and Campaign; the dropdown
    /// clearing its own selection while the list is filtered is ignored, and headings cannot be picked.</summary>
    public DrillholeOption? PickedDrillhole
    {
        get => DrillholeId.Length > 0 && catalog.Option(DrillholeId) is { } option && shownOptions.Contains(option) ? option : null;
        set
        {
            if (value?.Drillhole is not { } drillhole || string.Equals(drillhole.HoleId, DrillholeId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (isSelectionLocked)
            {
                RejectWhileLocked();
                return;
            }

            Choose(drillhole);
        }
    }

    /// <summary>Enter in the Drillhole box: picks the highlighted closest match. False when there is none.</summary>
    public bool CommitClosest()
    {
        if (ClosestOption?.Drillhole is not { } drillhole)
        {
            return false;
        }

        if (isSelectionLocked)
        {
            RejectWhileLocked();
            return true;
        }

        Choose(drillhole);
        return true;
    }

    public string DrillholeId => drillholeId;

    /// <summary>"Project" on the form (API projectCode). Read-only: filled by the selected drillhole.</summary>
    public string DrillholeName => drillholeName;

    /// <summary>"Campaign" on the form (API campaign). Read-only: filled by the selected drillhole.</summary>
    public string ProjectArea => projectArea;

    public string Operator
    {
        get => operatorName;
        set { if (Set(ref operatorName, value)) Update(); }
    }

    public string Notes
    {
        get => notes;
        set { if (Set(ref notes, value)) Update(); }
    }

    /// <summary>Loading, empty, error or "not in the latest list" message under the Drillhole box.</summary>
    public string DrillholesMessage =>
        drillholesStatus is null && catalog.Count == 0 ? "Loading drillholes…" :
        drillholesStatus is not null and not ApiStatus.Ok && catalog.Count == 0
            ? $"No drillholes available: the list could not be loaded and no saved copy exists yet. {drillholesError}" :
        drillholesStatus is not null and not ApiStatus.Ok ? $"Showing the saved drillhole list. Could not refresh: {drillholesError}" :
        drillholesStatus is ApiStatus.Ok && catalog.Count == 0 ? "The backend returned no drillholes." :
        emptyResponse ? "The backend returned no drillholes; showing the saved list." :
        DrillholeId.Length > 0 && Find(DrillholeId) is null ? $"{DrillholeId} is not in the latest drillhole list; its saved details are kept." :
        "";

    public bool CanRetryDrillholes =>
        drillholesStatus is not null and not ApiStatus.Ok || (drillholesStatus is ApiStatus.Ok && (catalog.Count == 0 || emptyResponse));

    public bool IsValid => HasRequiredText;

    /// <summary>
    /// The Windows account running the app: DOMAIN\Username for a domain account, otherwise the user name
    /// (a local account's "domain" is just the computer name, so it is left out).
    /// </summary>
    public static string CurrentWindowsAccount()
    {
        var user = Environment.UserName;
        var domain = Environment.UserDomainName;

        return string.IsNullOrWhiteSpace(domain) || string.Equals(domain, Environment.MachineName, StringComparison.OrdinalIgnoreCase)
            ? user
            : $"{domain}\\{user}";
    }

    /// <summary>Red border on the Drillhole box after Save &amp; Continue without a drillhole (the message is a toast).</summary>
    public string DrillholeIdError =>
        showErrors && string.IsNullOrWhiteSpace(DrillholeId) ? "Select a drillhole from the list." : "";

    public bool HasDrillholeError => DrillholeIdError.Length > 0;

    // ---------- Loading ----------

    /// <summary>Loads the drillhole list. A running load is reused; the current selection and form values are kept.</summary>
    public Task LoadDrillholesAsync()
    {
        if (drillholesLoad is { IsCompleted: false })
        {
            return drillholesLoad;
        }

        return drillholesLoad = LoadDrillholesCoreAsync();
    }

    /// <summary>Reloads "Measured drillholes" from the saved measurement history (on start and after each save).</summary>
    public Task RefreshMeasuredAsync()
    {
        // A refresh requested while one runs starts after it, so a save that just finished is never missed.
        var previous = measuredLoad ?? Task.CompletedTask;
        return measuredLoad = RunAfter(previous);

        async Task RunAfter(Task before)
        {
            await before;

            try
            {
                measured = await history.LoadMeasuredDrillholesAsync();
            }
            catch (Exception)
            {
                // History unreadable: keep the groups as they were; the full list stays usable.
                return;
            }

            await RequestOptions(debounce: false);
        }
    }

    /// <summary>Completes when the dropdown list reflects the latest search text, list and history.</summary>
    public async Task WhenOptionsUpdated()
    {
        Task pending;

        do
        {
            pending = optionsUpdate;
            await pending;
        }
        while (pending != optionsUpdate);
    }

    private async Task LoadDrillholesCoreAsync()
    {
        // 1. The cached list is shown at once (first load only), so the form works before the backend answers.
        if (!cacheChecked)
        {
            cacheChecked = true;

            try
            {
                var cached = await cache.LoadAsync();

                if (catalog.Count == 0 && cached.Count > 0)
                {
                    var previous = catalog;
                    catalog = await Task.Run(() => DrillholeCatalog.Build(cached, previous));
                    ShowDrillholes();
                }
            }
            catch (Exception)
            {
                // An unreadable cache is treated as empty; the backend list replaces it below.
            }
        }

        // 2. Refresh from the backend. Only a successful response with drillholes replaces the list and the cache;
        //    a failure or timeout keeps what is shown (cached or previously loaded).
        var result = await drillholesApi.GetAsync(CancellationToken.None);
        emptyResponse = false;

        if (result.IsOk)
        {
            var current = catalog;
            var fresh = await Task.Run(() => DrillholeCatalog.Build(result.Value!, current));
            emptyResponse = fresh.Count == 0 && catalog.Count > 0;

            if (fresh.Count > 0 || catalog.Count == 0)
            {
                catalog = fresh;
            }

            if (fresh.Count > 0)
            {
                try
                {
                    await cache.ReplaceAsync(fresh.All);
                }
                catch (Exception)
                {
                    // The list is still shown; the cache keeps its previous copy (the replace is all or nothing).
                }
            }
        }

        drillholesStatus = result.Status;
        drillholesError = result.Error;
        ShowDrillholes();
        await WhenOptionsUpdated();
    }

    private void ShowDrillholes()
    {
        RequestOptions(debounce: false);
        Changed(nameof(PickedDrillhole));
        Changed(nameof(DrillholesMessage));
        Changed(nameof(CanRetryDrillholes));
    }

    // ---------- Selection ----------

    private DrillholeResponse? Find(string holeId) => catalog.Find(holeId);

    private void Choose(DrillholeResponse entry)
    {
        SetDrillhole(entry.HoleId!.Trim(), entry.ProjectCode?.Trim() ?? "", entry.Campaign?.Trim() ?? "");

        if (drillholeSearch != drillholeId)
        {
            drillholeSearch = drillholeId;
            Changed(nameof(DrillholeSearch));
        }

        RequestOptions(debounce: false);
    }

    private void SetDrillhole(string id, string project, string campaign)
    {
        if (drillholeId == id && drillholeName == project && projectArea == campaign)
        {
            return;
        }

        var drillholeChanged = drillholeId != id;
        drillholeId = id;
        drillholeName = project;
        projectArea = campaign;
        Changed(nameof(DrillholeId));
        Changed(nameof(CanClearDrillhole));
        ClearDrillholeCommand.Refresh();
        Changed(nameof(DrillholeName));
        Changed(nameof(ProjectArea));
        Changed(nameof(PickedDrillhole));
        Changed(nameof(DrillholesMessage));
        Update();

        if (drillholeChanged)
        {
            DrillholeChanged?.Invoke(id);
        }
    }

    private void ClearDrillhole()
    {
        if (isSelectionLocked)
        {
            RejectWhileLocked();
            return;
        }

        drillholeSearch = "";
        Changed(nameof(DrillholeSearch));
        SetDrillhole("", "", "");
        Changed(nameof(CanClearDrillhole));
        ClearDrillholeCommand.Refresh();
        RequestOptions(debounce: false);
    }

    private void RejectWhileLocked()
    {
        Toasts.Error("Stop the measurement before changing the drillhole.");

        // Put the box back on the drillhole being measured.
        Changed(nameof(DrillholeSearch));
        Changed(nameof(PickedDrillhole));
    }

    // Filtering runs on the thread pool with a snapshot of the inputs. Typing waits for a short pause first; any newer
    // request makes an older one stale, so an old result can never replace a newer one.
    private Task RequestOptions(bool debounce)
    {
        var version = ++optionsVersion;
        return optionsUpdate = UpdateOptionsAsync(version, debounce, catalog, measured, drillholeSearch.Trim(), drillholeId);
    }

    private async Task UpdateOptionsAsync(
        int version, bool debounce, DrillholeCatalog source, IReadOnlyList<MeasuredDrillhole> history, string text, string picked)
    {
        if (debounce && searchDelay > TimeSpan.Zero)
        {
            await Task.Delay(searchDelay);
        }

        if (version != optionsVersion)
        {
            return;
        }

        var result = await Task.Run(() => source.Options(history, text, picked));

        if (version == optionsVersion)
        {
            Apply(result.Entries);
            ClosestOption = result.Closest;
        }
    }

    // Applies only the difference to the bound collection: unchanged entries keep their instances (and the dropdown
    // its selection); a large change is one Reset notification instead of thousands of item notifications.
    private void Apply(IReadOnlyList<DrillholeOption> options)
    {
        var current = DrillholeOptions;

        if (current.Count == options.Count && current.Zip(options).All(p => ReferenceEquals(p.First, p.Second)))
        {
            return;
        }

        var prefix = 0;

        while (prefix < current.Count && prefix < options.Count && ReferenceEquals(current[prefix], options[prefix]))
        {
            prefix++;
        }

        var suffix = 0;

        while (suffix < current.Count - prefix && suffix < options.Count - prefix &&
               ReferenceEquals(current[current.Count - 1 - suffix], options[options.Count - 1 - suffix]))
        {
            suffix++;
        }

        var removed = current.Count - prefix - suffix;
        var added = options.Count - prefix - suffix;
        rebuildingOptions = true;

        try
        {
            if (removed + added <= IncrementalChangeLimit)
            {
                for (var i = 0; i < removed; i++)
                {
                    current.RemoveAt(prefix);
                }

                for (var i = 0; i < added; i++)
                {
                    current.Insert(prefix + i, options[prefix + i]);
                }
            }
            else
            {
                current.ReplaceAll(options);
            }

            shownOptions = new HashSet<DrillholeOption>(options, ReferenceEqualityComparer.Instance);
        }
        finally
        {
            rebuildingOptions = false;
        }

        // Put the dropdown back on the picked entry and its text (a rebuild may have cleared both in the control).
        Changed(nameof(PickedDrillhole));
        Changed(nameof(DrillholeSearch));
    }

    // ---------- Saving ----------

    private bool HasRequiredText => !string.IsNullOrWhiteSpace(DrillholeId);

    private void Save(Action<CoreSnapshot> saved)
    {
        showErrors = true;
        RefreshValidation();

        if (!HasRequiredText)
        {
            Toasts.Error("Select a drillhole from the list.");
            return;
        }

        // An emptied Operator is filled with the signed-in account again; an edited one is kept as entered.
        if (string.IsNullOrWhiteSpace(Operator))
        {
            Operator = CurrentWindowsAccount();
        }

        saved(new CoreSnapshot(
            DrillholeId.Trim(),
            DrillholeName.Trim(),
            ProjectArea.Trim(),
            Operator.Trim(),
            Notes.Trim()));
    }

    private void Update()
    {
        RefreshValidation();
        Edited?.Invoke();
    }

    private void RefreshValidation()
    {
        Changed(nameof(DrillholeIdError));
        Changed(nameof(HasDrillholeError));
    }
}
