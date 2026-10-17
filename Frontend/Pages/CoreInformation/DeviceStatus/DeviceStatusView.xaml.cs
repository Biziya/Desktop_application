using System.Windows.Controls;

namespace Frontend.Pages.CoreInformation.DeviceStatus;

// View only: device checks and database access run in DeviceStatusMonitor (Calculation/Core-Information).
public partial class DeviceStatusView : UserControl
{
    public DeviceStatusView() => InitializeComponent();

    // Opening the Bluetooth device dropdown reads the device list again, so a newly paired CT007-M appears.
    private void BluetoothDropDownOpened(object sender, EventArgs e)
    {
        if (DataContext is CoreInformationViewModel vm)
        {
            _ = vm.Devices.RefreshBluetoothDevicesAsync();
        }
    }
}
