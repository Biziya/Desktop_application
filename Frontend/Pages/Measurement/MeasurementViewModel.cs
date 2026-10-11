using System.Collections.ObjectModel;
using Frontend.Shared.Infrastructure;
using CoreInformationData = Frontend.Shared.Models.CoreInformation;
using Frontend.Shared.Services;
namespace Frontend.Pages.Measurement;
public sealed class MeasurementViewModel : ObservableObject, IDisposable
{
    private readonly IMeasurementService measurement;
    private readonly ICsvExportService export;
    private readonly IDialogService dialogs;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? notificationLifetime;
    private CancellationTokenSource? activeCapture;
    private CoreInformationData? savedCore;
    private bool disposed;
    private MeasurementState state = MeasurementState.Idle;
    private string countdown = "00:05", notification = "";
    private double captureProgress;
    private int nextCapture = 1;

    public MeasurementViewModel(IMeasurementService measurement, ICsvExportService export, IDialogService dialogs)
    {
        this.measurement = measurement; this.export = export; this.dialogs = dialogs;
        ToggleMeasurementCommand = new RelayCommand(_ => ToggleMeasurementMode(),
            _ => !disposed && (IsMeasurementModeActive || savedCore is not null));
        TriggerCommand = new RelayCommand(parameter =>
        {
            // CaptureAsync contains all cancellation/error handling. Execution starts synchronously
            // so the state gate closes before another routed mouse event can be accepted.
            _ = CaptureAsync(parameter is DateTimeOffset timestamp ? timestamp : DateTimeOffset.Now);
        }, _ => CanTrigger);
        ExportCommand = new AsyncCommand(ExportAsync, _ => Notify("Export failed. Check the file location and try again."),
            () => !disposed && Results.Count > 0);
        DeleteCommand = new RelayCommand(Delete, p => !disposed && p is MeasurementResult);
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
            if (!Set(ref state,value)) return;
            Changed(nameof(IsMeasurementModeActive)); Changed(nameof(IsCapturing));
            Changed(nameof(MeasurementButtonText));
            RefreshCommands();
        }
    }
    public bool IsMeasurementModeActive => State != MeasurementState.Idle;
    public bool IsCapturing => State is MeasurementState.Capturing or MeasurementState.Processing;
    public bool CanTrigger => !disposed && State == MeasurementState.Ready && savedCore is not null && activeCapture is null;
    public string MeasurementButtonText => IsMeasurementModeActive ? "■  Stop" : "▶  Start";
    public string Countdown { get => countdown; private set => Set(ref countdown,value); }
    public double CaptureProgress { get => captureProgress; private set => Set(ref captureProgress,value); }
    public string Notification { get => notification; private set => Set(ref notification,value); }
    public bool NeedsCore => savedCore is null;

    public void SetCore(CoreInformationData? core)
    {
        savedCore = core;
        if (core is null) StopMeasurementMode();
        Changed(nameof(NeedsCore)); RefreshCommands();
    }
    private void RefreshCommands()
    {
        Changed(nameof(CanTrigger)); ToggleMeasurementCommand.Refresh(); TriggerCommand.Refresh();
    }
    private void ToggleMeasurementMode()
    {
        if (IsMeasurementModeActive) { StopMeasurementMode(); return; }
        if (disposed || savedCore is null) return;
        ResetCountdown(); State = MeasurementState.Ready;
    }
    private void StopMeasurementMode()
    {
        State = MeasurementState.Idle;
        activeCapture?.Cancel();
        ResetCountdown(); RefreshCommands();
    }
    private void ResetCountdown() { Countdown = "00:05"; CaptureProgress = 0; }

    private async Task CaptureAsync(DateTimeOffset start)
    {
        if (!CanTrigger || savedCore is not { } core) return;
        using var capture = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token = capture.Token;
        activeCapture = capture;
        State = MeasurementState.Capturing;
        try
        {
            var data = await measurement.CaptureAsync(start, remaining =>
            {
                // A cancelled operation may report late; it must never change a newer UI state.
                if (token.IsCancellationRequested || disposed || activeCapture != capture) return;
                Countdown = $"00:{Math.Clamp((int)Math.Ceiling(remaining.TotalSeconds),0,5):00}";
                CaptureProgress = Math.Clamp(1 - remaining.TotalSeconds / MockMeasurementService.Duration.TotalSeconds, 0, 1);
            }, token);
            token.ThrowIfCancellationRequested();
            State = MeasurementState.Processing;
            Countdown = "00:00"; CaptureProgress = 1;
            if (data.Samples.Count == 0) throw new InvalidOperationException("Capture contains no samples.");
            var average = data.Samples.Average();
            // Let WPF render zero. This display hold does not extend the capture interval.
            // Stop remains available and can cancel until the completed row is committed.
            await Task.Delay(100, token);
            token.ThrowIfCancellationRequested();
            Results.Add(new MeasurementResult { CaptureNumber = nextCapture++, StartTime = data.StartTime,
                EndTime = data.EndTime, CompletedAt = data.CompletedAt, SampleCount = data.Samples.Count,
                AverageValue = average, Unit = data.Unit, Core = core });
            Renumber();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!token.IsCancellationRequested && !disposed) Notify("Capture failed. Right-click to try again.");
        }
        finally
        {
            if (activeCapture == capture)
            {
                activeCapture = null;
                ResetCountdown();
                if (!disposed && State is MeasurementState.Capturing or MeasurementState.Processing)
                    State = MeasurementState.Ready;
                RefreshCommands();
            }
        }
    }
    private void Delete(object? parameter)
    {
        if (parameter is not MeasurementResult row || !Results.Contains(row) || !dialogs.ConfirmDelete()) return;
        Results.Remove(row); Renumber(); Notify("Measurement deleted.");
    }
    public void Move(MeasurementResult source, MeasurementResult target, bool after)
    {
        var sourceIndex = Results.IndexOf(source);
        var targetIndex = Results.IndexOf(target);
        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex) return;
        var destination = targetIndex + (after ? 1 : 0);
        if (sourceIndex < destination) destination--;
        Results.Move(sourceIndex, destination); Renumber();
    }
    private void Renumber()
    {
        for (var i=0; i<Results.Count; i++) Results[i].Number = i+1;
        ExportCommand.Refresh();
    }
    private async Task ExportAsync()
    {
        if (await export.ExportAsync(Results.ToArray(), lifetime.Token)) Notify("CSV exported.");
    }
    private async void Notify(string message)
    {
        if (disposed) return;
        notificationLifetime?.Cancel();
        notificationLifetime?.Dispose();
        notificationLifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token = notificationLifetime.Token;
        Notification = message;
        try { await Task.Delay(4500, token); if (!disposed) Notification = ""; }
        catch (OperationCanceledException) { }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        StopMeasurementMode();
        lifetime.Cancel(); notificationLifetime?.Cancel(); notificationLifetime?.Dispose(); lifetime.Dispose();
        ExportCommand.Refresh(); DeleteCommand.Refresh();
    }
}
