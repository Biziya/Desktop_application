using Frontend.Api;
using Frontend.Api.CoreInformation.Drillholes;
using Frontend.Calculation.CoreInformation;
using Frontend.Pages.CoreInformation.DrillholeInformation;
using Frontend.Pages.Measurement;
using Frontend.Shared.Infrastructure;
using Frontend.Shared.Models;
using Frontend.Windows.ConfirmationWindow;

namespace Frontend.Pages.CoreInformation;

/// <summary>
/// Core Information form and Device Status. Labels were renamed but storage was not: Project = <see cref="DrillholeName"/>
/// (API projectCode), Campaign = <see cref="ProjectArea"/> (API campaign, SQLite Project).
/// </summary>
public sealed class CoreInformationViewModel : ObservableObject
{
    // Small changes to the dropdown list are applied item by item (the selection survives); bigger ones in one Reset.
    private const int IncrementalChangeLimit = 64;

    private readonly DrillholesClient drillholesApi;
    private readonly DrillholeCache cache;
    private readonly IMeasurementRepository history;
    private readonly IDialogService dialogs;
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
    private IReadOnlyList<DrillholeResponse> localDrillholes = [];
    private bool isCreatingDrillhole;
    private bool showNewDrillholeErrors;
    private bool savingNewDrillhole;
    private string newDrillholeId = "";
    private string newProject = "";
    private string newCampaign = "";
    private string newDrillholeIdTaken = "";

    /// <param name="searchDelay">Pause after the last keystroke before the list is filtered (default 150 ms).</param>
    public CoreInformationViewModel(
        Action<CoreSnapshot> saved,
        DrillholesClient drillholesApi,
        BackendConnection backend,
        DrillholeCache cache,
        IMeasurementRepository history,
        DeviceStatusMonitor devices,
        TimeSpan? searchDelay = null,
        ToastService? toasts = null,
        IDialogService? dialogs = null)
    {
        Toasts = toasts ?? new ToastService();
        this.drillholesApi = drillholesApi;
        this.cache = cache;
        this.history = history;
        this.dialogs = dialogs ?? new DialogService();
        this.searchDelay = searchDelay ?? TimeSpan.FromMilliseconds(150);
        Devices = devices;
        operatorName = WindowsAccount.Current();
        SaveCommand = new RelayCommand(_ => Save(saved));
        RefreshDrillholesCommand = new RelayCommand(_ => _ = LoadDrillholesAsync());
        ClearDrillholeCommand = new RelayCommand(_ => ClearDrillhole(), _ => CanClearDrillhole);
        RefreshMeasurementDatabaseCommand = new RelayCommand(_ => _ = devices.CheckMeasurementDatabaseAsync());
        RefreshGammaGuardDatabaseCommand = new RelayCommand(_ => _ = devices.CheckGammaGuardDatabaseAsync(rediscover: true));
        SelectGammaGuardDatabaseCommand = new AsyncCommand(SelectGammaGuardDatabaseAsync,
            error => Toasts.Error($"The Gamma Guard database could not be changed: {error.Message}"));
        NewDrillholeCommand = new RelayCommand(_ => BeginNewDrillhole(), _ => !isSelectionLocked && !isCreatingDrillhole);
        SaveNewDrillholeCommand = new RelayCommand(_ => _ = SaveNewDrillholeAsync(), _ => isCreatingDrillhole && !savingNewDrillhole);
        CancelNewDrillholeCommand = new RelayCommand(_ => IsCreatingDrillhole = false, _ => isCreatingDrillhole);

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

    public ToastService Toasts { get; }

    /// <summary>Clears the search text and the selected drillhole; Operator, Notes and stored data are kept.</summary>
    public RelayCommand ClearDrillholeCommand { get; }

    public bool CanClearDrillhole => !isSelectionLocked && (drillholeSearch.Length > 0 || DrillholeId.Length > 0);

    /// <summary>Set while measuring, so a running capture can never be assigned to another drillhole.</summary>
    public bool IsSelectionLocked
    {
        get => isSelectionLocked;
        set
        {
            if (Set(ref isSelectionLocked, value))
            {
                Changed(nameof(CanClearDrillhole));
                ClearDrillholeCommand.Refresh();

                if (value)
                {
                    IsCreatingDrillhole = false;
                }

                NewDrillholeCommand.Refresh();
            }
        }
    }

    /// <summary>Opens the New drillhole form (Drillhole ID, Campaign and Project, all required).</summary>
    public RelayCommand NewDrillholeCommand { get; }

    public RelayCommand SaveNewDrillholeCommand { get; }

    public RelayCommand CancelNewDrillholeCommand { get; }

    /// <summary>The New drillhole form is open. Closing it discards what was typed.</summary>
    public bool IsCreatingDrillhole
    {
        get => isCreatingDrillhole;
        private set
        {
            if (Set(ref isCreatingDrillhole, value))
            {
                NewDrillholeCommand.Refresh();
                SaveNewDrillholeCommand.Refresh();
                CancelNewDrillholeCommand.Refresh();
            }
        }
    }

    public string NewDrillholeId
    {
        get => newDrillholeId;
        set
        {
            if (Set(ref newDrillholeId, value ?? ""))
            {
                NewDrillholeInputChanged();
            }
        }
    }

    /// <summary>Project of the new drillhole (stored like the API projectCode).</summary>
    public string NewProject
    {
        get => newProject;
        set
        {
            if (Set(ref newProject, value ?? ""))
            {
                NewDrillholeInputChanged();
            }
        }
    }

    /// <summary>Campaign of the new drillhole (stored like the API campaign).</summary>
    public string NewCampaign
    {
        get => newCampaign;
        set
        {
            if (Set(ref newCampaign, value ?? ""))
            {
                NewDrillholeInputChanged();
            }
        }
    }

    /// <summary>Shown under the Drillhole ID box after a save attempt; empty when valid.</summary>
    public string NewDrillholeIdError =>
        !showNewDrillholeErrors ? "" :
        newDrillholeId.Trim().Length == 0 ? "Drillhole ID is required." :
        newDrillholeIdTaken.Length > 0 && string.Equals(newDrillholeId.Trim(), newDrillholeIdTaken, StringComparison.OrdinalIgnoreCase)
            ? $"{newDrillholeIdTaken} already exists. Pick it from the Drillhole list." : "";

    public string NewCampaignError => showNewDrillholeErrors && newCampaign.Trim().Length == 0 ? "Campaign is required." : "";

    public string NewProjectError => showNewDrillholeErrors && newProject.Trim().Length == 0 ? "Project is required." : "";

    public bool HasNewDrillholeErrors => IsNewDrillholeIdInvalid || IsNewCampaignInvalid || IsNewProjectInvalid;

    public bool IsNewDrillholeIdInvalid => NewDrillholeIdError.Length > 0;

    public bool IsNewCampaignInvalid => NewCampaignError.Length > 0;

    public bool IsNewProjectInvalid => NewProjectError.Length > 0;

    private void BeginNewDrillhole()
    {
        if (isSelectionLocked)
        {
            RejectWhileLocked();
            return;
        }

        showNewDrillholeErrors = false;
        newDrillholeIdTaken = "";
        newDrillholeId = drillholeSearch.Trim().Length > 0 && catalog.Find(drillholeSearch) is null ? drillholeSearch.Trim() : "";
        newProject = "";
        newCampaign = "";
        NewDrillholeInputChanged();
        Changed(nameof(NewDrillholeId));
        Changed(nameof(NewProject));
        Changed(nameof(NewCampaign));
        IsCreatingDrillhole = true;
    }

    private void NewDrillholeInputChanged()
    {
        Changed(nameof(NewDrillholeIdError));
        Changed(nameof(NewCampaignError));
        Changed(nameof(NewProjectError));
        Changed(nameof(IsNewDrillholeIdInvalid));
        Changed(nameof(IsNewCampaignInvalid));
        Changed(nameof(IsNewProjectInvalid));
        Changed(nameof(HasNewDrillholeErrors));
    }

    /// <summary>
    /// Validates and stores the new drillhole, then selects it. Saving is refused (false, with a message under each
    /// empty box and one error toast) while Drillhole ID, Campaign or Project is empty, or when the ID already exists.
    /// </summary>
    public async Task<bool> SaveNewDrillholeAsync()
    {
        if (!isCreatingDrillhole || savingNewDrillhole)
        {
            return false;
        }

        var id = newDrillholeId.Trim();
        var project = newProject.Trim();
        var campaign = newCampaign.Trim();
        newDrillholeIdTaken = id.Length > 0 && catalog.Find(id) is { } existing ? existing.HoleId! : "";
        showNewDrillholeErrors = true;
        NewDrillholeInputChanged();

        if (HasNewDrillholeErrors)
        {
            var missing = new[] { id.Length == 0 ? "Drillhole ID" : null, campaign.Length == 0 ? "Campaign" : null, project.Length == 0 ? "Project" : null }
                .OfType<string>().ToList();
            Toasts.Error(missing.Count > 0
                ? $"The drillhole was not saved: {string.Join(" and ", missing)} {(missing.Count == 1 ? "is" : "are")} required."
                : NewDrillholeIdError);
            return false;
        }

        var entry = new DrillholeResponse(id, project, campaign);
        savingNewDrillhole = true;
        SaveNewDrillholeCommand.Refresh();

        try
        {
            await cache.AddLocalAsync(entry);
        }
        catch (InvalidOperationException)
        {
            newDrillholeIdTaken = id;
            NewDrillholeInputChanged();
            Toasts.Error(NewDrillholeIdError);
            return false;
        }
        catch (Exception error)
        {
            Toasts.Error($"The drillhole could not be saved: {error.Message}");
            return false;
        }
        finally
        {
            savingNewDrillhole = false;
            SaveNewDrillholeCommand.Refresh();
        }

        localDrillholes = [.. localDrillholes, entry];
        var previous = catalog;
        catalog = DrillholeCatalog.Build(previous.All.Append(entry), previous);
        IsCreatingDrillhole = false;
        ShowDrillholes();
        Choose(catalog.Find(id)!);
        await WhenOptionsUpdated();
        Toasts.Success($"Drillhole {id} created and selected.");
        return true;
    }

    /// <summary>Highlighted best match for the typed text; picked only on Enter (<see cref="CommitClosest"/>) or a click.</summary>
    public DrillholeOption? ClosestOption
    {
        get => closestOption;
        private set => Set(ref closestOption, value);
    }

    public RelayCommand SaveCommand { get; }

    public RelayCommand RefreshDrillholesCommand { get; }

    public RelayCommand RefreshMeasurementDatabaseCommand { get; }

    public RelayCommand RefreshGammaGuardDatabaseCommand { get; }

    public AsyncCommand SelectGammaGuardDatabaseCommand { get; }

    public DeviceStatusMonitor Devices { get; }

    /// <summary>Matches for <see cref="DrillholeSearch"/>: "Measured drillholes" (most recent first), then "All drillholes".</summary>
    public BulkObservableCollection<DrillholeOption> DrillholeOptions { get; } = [];

    /// <summary>Text typed in the Drillhole box; filters the list after a short pause, off the UI thread.</summary>
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

            // Text that no longer names the chosen drillhole clears it; typing never picks.
            if (!string.Equals(value, DrillholeId, StringComparison.OrdinalIgnoreCase) && DrillholeId.Length > 0)
            {
                SetDrillhole("", "", "");
            }

            RequestOptions(debounce: true);
        }
    }

    /// <summary>Dropdown SelectedItem; the control clearing its own selection while filtering is ignored.</summary>
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

    /// <summary>"Project" on the form (API projectCode).</summary>
    public string DrillholeName => drillholeName;

    /// <summary>"Campaign" on the form (API campaign).</summary>
    public string ProjectArea => projectArea;

    /// <summary>Leading spaces and line breaks are dropped as they are typed.</summary>
    public string Operator
    {
        get => operatorName;
        set => SetTrimmed(ref operatorName, value);
    }

    /// <summary>Leading spaces and line breaks are dropped as they are typed; inner and trailing text is kept.</summary>
    public string Notes
    {
        get => notes;
        set => SetTrimmed(ref notes, value);
    }

    public string DrillholesMessage
    {
        get
        {
            if (catalog.Count == 0)
            {
                return drillholesStatus is null ? "Loading drillholes…" :
                    DrillholesFailed ? $"No drillholes available: the list could not be loaded and no saved copy exists yet. {drillholesError}" :
                    "The backend returned no drillholes.";
            }

            if (DrillholesFailed)
            {
                return $"Showing the saved drillhole list. Could not refresh: {drillholesError}";
            }

            if (emptyResponse)
            {
                return "The backend returned no drillholes; showing the saved list.";
            }

            return DrillholeId.Length > 0 && catalog.Find(DrillholeId) is null
                ? $"{DrillholeId} is not in the latest drillhole list; its saved details are kept."
                : "";
        }
    }

    public bool CanRetryDrillholes => DrillholesFailed || (drillholesStatus is ApiStatus.Ok && (catalog.Count == 0 || emptyResponse));

    // The latest load got no usable answer (offline, HTTP error, unexpected data).
    private bool DrillholesFailed => drillholesStatus is not null and not ApiStatus.Ok;

    public bool HasDrillholeError => showErrors && !HasDrillhole;

    /// <summary>Loads the drillhole list, reusing a running load.</summary>
    public Task LoadDrillholesAsync()
    {
        if (drillholesLoad is { IsCompleted: false })
        {
            return drillholesLoad;
        }

        return drillholesLoad = LoadDrillholesCoreAsync();
    }

    /// <summary>Reloads "Measured drillholes" from the saved measurement history.</summary>
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
        // Show the cached list first so the form works before the backend answers.
        if (!cacheChecked)
        {
            cacheChecked = true;

            try
            {
                localDrillholes = await cache.LoadLocalAsync();
            }
            catch (Exception)
            {
                // Unreadable: no local drillholes are listed; creating one reports the error.
            }

            try
            {
                var cached = await cache.LoadAsync();

                if (catalog.Count == 0 && (cached.Count > 0 || localDrillholes.Count > 0))
                {
                    var previous = catalog;
                    var local = localDrillholes;
                    catalog = await Task.Run(() => DrillholeCatalog.Build(cached.Concat(local), previous));
                    ShowDrillholes();
                }
            }
            catch (Exception)
            {
                // An unreadable cache is treated as empty; the backend list replaces it below.
            }
        }

        // Only a successful, non-empty response replaces the list and the cache. Drillholes created on this laptop
        // are always added (the backend entry wins when the same Hole ID exists there).
        var result = await drillholesApi.GetAsync(CancellationToken.None);
        emptyResponse = false;

        if (result.IsOk)
        {
            var current = catalog;
            var fresh = await Task.Run(() => DrillholeCatalog.Build(result.Value!, current));
            emptyResponse = fresh.Count == 0 && catalog.Count > localDrillholes.Count;

            if (fresh.Count > 0 || catalog.Count <= localDrillholes.Count)
            {
                var local = localDrillholes;
                catalog = await Task.Run(() => DrillholeCatalog.Build(fresh.All.Concat(local), current));
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

    // Applies only the difference so the dropdown keeps its selection; a large change is one Reset notification.
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

    // Cancelling the dialog or choosing an unusable file keeps the current database. Not while measuring: the
    // samples of one drillhole run come from one database.
    private async Task SelectGammaGuardDatabaseAsync()
    {
        if (isSelectionLocked)
        {
            Toasts.Error("Stop the measurement before changing the Gamma Guard database.");
            return;
        }

        if (dialogs.PickGammaGuardDatabase(Devices.GammaGuardDatabase.Path) is not { } path)
        {
            return;
        }

        var status = await Devices.SelectGammaGuardDatabaseAsync(path);

        if (status.State == LocalState.Available)
        {
            Toasts.Success($"Gamma Guard database selected: {status.Path}");
        }
        else
        {
            Toasts.Error($"The Gamma Guard database was not changed. {status.Text}: {status.Detail}");
        }
    }

    private bool HasDrillhole => !string.IsNullOrWhiteSpace(DrillholeId);

    private void Save(Action<CoreSnapshot> saved)
    {
        showErrors = true;
        Changed(nameof(HasDrillholeError));

        if (!HasDrillhole)
        {
            Toasts.Error("Select a drillhole from the list.");
            return;
        }

        // An emptied Operator is filled with the signed-in account again; an edited one is kept as entered.
        if (string.IsNullOrWhiteSpace(Operator))
        {
            Operator = WindowsAccount.Current();
        }

        saved(new CoreSnapshot(
            DrillholeId.Trim(),
            DrillholeName.Trim(),
            ProjectArea.Trim(),
            Operator.Trim(),
            Notes.Trim()));
    }

    // A text field that cannot start with whitespace. When only leading whitespace was typed the stored value does not
    // change, but the notification still goes out so the box drops what was typed.
    private void SetTrimmed(ref string field, string? value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        var trimmed = (value ?? "").TrimStart();

        if (Set(ref field, trimmed, name))
        {
            Update();
        }
        else if (trimmed != value)
        {
            Changed(name);
        }
    }

    private void Update()
    {
        Changed(nameof(HasDrillholeError));
        Edited?.Invoke();
    }
}
