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
    private CoreInformationData? savedCore;
    private bool capturing, disposed;
    private string countdown = "00:05", notification = "";
    private int nextCapture = 1;
    public MeasurementViewModel(IMeasurementService measurement, ICsvExportService export, IDialogService dialogs)
    {
        this.measurement = measurement; this.export = export; this.dialogs = dialogs;
        StartCommand = new AsyncCommand(CaptureAsync, _ => Notify("Capture failed. Please try again."),
            () => !disposed && !IsCapturing && savedCore is not null);
        ExportCommand = new AsyncCommand(ExportAsync, _ => Notify("Export failed. Check the file location and try again."),
            () => !disposed && Results.Count > 0);
        DeleteCommand = new RelayCommand(Delete, p => !disposed && p is MeasurementResult);
    }
    public ObservableCollection<MeasurementResult> Results { get; } = [];
    public AsyncCommand StartCommand { get; }
    public AsyncCommand ExportCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public bool IsCapturing { get => capturing; private set { Set(ref capturing,value); StartCommand.Refresh(); } }
    public string Countdown { get => countdown; private set => Set(ref countdown,value); }
    public string Notification { get => notification; private set => Set(ref notification,value); }
    public bool NeedsCore => savedCore is null;
    public void SetCore(CoreInformationData? core) { savedCore = core; Changed(nameof(NeedsCore)); StartCommand.Refresh(); }
    private async Task CaptureAsync()
    {
        if (savedCore is not { } core || IsCapturing || disposed) return;
        var start = DateTimeOffset.Now; // Capture the command activation time before any asynchronous work.
        IsCapturing = true;
        try
        {
            var data = await measurement.CaptureAsync(start,
                remaining => Countdown = $"00:{Math.Clamp((int)Math.Ceiling(remaining.TotalSeconds),0,5):00}", lifetime.Token);
            lifetime.Token.ThrowIfCancellationRequested();
            if (data.Samples.Count == 0) throw new InvalidOperationException("Capture contains no samples.");
            Results.Add(new MeasurementResult { CaptureNumber = nextCapture++, StartTime = data.StartTime,
                EndTime = data.EndTime, SampleCount = data.Samples.Count, AverageValue = data.Samples.Average(),
                Unit = data.Unit, Core = core });
            Renumber();
            // Briefly render zero; the measurement itself still ends at exactly start + five seconds.
            await Task.Delay(100, lifetime.Token);
        }
        finally { Countdown = "00:05"; IsCapturing = false; }
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
        lifetime.Cancel(); notificationLifetime?.Cancel(); notificationLifetime?.Dispose(); lifetime.Dispose();
    }
}
