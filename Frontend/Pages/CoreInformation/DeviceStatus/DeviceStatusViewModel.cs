using Frontend.Calculation.CoreInformation;
using Frontend.Pages.Measurement;
using Frontend.Shared.Infrastructure;

namespace Frontend.Pages.CoreInformation.DeviceStatus;

/// <summary>The Device Status card: this laptop's CT007, measurement database and Gamma Guard database, and their buttons.</summary>
public sealed class DeviceStatusViewModel : ObservableObject
{
    private readonly ToastService toasts;
    private readonly IDialogService dialogs;
    private readonly Func<bool> isSelectionLocked;

    /// <param name="isSelectionLocked">True while measuring: the Gamma Guard database cannot change then.</param>
    public DeviceStatusViewModel(DeviceStatusMonitor devices, ToastService toasts, IDialogService dialogs, Func<bool> isSelectionLocked)
    {
        Devices = devices;
        this.toasts = toasts;
        this.dialogs = dialogs;
        this.isSelectionLocked = isSelectionLocked;
        RefreshMeasurementDatabaseCommand = new RelayCommand(_ => _ = devices.CheckMeasurementDatabaseAsync());
        RefreshGammaGuardDatabaseCommand = new RelayCommand(_ => _ = devices.CheckGammaGuardDatabaseAsync(rediscover: true));
        SelectGammaGuardDatabaseCommand = new AsyncCommand(SelectGammaGuardDatabaseAsync,
            error => toasts.Error($"The Gamma Guard database could not be changed: {error.Message}"));
    }

    public DeviceStatusMonitor Devices { get; }

    public RelayCommand RefreshMeasurementDatabaseCommand { get; }

    public RelayCommand RefreshGammaGuardDatabaseCommand { get; }

    public AsyncCommand SelectGammaGuardDatabaseCommand { get; }

    // Cancelling the dialog or choosing an unusable file keeps the current database. Not while measuring: the
    // samples of one drillhole run come from one database.
    private async Task SelectGammaGuardDatabaseAsync()
    {
        if (isSelectionLocked())
        {
            toasts.Error("Stop the measurement before changing the Gamma Guard database.");
            return;
        }

        if (dialogs.PickGammaGuardDatabase(Devices.GammaGuardDatabase.Path) is not { } path)
        {
            return;
        }

        var status = await Devices.SelectGammaGuardDatabaseAsync(path);

        if (status.State == LocalState.Available)
        {
            toasts.Success($"Gamma Guard database selected: {status.Path}");
        }
        else
        {
            toasts.Error($"The Gamma Guard database was not changed. {status.Text}: {status.Detail}");
        }
    }
}
