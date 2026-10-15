namespace Backend.Models.CoreInformation.BluetoothDevice;

public sealed record BluetoothDeviceResponse(bool Connected, string DeviceName)
{
    public static BluetoothDeviceResponse NotConnected { get; } = new(false, string.Empty);
}
