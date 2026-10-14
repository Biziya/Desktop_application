using System.Collections.ObjectModel;
using Frontend.Shared.Infrastructure;
using Frontend.Shared.Models;

namespace Frontend.Pages.Measurement;

public sealed class MeasurementViewModel : ObservableObject, IDisposable
{
    private readonly IMeasurementService measurement;
    private readonly ICsvExportService export;
    private readonly IDialogService dialogs;
    private readonly IMeasurementRepository repository;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? notificationLifetime;
    private CancellationTokenSource? activeCapture;
    private CoreSnapshot? savedCore;
    private bool disposed;
    private MeasurementState state = MeasurementState.Idle;
    private string countdown;
    private string notification = "";
    private double captureProgress;
    private int nextCapture = 1;
    private MeasurementSession? session;
    private bool hasUnsavedRows;
    private bool openingSession;
    private string nextFrom = "";
    private string step = "";
    private long? nextFromMm;
    private long? stepMm;

    // Database writes run one after another, in the order they were requested. Never faults.
    private Task persistence = Task.CompletedTask;

    public MeasurementViewModel(
        IMeasurementService measurement,
        ICsvExportService export,
        IDialogService dialogs,
        IMeasurementRepository repository)
    {
        this.measurement = measurement;
        this.export = export;
        this.dialogs = dialogs;
        this.repository = repository;
        countdown = FormatCountdown(measurement.Duration);

        ToggleMeasurementCommand = new RelayCommand(
            _ => ToggleMeasurementMode(),
            _ => IsMeasurementModeActive || (SessionMatchesCore && HasValidDepths && !openingSession));

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
            _ => Notify("Export failed. Check the file location and try again."),
            () => Results.Count > 0);

        DeleteCommand = new RelayCommand(Delete, parameter => parameter is MeasurementResult row && Results.Contains(row));

        NewMeasurementCommand = new RelayCommand(_ => NewMeasurement(), _ => !IsMeasurementModeActive);

        Results.CollectionChanged += (_, _) => ExportCommand.Refresh();
    }

    public ObservableCollection<MeasurementResult> Results { get; } = [];

    public RelayCommand ToggleMeasurementCommand { get; }

    public RelayCommand TriggerCommand { get; }

    public AsyncCommand ExportCommand { get; }

    public RelayCommand DeleteCommand { get; }

    /// <summary>Closes the current drillhole: the next Save &amp; Continue opens the session of the drillhole entered then.</summary>
    public RelayCommand NewMeasurementCommand { get; }

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
        State == MeasurementState.Ready && SessionMatchesCore && HasValidDepths && activeCapture is null;

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

    public string Notification
    {
        get => notification;
        private set => Set(ref notification, value);
    }

    public bool NeedsCore => savedCore is null;

    /// <summary>Session shown in the table and exported: the session of the saved Drillhole ID (or a restored one).</summary>
    public Guid? SessionId => session?.Id;

    /// <summary>Starting depth (metres) of the next capture. A change applies from the next capture.</summary>
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

    /// <summary>Depth step (metres) of each capture. A change applies from the next capture.</summary>
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

    public string DepthError =>
        nextFrom.Length > 0 && nextFromMm is null ? "Next from: enter a depth in metres (at most 3 decimals)." :
        step.Length > 0 && stepMm is null ? "Step: enter a depth greater than 0 m (at most 3 decimals)." :
        nextFromMm is null || stepMm is null ? "Enter the starting depth and step to enable Start." : "";

    /// <summary>Interval the next capture will record, e.g. "10.50 – 11.00 m".</summary>
    public string NextInterval =>
        nextFromMm is { } from && stepMm is { } size ? $"{Depth.Format(from)} – {Depth.Format(from + size)} m" : "";

    private bool HasValidDepths => nextFromMm is not null && stepMm is not null;

    private bool SessionMatchesCore =>
        session is { DrillholeId: { } drillhole } && savedCore is { } core && drillhole == core.DrillholeId;

    /// <summary>
    /// Shows the most recently stored session after a restart, in its saved order. Start then continues it.
    /// It does not create a session, and it is skipped if a session is already active.
    /// </summary>
    public async Task RestoreLatestSessionAsync()
    {
        try
        {
            if (await repository.LoadLatestSessionAsync() is not { } latest)
            {
                return;
            }

            var rows = await repository.LoadAsync(latest.Id);
            var progress = await repository.LoadProgressAsync(latest.Id);

            if (disposed || session is not null || openingSession)
            {
                return;
            }

            ShowSession(latest, rows, progress);
        }
        catch (Exception error)
        {
            Notify($"Stored measurements could not be loaded: {error.Message}");
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
    private async Task OpenDrillholeAsync(CoreSnapshot core)
    {
        openingSession = true;
        RefreshCommands();

        try
        {
            // Queued saves of the current session finish first, so nothing is reloaded half-saved.
            await persistence;

            if (hasUnsavedRows)
            {
                SaveSession();
                Notify("Measurements of the previous drillhole are not saved yet. Retrying — click Save & Continue again once saving succeeds.");
                return;
            }

            var candidate = new MeasurementSession(
                Guid.NewGuid(), DateTimeOffset.Now, core.DrillholeId, core.DrillholeName, core.ProjectArea, core.Operator, core.Notes);
            var resolved = await repository.ResolveSessionAsync(candidate);
            var rows = await repository.LoadAsync(resolved.Id);
            var progress = await repository.LoadProgressAsync(resolved.Id);

            if (disposed || savedCore != core)
            {
                return;
            }

            ShowSession(resolved, rows, progress);

            if (resolved.Id != candidate.Id && resolved.MetadataDiffers(candidate))
            {
                Notify($"Drillhole {core.DrillholeId} already exists. Its stored name, project, operator and comment are kept; the edited values were not saved.");
            }
        }
        catch (Exception error)
        {
            Notify($"The drillhole session could not be opened: {error.Message}");
        }
        finally
        {
            openingSession = false;
            RefreshCommands();
        }
    }

    // Shows a stored session: its rows in saved order and its saved next depth and step. Without saved progress the
    // starting depth defaults to the core's Depth From and the step stays as entered.
    private void ShowSession(MeasurementSession shown, IReadOnlyList<MeasurementResult> rows, DepthProgress? progress)
    {
        session = shown;
        nextCapture = rows.Count == 0 ? 1 : rows.Max(r => r.CaptureNumber) + 1;
        Results.Clear();

        foreach (var row in rows)
        {
            Results.Add(row);
        }

        Renumber();
        Changed(nameof(SessionId));

        if (progress is not null)
        {
            SetDepthInputs(Depth.Format(progress.NextFromMm), Depth.Format(progress.StepMm));
        }
        else if (savedCore is { } core)
        {
            SetDepthInputs(core.DepthFrom.ToString(System.Globalization.CultureInfo.CurrentCulture), step);
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
        stepMm = Depth.TryParseMetres(step, out var size) && size > 0 ? size : null;
        Changed(nameof(DepthError));
        Changed(nameof(NextInterval));
        RefreshCommands();

        // A user change is stored as the progress of the drillhole's session (rows are re-saved unchanged).
        if (save && HasValidDepths && SessionMatchesCore && !openingSession)
        {
            SaveSession();
        }
    }

    public void Move(MeasurementResult source, MeasurementResult target, bool after)
    {
        var sourceIndex = Results.IndexOf(source);
        var targetIndex = Results.IndexOf(target);

        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex)
        {
            return;
        }

        var destination = targetIndex + (after ? 1 : 0);

        if (sourceIndex < destination)
        {
            destination--;
        }

        Results.Move(sourceIndex, destination);
        Renumber();
        SaveSession();
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
        notificationLifetime?.Cancel();
        notificationLifetime?.Dispose();
        lifetime.Dispose();
        ExportCommand.Refresh();
        DeleteCommand.Refresh();
    }

    private void RefreshCommands()
    {
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
            Notify("Previous measurements are not saved yet. Retrying — try New Measurement again once saving succeeds.");
            return;
        }

        session = null;
        nextCapture = 1;
        Results.Clear();
        Changed(nameof(SessionId));
        SetDepthInputs("", "");
        SetCore(null);
        Notify("Enter the next drillhole in Core Information and click Save & Continue. Earlier measurements remain stored.");
    }

    private void StopMeasurementMode()
    {
        // Cancel first so the State change below refreshes the commands with the final picture.
        activeCapture?.Cancel();
        State = MeasurementState.Idle;
        ResetCountdown();
    }

    private void ResetCountdown()
    {
        Countdown = FormatCountdown(measurement.Duration);
        CaptureProgress = 0;
    }

    private string FormatCountdown(TimeSpan remaining)
    {
        var seconds = Math.Clamp((int)Math.Ceiling(remaining.TotalSeconds), 0, (int)measurement.Duration.TotalSeconds);
        return $"00:{seconds:00}";
    }

    private async Task CaptureAsync(DateTimeOffset start)
    {
        if (savedCore is not { } core || session is not { } current || nextFromMm is not { } fromMm || stepMm is not { } stepValue)
        {
            return;
        }

        // The interval is fixed now: later step or depth edits apply to the next capture only.
        var toMm = fromMm + stepValue;

        using var capture = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token = capture.Token;
        activeCapture = capture;
        State = MeasurementState.Capturing;

        try
        {
            var data = await measurement.CaptureAsync(
                start,
                remaining =>
                {
                    // A cancelled operation may report late; it must never change a newer UI state.
                    if (token.IsCancellationRequested || disposed || activeCapture != capture)
                    {
                        return;
                    }

                    Countdown = FormatCountdown(remaining);
                    CaptureProgress = Math.Clamp(1 - remaining.TotalSeconds / measurement.Duration.TotalSeconds, 0, 1);
                },
                token);

            token.ThrowIfCancellationRequested();
            State = MeasurementState.Processing;
            Countdown = FormatCountdown(TimeSpan.Zero);
            CaptureProgress = 1;

            if (data.Samples.Count == 0)
            {
                throw new InvalidOperationException("Capture contains no samples.");
            }

            var average = data.Samples.Average();

            // Display hold, kept on purpose: it gives WPF one render of "00:00" and the full ring before the
            // row is committed. It does not extend the capture interval, and Stop can still cancel during it.
            await Task.Delay(100, token);
            token.ThrowIfCancellationRequested();

            Results.Add(new MeasurementResult
            {
                SessionId = current.Id,
                CaptureNumber = nextCapture++,
                StartTime = data.StartTime,
                EndTime = data.EndTime,
                CompletedAt = data.CompletedAt,
                SampleCount = data.Samples.Count,
                Reading = data.Reading ?? DeviceReading.Empty,
                AverageValue = average,
                Unit = data.Unit,
                Core = core,
                FromMm = fromMm,
                ToMm = toMm,
                StepMm = stepValue
            });
            Renumber();

            // Advance once per completed capture — unless the user typed a new starting depth meanwhile,
            // which then applies to the next capture. Saved in the same transaction as the new row.
            if (nextFromMm == fromMm)
            {
                SetDepthInputs(Depth.Format(toMm), step);
            }

            SaveSession();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            // Disposal cancels the token, so a real failure always has an uncancelled token here.
            if (!token.IsCancellationRequested)
            {
                Notify("Capture failed. Right-click to try again.");
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

    private void Delete(object? parameter)
    {
        if (parameter is not MeasurementResult row || !dialogs.ConfirmDelete())
        {
            return;
        }

        Results.Remove(row);
        Renumber();
        _ = Persist(() => repository.DeleteAsync(row.SessionId, row.CaptureNumber));
        SaveSession();
        Notify("Measurement deleted.");
    }

    private void Renumber()
    {
        for (var i = 0; i < Results.Count; i++)
        {
            Results[i].Number = i + 1;
        }
    }

    // Stores the whole table (one session, current order). Stored rows only get their SortOrder updated, so a row
    // whose earlier save failed is saved by the next change, Stop or close — never twice.
    private async void SaveSession()
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
    }

    // Queues a write without blocking the UI. A failure is reported; the rows stay in the table.
    private async Task<bool> Persist(Func<Task> write)
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
            Notify($"Saving to the database failed ({error.Message}). Rows stay in the table and are saved again on the next change or Stop.");
            return false;
        }
    }

    private async Task ExportAsync()
    {
        if (await export.ExportAsync(Results.ToArray(), lifetime.Token))
        {
            Notify("CSV exported.");
        }
    }

    private async void Notify(string message)
    {
        if (disposed)
        {
            return;
        }

        notificationLifetime?.Cancel();
        notificationLifetime?.Dispose();
        notificationLifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token = notificationLifetime.Token;
        Notification = message;

        try
        {
            await Task.Delay(4500, token);

            if (!disposed)
            {
                Notification = "";
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
