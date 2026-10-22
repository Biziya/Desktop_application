using System.Collections.ObjectModel;
using System.IO;
using Frontend.Calculation.Measurement;
using Frontend.Shared.Infrastructure;
using Frontend.Shared.Models;

namespace Frontend.Pages.Measurement;

public enum MeasurementState
{
    Idle,
    Ready,
    Capturing,
    Processing
}

public sealed class MeasurementViewModel : ObservableObject, IDisposable
{
    private readonly IMeasurementService measurement;
    private readonly ICsvExportService export;
    private readonly IDialogService dialogs;
    private readonly IMeasurementRepository repository;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? activeCapture;
    private CoreSnapshot? savedCore;
    private bool disposed;
    private MeasurementState state = MeasurementState.Idle;
    private string countdown;
    private readonly CaptureDurationSettings durationSettings;
    private TimeSpan captureDuration;
    private bool isEditingDuration;
    private string durationText = "";
    private int viewVersion;
    private double captureProgress;
    private int nextCapture = 1;
    private MeasurementSession? session;
    private bool hasUnsavedRows;
    private bool openingSession;
    private bool depthsTypedWhileOpening;
    private string nextFrom = "";
    private string step = "";
    private long? nextFromMm;
    private long? stepMm;
    private readonly CpsAlarm alarm;
    private string threshold;
    private double? thresholdCps;

    // Database writes run one after another, in the order they were requested. Never faults.
    private Task persistence = Task.CompletedTask;

    public MeasurementViewModel(
        IMeasurementService measurement,
        ICsvExportService export,
        IDialogService dialogs,
        IMeasurementRepository repository,
        bool showDemoTsMs = false,
        ToastService? toasts = null,
        CaptureDurationSettings? durationSettings = null,
        CpsAlarm? alarm = null,
        double? defaultThreshold = null)
    {
        ShowDemoTsMs = showDemoTsMs;
        this.alarm = alarm ?? new CpsAlarm(() => { }, TimeSpan.Zero);
        thresholdCps = defaultThreshold;
        threshold = defaultThreshold is { } initial ? CpsAlarm.FormatThreshold(initial) : "";
        this.measurement = measurement;
        this.export = export;
        this.dialogs = dialogs;
        this.repository = repository;
        Toasts = toasts ?? new ToastService();
        this.durationSettings = durationSettings ?? new CaptureDurationSettings(null);
        captureDuration = TimeSpan.FromSeconds(this.durationSettings.Load());
        countdown = FormatCountdown(captureDuration);

        // Start needs a valid drillhole (saved Core Information with its session). Invalid depths do not disable it:
        // clicking Start then explains what is missing in a toast (validation on the attempt, not per keystroke).
        ToggleMeasurementCommand = new RelayCommand(
            _ => ToggleMeasurementMode(),
            _ => IsMeasurementModeActive || (SessionMatchesCore && !openingSession));

        EditDurationCommand = new RelayCommand(_ => BeginEditDuration(), _ => !IsCapturing && !isEditingDuration);
        SaveDurationCommand = new RelayCommand(_ => SaveDuration(), _ => isEditingDuration);
        CancelDurationCommand = new RelayCommand(_ => IsEditingDuration = false, _ => isEditingDuration);

        TriggerCommand = new RelayCommand(
            parameter =>
            {
                // CaptureAsync contains all cancellation/error handling. Execution starts synchronously
                // so the state gate closes before another routed mouse event can be accepted.
                _ = CaptureAsync(parameter is DateTimeOffset timestamp ? timestamp : DateTimeOffset.Now);
            },
            _ => CanTrigger);

        ExportCommand = new AsyncCommand(
            ExportAsync,
            _ => Notify(ToastKind.Error, "Export failed. Check the file location and try again."),
            () => Results.Count > 0);

        DeleteCommand = new RelayCommand(Delete, parameter => parameter is MeasurementResult row && Results.Contains(row));

        NewMeasurementCommand = new RelayCommand(_ => NewMeasurement(), _ => !IsMeasurementModeActive && session is not null);

        Results.CollectionChanged += (_, change) =>
        {
            AssignDemoTsMs(change.NewItems);
            ExportCommand.Refresh();
        };
    }

    public ObservableCollection<MeasurementResult> Results { get; } = [];

    /// <summary>Raised after measurement rows were saved successfully (e.g. to refresh "Measured drillholes").</summary>
    public event Action? MeasurementsSaved;

    public RelayCommand ToggleMeasurementCommand { get; }

    public RelayCommand TriggerCommand { get; }

    public AsyncCommand ExportCommand { get; }

    public RelayCommand DeleteCommand { get; }

    /// <summary>Closes the current drillhole: the next Save &amp; Continue opens the session of the drillhole entered then.</summary>
    public RelayCommand NewMeasurementCommand { get; }

    /// <summary>
    /// DEMO / PREVIEW MODE: rows without a device ts_ms get a random one so the table can be previewed.
    /// Turn off (MainViewModel) once the device provides ts_ms. Demo values are never saved or exported.
    /// </summary>
    public bool ShowDemoTsMs { get; }

    public MeasurementState State
    {
        get => state;
        private set
        {
            if (Set(ref state, value))
            {
                Changed(nameof(IsMeasurementModeActive));
                RefreshCommands();
            }
        }
    }

    public bool IsMeasurementModeActive => State != MeasurementState.Idle;

    public bool IsCapturing => State is MeasurementState.Capturing or MeasurementState.Processing;

    public bool CanTrigger =>
        State == MeasurementState.Ready && SessionMatchesCore && HasValidDepths && activeCapture is null && !isEditingDuration;

    public string Countdown
    {
        get => countdown;
        private set => Set(ref countdown, value);
    }

    public double CaptureProgress
    {
        get => captureProgress;
        private set => Set(ref captureProgress, value);
    }

    /// <summary>The app's toasts (results and errors of measurement actions).</summary>
    public ToastService Toasts { get; }

    // ---------- Capture duration (click the countdown to edit) ----------

    /// <summary>Length of the next capture. A running capture keeps the duration it started with.</summary>
    public TimeSpan CaptureDuration => captureDuration;

    public RelayCommand EditDurationCommand { get; }

    public RelayCommand SaveDurationCommand { get; }

    public RelayCommand CancelDurationCommand { get; }

    /// <summary>The duration editor is open. While open, right-click captures are not accepted.</summary>
    public bool IsEditingDuration
    {
        get => isEditingDuration;
        private set
        {
            if (Set(ref isEditingDuration, value))
            {
                RefreshCommands();
            }
        }
    }

    /// <summary>Seconds typed in the duration editor.</summary>
    public string DurationText
    {
        get => durationText;
        set => Set(ref durationText, value);
    }

    public bool NeedsCore => savedCore is null;

    /// <summary>Session shown in the table and exported: the session of the saved Drillhole ID (or a restored one).</summary>
    public Guid? SessionId => session?.Id;

    /// <summary>Starting depth (metres, the next capture's "from"). A change applies from the next capture.</summary>
    public string NextFrom
    {
        get => nextFrom;
        set
        {
            if (Set(ref nextFrom, value))
            {
                DepthInputsChanged(save: true);
            }
        }
    }

    /// <summary>
    /// Depth step of each capture in CENTIMETRES (Step (cm) input): to_m = from_m + step_cm / 100. Stored in mm as
    /// before. A change applies from the next capture.
    /// </summary>
    public string Step
    {
        get => step;
        set
        {
            if (Set(ref step, value))
            {
                DepthInputsChanged(save: true);
            }
        }
    }

    /// <summary>
    /// CPS alarm threshold: a completed row whose CPS (the table's value) is above it sounds the alarm. Empty means no
    /// alarm. It never blocks Start or changes a measurement; a change applies from the next completed row.
    /// </summary>
    public string Threshold
    {
        get => threshold;
        set
        {
            if (Set(ref threshold, value))
            {
                thresholdCps = CpsAlarm.TryParseThreshold(value, out var parsed) ? parsed : null;
                Changed(nameof(IsThresholdInvalid));
            }
        }
    }

    /// <summary>Red border on Threshold (typed text that is not a valid threshold).</summary>
    public bool IsThresholdInvalid => threshold.Trim().Length > 0 && thresholdCps is null;

    public const string ThresholdError = "Threshold: enter a CPS value of 0 or more (at most 2 decimals), or leave it empty for no alarm.";

    /// <summary>Called when the user leaves the Threshold box: invalid text gets an error toast (no alarm until fixed).</summary>
    public void CommitThreshold()
    {
        if (IsThresholdInvalid)
        {
            Notify(ToastKind.Error, ThresholdError);
        }
    }

    /// <summary>Red border on Starting Depth (typed text that is not a valid depth).</summary>
    public bool IsNextFromInvalid => nextFrom.Length > 0 && nextFromMm is null;

    /// <summary>Red border on Step (typed text that is not a valid step).</summary>
    public bool IsStepInvalid => step.Length > 0 && stepMm is null;

    public string DepthError =>
        nextFrom.Length > 0 && nextFromMm is null ? "Starting depth: enter a depth in metres (at most 3 decimals)." :
        step.Length > 0 && stepMm is null ? "Step: enter a step greater than 0 cm (at most 1 decimal)." :
        nextFromMm is null || stepMm is null ? "Enter the starting depth and step to enable Start." : "";

    private bool HasValidDepths => nextFromMm is not null && stepMm is not null;

    private bool SessionMatchesCore =>
        session is { DrillholeId: { } drillhole } && savedCore is { } core && drillhole == core.DrillholeId;

    /// <summary>
    /// Follows the drillhole selected on Core Information (empty when none is selected). Empty: the table and chart
    /// are emptied and Start, export and delete are unavailable — nothing stored is deleted. A drillhole: its stored
    /// rows (sorted by From), session and depth progress are shown, without creating a session; Save &amp; Continue
    /// still creates it when needed. A load that finishes after a newer selection is discarded.
    /// </summary>
    public void SelectDrillhole(string drillholeId)
    {
        if (string.Equals(session?.DrillholeId ?? "", drillholeId, StringComparison.Ordinal) && !openingSession)
        {
            return;
        }

        var version = ++viewVersion;

        // Rows not stored yet are queued for saving with their own session before the view switches.
        if (hasUnsavedRows)
        {
            SaveSession();
            hasUnsavedRows = false;
        }

        StopMeasurementMode();
        ClearSession();

        if (drillholeId.Length > 0)
        {
            _ = LoadSessionAsync(version, $"Stored measurements of {drillholeId} could not be loaded", async () =>
            {
                if (await repository.FindSessionAsync(drillholeId) is not { } stored)
                {
                    return;
                }

                var rows = await repository.LoadAsync(stored.Id);
                var progress = await repository.LoadProgressAsync(stored.Id);

                if (!disposed && version == viewVersion)
                {
                    ShowSession(stored, rows, progress);
                }
            });
        }
    }

    public void SetCore(CoreSnapshot? core)
    {
        savedCore = core;

        if (core is null)
        {
            StopMeasurementMode();
        }
        else if (!SessionMatchesCore)
        {
            _ = OpenDrillholeAsync(core);
        }

        Changed(nameof(NeedsCore));
        RefreshCommands();
    }

    // Looks up the session of the core's Drillhole ID (creating it only if none exists) and shows its rows and
    // saved progress. The same Drillhole ID always resolves to the same session, also after a restart.
    private Task OpenDrillholeAsync(CoreSnapshot core)
    {
        var version = ++viewVersion;

        return LoadSessionAsync(version, "The drillhole session could not be opened", async () =>
        {
            if (hasUnsavedRows)
            {
                SaveSession();
                Notify(ToastKind.Error, "Measurements of the previous drillhole are not saved yet. Retrying — click Save & Continue again once saving succeeds.");
                return;
            }

            var candidate = new MeasurementSession(
                Guid.NewGuid(), DateTimeOffset.Now, core.DrillholeId, core.DrillholeName, core.ProjectArea, core.Operator, core.Notes);
            var resolved = await repository.ResolveSessionAsync(candidate);
            var rows = await repository.LoadAsync(resolved.Id);
            var progress = await repository.LoadProgressAsync(resolved.Id);

            // A newer selection or open happened meanwhile: this result belongs to an earlier drillhole.
            if (disposed || savedCore != core || version != viewVersion)
            {
                return;
            }

            ShowSession(resolved, rows, progress);

            if (resolved.Id != candidate.Id && resolved.MetadataDiffers(candidate))
            {
                Notify(ToastKind.Warning, $"Drillhole {core.DrillholeId} already exists. Its stored project, campaign, operator and notes are kept.");
            }
        });
    }

    // Loads a drillhole's session while Start is unavailable. Queued saves of the current session finish first, so
    // nothing is reloaded half-saved; a failure is reported as "<failure>: <reason>".
    private async Task LoadSessionAsync(int version, string failure, Func<Task> load)
    {
        openingSession = true;
        depthsTypedWhileOpening = false;
        RefreshCommands();

        try
        {
            await persistence;
            await load();
        }
        catch (Exception error)
        {
            Notify(ToastKind.Error, $"{failure}: {error.Message}");
        }
        finally
        {
            // A newer selection that is still loading keeps its own loading state.
            if (version == viewVersion)
            {
                openingSession = false;
                RefreshCommands();

                // Values typed while the session was loading were kept on screen; store them now as its progress.
                if (depthsTypedWhileOpening)
                {
                    depthsTypedWhileOpening = false;
                    DepthInputsChanged(save: true);
                }
            }
        }
    }

    // Empties the table and the depth inputs; nothing stored is deleted.
    private void ClearSession()
    {
        session = null;
        nextCapture = 1;
        Results.Clear();
        Changed(nameof(SessionId));
        SetDepthInputs("", "");
    }

    // Shows a stored session: its rows sorted by From and its saved starting depth and step (the step converted from
    // stored mm to cm). Without saved progress the starting depth is empty (to be entered on the Measurement page) and
    // the step stays as entered.
    private void ShowSession(MeasurementSession shown, IReadOnlyList<MeasurementResult> rows, DepthProgress? progress)
    {
        session = shown;
        nextCapture = rows.Count == 0 ? 1 : rows.Max(r => r.CaptureNumber) + 1;
        Results.Clear();

        foreach (var row in MeasurementOrder.Sort(rows))
        {
            Results.Add(row);
        }

        Renumber();
        Changed(nameof(SessionId));

        // What the user typed while the session was loading wins over stored values: never overwrite an active edit.
        if (depthsTypedWhileOpening)
        {
            return;
        }

        if (progress is not null)
        {
            SetDepthInputs(Depth.Format(progress.NextFromMm), Depth.FormatCentimetres(progress.StepMm));
        }
        else
        {
            SetDepthInputs("", step);
        }
    }

    // Sets both inputs without saving (used when values come from storage or a completed capture).
    private void SetDepthInputs(string from, string size)
    {
        nextFrom = from;
        step = size;
        Changed(nameof(NextFrom));
        Changed(nameof(Step));
        DepthInputsChanged(save: false);
    }

    private void DepthInputsChanged(bool save)
    {
        nextFromMm = Depth.TryParseMetres(nextFrom, out var from) ? from : null;
        stepMm = Depth.TryParseCentimetres(step, out var size) && size > 0 ? size : null;
        Changed(nameof(DepthError));
        Changed(nameof(IsNextFromInvalid));
        Changed(nameof(IsStepInvalid));
        RefreshCommands();

        if (save && openingSession)
        {
            depthsTypedWhileOpening = true;
        }

        // A user change is stored as the progress of the drillhole's session (rows are re-saved unchanged).
        if (save && HasValidDepths && SessionMatchesCore && !openingSession)
        {
            SaveSession();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        StopMeasurementMode();

        if (hasUnsavedRows)
        {
            SaveSession();
        }

        // Let queued saves finish on close. They run on the thread pool, so waiting here cannot deadlock.
        persistence.Wait(TimeSpan.FromSeconds(5));
        lifetime.Cancel();
        lifetime.Dispose();
        ExportCommand.Refresh();
        DeleteCommand.Refresh();
    }

    /// <summary>
    /// Called when the user leaves a depth box (commits it): invalid text gets an error toast. Typing itself only
    /// colours the box border.
    /// </summary>
    public void CommitDepthInputs()
    {
        if (IsNextFromInvalid || IsStepInvalid)
        {
            Notify(ToastKind.Error, DepthError);
        }
    }

    private void RefreshCommands()
    {
        EditDurationCommand.Refresh();
        SaveDurationCommand.Refresh();
        CancelDurationCommand.Refresh();
        ToggleMeasurementCommand.Refresh();
        TriggerCommand.Refresh();
        NewMeasurementCommand.Refresh();
    }

    private void ToggleMeasurementMode()
    {
        if (IsMeasurementModeActive)
        {
            // Stop ends acquisition only: rows, session and queued saves are kept. Retry a failed save now.
            StopMeasurementMode();

            if (hasUnsavedRows)
            {
                SaveSession();
            }

            return;
        }

        // Start needs a valid starting depth and step; otherwise say what is missing (5 s error toast).
        if (!HasValidDepths)
        {
            Notify(ToastKind.Error, DepthError);
            return;
        }

        // Start (also after Stop) continues the drillhole's session, its rows and its next depth and step.
        ResetCountdown();
        State = MeasurementState.Ready;
    }

    // Sessions follow the Drillhole ID, so a new measurement means entering the next drillhole: this closes the
    // current one (it stays stored with its progress) and asks for Core Information to be saved again.
    private void NewMeasurement()
    {
        // Closing clears the table, so unsaved rows must reach the database first.
        if (hasUnsavedRows)
        {
            SaveSession();
            Notify(ToastKind.Error, "Previous measurements are not saved yet. Retrying — try New Measurement again once saving succeeds.");
            return;
        }

        ClearSession();
        SetCore(null);
        Notify(ToastKind.Information, "Enter the next drillhole in Core Information and click Save & Continue. Earlier measurements remain stored.");
    }

    private void StopMeasurementMode()
    {
        // Cancel first so the State change below refreshes the commands with the final picture.
        activeCapture?.Cancel();
        State = MeasurementState.Idle;
        ResetCountdown();
        alarm.Reset();
    }

    // Back to the full configured duration (after completion, cancellation, Stop or a duration change).
    private void ResetCountdown()
    {
        Countdown = FormatCountdown(captureDuration);
        CaptureProgress = 0;
    }

    // mm:ss, rounded up to whole seconds and never above the given capture's duration.
    private static string FormatCountdown(TimeSpan remaining, TimeSpan? duration = null)
    {
        var limit = (int)Math.Ceiling((duration ?? remaining).TotalSeconds);
        var seconds = Math.Clamp((int)Math.Ceiling(remaining.TotalSeconds), 0, Math.Max(limit, 0));
        return $"{seconds / 60:00}:{seconds % 60:00}";
    }

    private void BeginEditDuration()
    {
        if (IsCapturing)
        {
            return;
        }

        DurationText = ((int)captureDuration.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
        IsEditingDuration = true;
    }

    // Saves a valid duration for the next capture (and stores it); invalid input keeps the editor open with an error
    // toast. Never starts measuring or triggers a capture.
    private void SaveDuration()
    {
        if (!CaptureDurationSettings.TryParse(DurationText, out var seconds))
        {
            Notify(ToastKind.Error,
                $"Capture duration: enter whole seconds from {CaptureDurationSettings.MinSeconds} to {CaptureDurationSettings.MaxSeconds}.");
            return;
        }

        if (IsCapturing)
        {
            Notify(ToastKind.Error, "The capture duration can be changed when no capture is running.");
            return;
        }

        captureDuration = TimeSpan.FromSeconds(seconds);
        Changed(nameof(CaptureDuration));
        ResetCountdown();
        IsEditingDuration = false;

        try
        {
            durationSettings.Save(seconds);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Notify(ToastKind.Error, $"The capture duration is used now but could not be saved ({error.Message}).");
        }
    }

    private async Task CaptureAsync(DateTimeOffset start)
    {
        if (savedCore is not { } core || session is not { } current || nextFromMm is not { } fromMm || stepMm is not { } stepValue)
        {
            return;
        }

        // The interval and the duration are fixed now: later step, depth or duration edits apply to the next capture.
        var toMm = fromMm + stepValue;
        var duration = captureDuration;

        using var capture = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token = capture.Token;
        activeCapture = capture;
        State = MeasurementState.Capturing;

        try
        {
            var data = await measurement.CaptureAsync(
                start,
                duration,
                remaining =>
                {
                    // A cancelled operation may report late; it must never change a newer UI state.
                    if (token.IsCancellationRequested || disposed || activeCapture != capture)
                    {
                        return;
                    }

                    Countdown = FormatCountdown(remaining, duration);
                    CaptureProgress = Math.Clamp(1 - remaining.TotalSeconds / duration.TotalSeconds, 0, 1);
                },
                token);

            token.ThrowIfCancellationRequested();
            State = MeasurementState.Processing;
            Countdown = FormatCountdown(TimeSpan.Zero);
            CaptureProgress = 1;

            // Display hold, kept on purpose: it gives WPF one render of "00:00" and the full ring before the
            // row is committed. It does not extend the capture interval, and Stop can still cancel during it.
            await Task.Delay(100, token);
            token.ThrowIfCancellationRequested();
            var row = BuildResult(data, current, core, fromMm, toMm, stepValue, duration);

            // The row goes to its place in the From order (not necessarily the end).
            AddSorted(row);

            // Once per completed row, on the CPS the table shows; the sound plays without waiting.
            alarm.Check(row.Reading.Count0, thresholdCps);

            if (row.IsIncomplete)
            {
                Notify(ToastKind.Warning,
                    $"Incomplete capture: {row.SampleCount} of {row.ExpectedSamples} samples received. The result averages the {row.SampleCount} received.");
            }

            // Advance once per completed capture, from this capture's own interval (never from the last displayed
            // row: the table is sorted by From) — unless the user typed a new starting depth meanwhile, which then
            // applies to the next capture. Saved in the same transaction as the new row.
            if (nextFromMm == fromMm)
            {
                SetDepthInputs(Depth.Format(toMm), step);
            }

            SaveSession();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (CaptureException error) when (!token.IsCancellationRequested)
        {
            // No (usable) samples, no device, database unavailable …: no row, no depth advance.
            Notify(ToastKind.Error, error.Message);
        }
        catch (Exception)
        {
            // Disposal cancels the token, so a real failure always has an uncancelled token here.
            if (!token.IsCancellationRequested)
            {
                Notify(ToastKind.Error, "Capture failed. Right-click to try again.");
            }
        }
        finally
        {
            if (activeCapture == capture)
            {
                activeCapture = null;
                ResetCountdown();

                if (!disposed && State is MeasurementState.Capturing or MeasurementState.Processing)
                {
                    State = MeasurementState.Ready;
                }

                RefreshCommands();
            }
        }
    }

    // The completed row of a capture. Device captures: the averages of the accepted samples (each field over its own
    // valid values), the actual sample count, the window and the source detector; the raw samples ride along and are
    // saved with the row. Other services (tests) deliver plain values. No samples at all is an error — nothing is invented.
    private MeasurementResult BuildResult(
        CaptureData data, MeasurementSession current, CoreSnapshot core, long fromMm, long toMm, long stepMm, TimeSpan duration)
    {
        var deviceSamples = data.DeviceSamples;
        var sampleCount = deviceSamples?.Count ?? data.Samples.Count;

        if (sampleCount == 0)
        {
            throw new CaptureException(deviceSamples is null
                ? "The capture returned no samples. Nothing was saved."
                : $"No samples were received from {data.Device?.Name ?? data.Device?.Serial ?? "the device"} during the {duration.TotalSeconds:0} s capture. Nothing was saved.");
        }

        var summary = deviceSamples is null ? null : SampleAveraging.Summarize(deviceSamples);
        var device = deviceSamples is null ? null : data.Device;

        return new MeasurementResult
        {
            SessionId = current.Id,
            CaptureNumber = nextCapture++,
            StartTime = data.StartTime,
            EndTime = data.EndTime,
            CompletedAt = data.CompletedAt,
            SampleCount = sampleCount,
            ExpectedSamples = data.ExpectedSamples,
            DurationMs = (long)(data.EndTime - data.StartTime).TotalMilliseconds,
            Reading = summary is null
                ? data.Reading ?? DeviceReading.Empty
                : new DeviceReading(summary.FirstTsMs, summary.Count0, summary.Count0Raw, summary.Hdm, summary.HdmRaw, summary.DoseRate),
            Temperature = summary?.Temperature,
            Pressure = summary?.Pressure,
            DeviceSerial = device?.Serial,
            DeviceName = device?.Name,
            DeviceMac = device?.Mac,
            DeviceSessionId = summary?.DeviceSessionId,
            Samples = deviceSamples,
            AverageValue = deviceSamples is null ? data.Samples.Average() : null,
            Unit = deviceSamples is null ? data.Unit : null,
            Core = core,
            FromMm = fromMm,
            ToMm = toMm,
            StepMm = stepMm
        };
    }

    // Each row gets its demo value once, when it enters the table, so redraws and re-sorting keep the same value.
    private void AssignDemoTsMs(System.Collections.IList? rows)
    {
        if (!ShowDemoTsMs || rows is null)
        {
            return;
        }

        foreach (MeasurementResult row in rows)
        {
            if (row.Reading.TsMs is null)
            {
                row.DemoTsMs ??= Random.Shared.Next(500, 5001);
            }
        }
    }

    private void Delete(object? parameter)
    {
        if (parameter is not MeasurementResult row || !dialogs.ConfirmDelete())
        {
            return;
        }

        _ = DeleteAsync(row);
    }

    // Removes the confirmed row. If the database delete fails the row is shown again in its place, because it is
    // still stored.
    private async Task DeleteAsync(MeasurementResult row)
    {
        Results.Remove(row);
        Renumber();

        if (!await Persist(
                () => repository.DeleteAsync(row.SessionId, row.CaptureNumber),
                error => $"Deleting the measurement failed ({error.Message}). It is still stored and stays in the table."))
        {
            AddSorted(row);
            return;
        }

        SaveSession();
        // Reported only now: the row is gone from the database.
        Notify(ToastKind.Success, "Measurement deleted.");
    }

    // Inserts a row at its position in the From order and renumbers No. to match the displayed positions.
    private void AddSorted(MeasurementResult row)
    {
        Results.Insert(MeasurementOrder.InsertIndex(Results, row), row);
        Renumber();
    }

    private void Renumber()
    {
        for (var i = 0; i < Results.Count; i++)
        {
            Results[i].Number = i + 1;
        }
    }

    // Stores the whole table (one session, displayed order) without waiting. Stored rows only get their SortOrder
    // updated, so a row whose earlier save failed is saved by the next change, Stop or close — never twice.
    private void SaveSession() => _ = SaveSessionAsync();

    private async Task SaveSessionAsync()
    {
        if (session is not { } current)
        {
            return;
        }

        var rows = Results.ToArray();
        var progress = nextFromMm is { } from && stepMm is { } size ? new DepthProgress(from, size) : null;
        var saved = await Persist(() => repository.SaveAsync(current, rows, progress));

        // Saves complete in request order, so the latest result describes the table.
        if (session == current)
        {
            hasUnsavedRows = !saved;
        }

        if (saved && rows.Length > 0)
        {
            MeasurementsSaved?.Invoke();
        }
    }

    // Queues a write without blocking the UI. A failure is reported (by default: the rows stay in the table).
    private async Task<bool> Persist(Func<Task> write, Func<Exception, string>? failureMessage = null)
    {
        var previous = persistence;
        var current = Task.Run(async () =>
        {
            await previous;
            await write();
        });
        persistence = current.ContinueWith(_ => { }, TaskScheduler.Default);

        try
        {
            await current;
            return true;
        }
        catch (Exception error)
        {
            Notify(ToastKind.Error, failureMessage?.Invoke(error) ??
                $"Saving to the database failed ({error.Message}). Rows stay in the table and are saved again on the next change or Stop.");
            return false;
        }
    }

    private async Task ExportAsync()
    {
        if (session is { } current && await export.ExportAsync(Results.ToArray(), current, lifetime.Token))
        {
            Notify(ToastKind.Success, "CSV exported.");
        }
    }

    private void Notify(ToastKind kind, string message)
    {
        if (!disposed)
        {
            Toasts.Show(kind, message);
        }
    }
}
