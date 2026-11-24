using Frontend.Api;
using Frontend.Api.CoreInformation.Drillholes;
using Frontend.Calculation.CoreInformation;
using Frontend.Pages.CoreInformation.DeviceStatus;
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
    private readonly Action<CoreSnapshot> saved;
    private bool isNewMode;
    private bool newDrillholeIdTouched;
    private bool saving;
    private string newDrillholeId = "";

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
        DeviceStatus = new DeviceStatusViewModel(devices, Toasts, this.dialogs, () => isSelectionLocked);
        operatorName = WindowsAccount.Current();
        this.saved = saved;
        SaveCommand = new RelayCommand(_ => _ = SaveAsync(), _ => !saving && (!isNewMode || CanSaveNew));
        RefreshDrillholesCommand = new RelayCommand(_ => _ = LoadDrillholesAsync());
        ClearDrillholeCommand = new RelayCommand(_ => ClearDrillhole(), _ => CanClearDrillhole);
        SelectExistingDuplicateCommand = new RelayCommand(_ => SelectExistingDuplicate(), _ => DuplicateDrillhole is not null);
        NewProject.InputChanged += NewDrillholeInputChanged;
        NewCampaign.InputChanged += NewDrillholeInputChanged;

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

                // Measuring needs a saved drillhole: a New drillhole that was not saved is dropped.
                if (value && isNewMode)
                {
                    ResetNewDrillhole();
                    SetNewMode(false);
                }

                Changed(nameof(CanSwitchMode));
                SaveCommand.Refresh();
            }
        }
    }

    // ── Existing / New ────────────────────────────────────────────────────────────────────────────────────────────
    // Existing: pick a drillhole from the list (Project and Campaign are filled from it). New: type a Drillhole ID and
    // choose or add its Project and Campaign in the same row; Save & Continue creates it on this computer (never sent
    // to the central drillhole list), selects it and continues, all in one step.

    /// <summary>True in New mode (create a drillhole), false in Existing mode (pick one).</summary>
    public bool IsNewMode => isNewMode;

    /// <summary>"New" radio button: only a check acts (the group unchecking it is ignored).</summary>
    public bool IsNewModeSelected
    {
        get => isNewMode;
        set
        {
            if (value)
            {
                SwitchMode(toNew: true);
            }
        }
    }

    /// <summary>"Existing" radio button: only a check acts (the group unchecking it is ignored).</summary>
    public bool IsExistingModeSelected
    {
        get => !isNewMode;
        set
        {
            if (value)
            {
                SwitchMode(toNew: false);
            }
        }
    }

    /// <summary>The switch is disabled while measuring: the drillhole cannot change until Stop.</summary>
    public bool CanSwitchMode => !isSelectionLocked;

    /// <summary>
    /// Switches between Existing and New. To New: the selected drillhole is cleared, so only one is ever active.
    /// New → Existing with something typed or picked asks "Discard new drillhole?" first. False when refused.
    /// </summary>
    public bool SwitchMode(bool toNew)
    {
        if (toNew == isNewMode)
        {
            return true;
        }

        if (isSelectionLocked)
        {
            RejectWhileLocked();
            NotifyMode();
            return false;
        }

        if (toNew)
        {
            ClearDrillhole();
            showErrors = false;
            Changed(nameof(HasDrillholeError));
        }
        else if (HasNewInput && !dialogs.ConfirmDiscardNewDrillhole())
        {
            NotifyMode();
            return false;
        }

        ResetNewDrillhole();
        SetNewMode(toNew);
        return true;
    }

    private void SetNewMode(bool value)
    {
        isNewMode = value;
        NotifyMode();
        SaveCommand.Refresh();
    }

    private void NotifyMode()
    {
        Changed(nameof(IsNewMode));
        Changed(nameof(IsNewModeSelected));
        Changed(nameof(IsExistingModeSelected));
    }

    /// <summary>Drillhole ID typed in New mode.</summary>
    public string NewDrillholeId
    {
        get => newDrillholeId;
        set
        {
            if (Set(ref newDrillholeId, value ?? ""))
            {
                newDrillholeIdTouched = true;
                NewDrillholeInputChanged();
            }
        }
    }

    /// <summary>Project of the new drillhole: picked from the list or added (stored like the API projectCode).</summary>
    public NewNameField NewProject { get; } = new("Project");

    /// <summary>Campaign of the new drillhole: picked from the list or added (stored like the API campaign).</summary>
    public NewNameField NewCampaign { get; } = new("Campaign");

    /// <summary>The drillhole that already has the typed ID (any letter case), or null.</summary>
    public DrillholeResponse? DuplicateDrillhole =>
        newDrillholeId.Trim() is { Length: > 0 } id ? catalog.Find(id) : null;

    public bool IsNewDrillholeIdDuplicate => DuplicateDrillhole is not null;

    /// <summary>Shown under the Drillhole ID box: required once touched, or the ID already exists.</summary>
    public string NewDrillholeIdError =>
        newDrillholeId.Trim().Length == 0 ? (newDrillholeIdTouched ? "Drillhole ID is required." : "") :
        IsNewDrillholeIdDuplicate ? "Drillhole already exists — switch to Existing to select it." : "";

    public bool IsNewDrillholeIdInvalid => NewDrillholeIdError.Length > 0;

    /// <summary>The ID box shows "required" (the duplicate message has its own line with a link).</summary>
    public bool IsNewDrillholeIdRequired => IsNewDrillholeIdInvalid && !IsNewDrillholeIdDuplicate;

    /// <summary>Drillhole ID, Project and Campaign are all filled in and valid: Save &amp; Continue is enabled.</summary>
    public bool CanSaveNew =>
        newDrillholeId.Trim().Length > 0 && !IsNewDrillholeIdDuplicate && NewProject.IsValid && NewCampaign.IsValid;

    /// <summary>Something was typed or picked in New mode (switching to Existing then asks first).</summary>
    public bool HasNewInput => newDrillholeId.Trim().Length > 0 || NewProject.HasInput || NewCampaign.HasInput;

    /// <summary>The link in the "already exists" message: switch to Existing and select that drillhole.</summary>
    public RelayCommand SelectExistingDuplicateCommand { get; }

    /// <summary>Focus left a New field: an empty one now shows "required". <paramref name="field"/>: id, project, campaign.</summary>
    public void MarkTouched(string field)
    {
        switch (field)
        {
            case "id":
                if (!newDrillholeIdTouched)
                {
                    newDrillholeIdTouched = true;
                    NewDrillholeInputChanged();
                }

                break;
            case "project":
                NewProject.MarkTouched();
                break;
            case "campaign":
                NewCampaign.MarkTouched();
                break;
        }
    }

    private void SelectExistingDuplicate()
    {
        if (DuplicateDrillhole is not { } existing || isSelectionLocked)
        {
            return;
        }

        ResetNewDrillhole();
        SetNewMode(false);
        Choose(existing);
    }

    private void ResetNewDrillhole()
    {
        newDrillholeId = "";
        newDrillholeIdTouched = false;
        Changed(nameof(NewDrillholeId));
        NewProject.Reset();
        NewCampaign.Reset();
        NewDrillholeInputChanged();
    }

    private void NewDrillholeInputChanged()
    {
        Changed(nameof(DuplicateDrillhole));
        Changed(nameof(IsNewDrillholeIdDuplicate));
        Changed(nameof(NewDrillholeIdError));
        Changed(nameof(IsNewDrillholeIdInvalid));
        Changed(nameof(IsNewDrillholeIdRequired));
        Changed(nameof(CanSaveNew));
        Changed(nameof(HasNewInput));
        SaveCommand.Refresh();
        SelectExistingDuplicateCommand.Refresh();
    }

    // Project and Campaign dropdowns in New mode: every name in the list, including drillholes created here.
    private void RefreshNameChoices()
    {
        NewProject.SetExisting(catalog.All.Select(d => d.ProjectCode));
        NewCampaign.SetExisting(catalog.All.Select(d => d.Campaign));
        NewDrillholeInputChanged();
    }

    /// <summary>
    /// Save &amp; Continue. Existing mode: saves the picked drillhole and continues. New mode: stores the new drillhole on
    /// this computer, switches back to Existing with it selected, and continues. False when nothing was saved.
    /// </summary>
    public async Task<bool> SaveAsync()
    {
        if (saving)
        {
            return false;
        }

        if (!isNewMode)
        {
            return Save();
        }

        if (!CanSaveNew)
        {
            MarkTouched("id");
            MarkTouched("project");
            MarkTouched("campaign");
            return false;
        }

        var entry = new DrillholeResponse(newDrillholeId.Trim(), NewProject.Value, NewCampaign.Value);
        saving = true;
        SaveCommand.Refresh();

        try
        {
            await cache.AddLocalAsync(entry);
        }
        catch (InvalidOperationException)
        {
            // Created meanwhile (another window of the app): list it so the duplicate message and link appear.
            localDrillholes = [.. await cache.LoadLocalAsync()];
            catalog = DrillholeCatalog.Build(catalog.Remote, catalog, localDrillholes);
            ShowDrillholes();
            Toasts.Error(NewDrillholeIdError.Length > 0 ? NewDrillholeIdError : $"Drillhole {entry.HoleId} already exists.");
            return false;
        }
        catch (Exception error)
        {
            Toasts.Error($"The drillhole could not be saved: {error.Message}");
            return false;
        }
        finally
        {
            saving = false;
            SaveCommand.Refresh();
        }

        localDrillholes = [.. localDrillholes, entry];
        catalog = DrillholeCatalog.Build(catalog.Remote, catalog, localDrillholes);
        ResetNewDrillhole();
        SetNewMode(false);
        ShowDrillholes();
        Choose(catalog.Find(entry.HoleId!)!);
        await WhenOptionsUpdated();
        Toasts.Success($"Drillhole {entry.HoleId} created.");
        return Save();
    }

    /// <summary>Highlighted best match for the typed text; picked only on Enter (<see cref="CommitClosest"/>) or a click.</summary>
    public DrillholeOption? ClosestOption
    {
        get => closestOption;
        private set => Set(ref closestOption, value);
    }

    public RelayCommand SaveCommand { get; }

    public RelayCommand RefreshDrillholesCommand { get; }

    /// <summary>The Device Status card beside the drillhole form.</summary>
    public DeviceStatusViewModel DeviceStatus { get; }

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
                    catalog = await Task.Run(() => DrillholeCatalog.Build(cached, previous, local));
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
                catalog = await Task.Run(() => DrillholeCatalog.Build(fresh.All, current, local));
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
        RefreshNameChoices();
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

    private bool HasDrillhole => !string.IsNullOrWhiteSpace(DrillholeId);

    private bool Save()
    {
        showErrors = true;
        Changed(nameof(HasDrillholeError));

        if (!HasDrillhole)
        {
            Toasts.Error("Select a drillhole from the list.");
            return false;
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

        return true;
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
