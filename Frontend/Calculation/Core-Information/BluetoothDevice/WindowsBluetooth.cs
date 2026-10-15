using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Frontend.Calculation.CoreInformation.BluetoothDevice;

/// <summary>
/// Lists the Bluetooth (BR/EDR — the CT-007 uses the Serial Port Profile) devices this laptop knows, with the Windows
/// Bluetooth API (BluetoothApis.dll). It only reads Windows' device list: no inquiry scan, no pairing, no connection.
/// </summary>
internal static class WindowsBluetooth
{
    private const int ErrorNoMoreItems = 259;

    public static IReadOnlyList<KnownBluetoothDevice> ListKnownDevices()
    {
        // Without a radio that is switched on, the state of the device cannot be read.
        var radioParams = new FindRadioParams { Size = Marshal.SizeOf<FindRadioParams>() };
        var radioFind = BluetoothFindFirstRadio(ref radioParams, out var radio);

        if (radioFind == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(error == ErrorNoMoreItems
                ? "No Bluetooth adapter is available on this laptop, or Bluetooth is turned off."
                : new Win32Exception(error).Message);
        }

        CloseHandle(radio);
        BluetoothFindRadioClose(radioFind);

        var search = new DeviceSearchParams
        {
            Size = Marshal.SizeOf<DeviceSearchParams>(),
            ReturnAuthenticated = true,
            ReturnRemembered = true,
            ReturnConnected = true,
            ReturnUnknown = false,
            IssueInquiry = false,
            Radio = IntPtr.Zero
        };
        var info = new DeviceInfo { Size = Marshal.SizeOf<DeviceInfo>() };
        var devices = new List<KnownBluetoothDevice>();
        var find = BluetoothFindFirstDevice(ref search, ref info);

        if (find == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            return error == ErrorNoMoreItems ? devices : throw new Win32Exception(error);
        }

        try
        {
            do
            {
                devices.Add(new KnownBluetoothDevice(info.Name ?? "", info.Address, info.Connected, info.Remembered || info.Authenticated));
                info = new DeviceInfo { Size = Marshal.SizeOf<DeviceInfo>() };
            }
            while (BluetoothFindNextDevice(find, ref info));
        }
        finally
        {
            BluetoothFindDeviceClose(find);
        }

        return devices;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FindRadioParams
    {
        public int Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceSearchParams
    {
        public int Size;
        [MarshalAs(UnmanagedType.Bool)] public bool ReturnAuthenticated;
        [MarshalAs(UnmanagedType.Bool)] public bool ReturnRemembered;
        [MarshalAs(UnmanagedType.Bool)] public bool ReturnUnknown;
        [MarshalAs(UnmanagedType.Bool)] public bool ReturnConnected;
        [MarshalAs(UnmanagedType.Bool)] public bool IssueInquiry;
        public byte TimeoutMultiplier;
        public IntPtr Radio;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemTime
    {
        public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DeviceInfo
    {
        public int Size;
        public ulong Address;
        public uint ClassOfDevice;
        [MarshalAs(UnmanagedType.Bool)] public bool Connected;
        [MarshalAs(UnmanagedType.Bool)] public bool Remembered;
        [MarshalAs(UnmanagedType.Bool)] public bool Authenticated;
        public SystemTime LastSeen;
        public SystemTime LastUsed;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)] public string? Name;
    }

    [DllImport("BluetoothApis.dll", SetLastError = true)]
    private static extern IntPtr BluetoothFindFirstRadio(ref FindRadioParams parameters, out IntPtr radio);

    [DllImport("BluetoothApis.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindRadioClose(IntPtr find);

    [DllImport("BluetoothApis.dll", SetLastError = true)]
    private static extern IntPtr BluetoothFindFirstDevice(ref DeviceSearchParams search, ref DeviceInfo info);

    [DllImport("BluetoothApis.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindNextDevice(IntPtr find, ref DeviceInfo info);

    [DllImport("BluetoothApis.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindDeviceClose(IntPtr find);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
