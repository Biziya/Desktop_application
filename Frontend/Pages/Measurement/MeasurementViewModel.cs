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
            _ => IsMeasurementModeActive || savedCore is not null);

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

        Results.CollectionChanged += (_, _) => ExportCommand.Refresh();
    }

    public ObservableCollection<MeasurementResult> Results { get; } = [];

    public RelayCommand ToggleMeasurementCommand { get; }

    public RelayCommand TriggerCommand { get; }

    public AsyncCommand ExportCommand { get; }

    public RelayCommand DeleteCommand { get; }

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

    public bool CanTrigger => State == MeasurementState.Ready && savedCore is not null && activeCapture is null;

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

    /// <summary>Session shown in the table and exported. Hidden from the table; null until Start or a restore.</summary>
    public Guid? MeasurementSessionId => session?.Id;

    /// <summary>
    /// Shows the most recently stored session after a restart. It does not create a session, and it is skipped
    /// if Start has already begun a new one.
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

            if (disposed || session is not null)
            {
                return;
            }

            session = latest;
            Changed(nameof(MeasurementSessionId));

            foreach (var row in rows)
            {
                Results.Add(row);
            }

            Renumber();
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

        Changed(nameof(NeedsCore));
        RefreshCommands();
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
    }

    private void ToggleMeasurementMode()
    {
        if (IsMeasurementModeActive)
        {
            StopMeasurementMode();
            return;
        }

        BeginSession();
        ResetCountdown();
        State = MeasurementState.Ready;
    }

    // Start is the action that begins a new measurement, so it alone creates a session. Navigation, Stop and
    // restoring stored rows never do. The previous session stays in the database; the table shows only this one.
    private void BeginSession()
    {
        session = MeasurementSession.New();
        nextCapture = 1;
        Results.Clear();
        Changed(nameof(MeasurementSessionId));
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
        if (savedCore is not { } core || session is not { } current)
        {
            return;
        }

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
                MeasurementSessionId = current.Id,
                CaptureNumber = nextCapture++,
                StartTime = data.StartTime,
                EndTime = data.EndTime,
                CompletedAt = data.CompletedAt,
                SampleCount = data.Samples.Count,
                Reading = data.Reading ?? DeviceReading.Empty,
                AverageValue = average,
                Unit = data.Unit,
                Core = core
            });
            Renumber();
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
        Persist(() => repository.DeleteAsync(row.MeasurementSessionId, row.CaptureNumber));
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

    // Stores the whole table (one session, display order). Rows already stored only get their No updated,
    // so a row whose earlier save failed is saved again by the next change.
    private void SaveSession()
    {
        if (session is { } current)
        {
            var rows = Results.ToArray();
            Persist(() => repository.SaveAsync(current, rows));
        }
    }

    // Queues a write without blocking the UI. A failure is reported; the rows stay in the table.
    private async void Persist(Func<Task> write)
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
        }
        catch (Exception error)
        {
            Notify($"Saving to the database failed ({error.Message}). Rows stay in the table and are saved again with the next change.");
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
