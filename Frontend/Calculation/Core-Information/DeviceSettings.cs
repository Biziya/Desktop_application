using System.IO;
using System.Text.Json;

namespace Frontend.Calculation.CoreInformation;

/// <summary>
/// Rules for the local Device Status checks, read from Calculation/Core-Information/device-settings.json next to the
/// executable (editable without rebuilding). Missing or invalid values fall back to the defaults below.
/// </summary>
/// <param name="Ct007NamePrefix">A Bluetooth device whose name begins with this (ignoring case) is a CT007-M
/// detector: "CT007-M-".</param>
/// <param name="Ct007Address">Optional Bluetooth address (e.g. "00:11:22:AA:BB:CC"); when set it identifies the device
/// on its own, whatever its name.</param>
/// <param name="GammaGuardDatabasePath">Optional fixed path of the Gamma Guard database (environment variables such
/// as %USERPROFILE% are expanded). Empty: the path is discovered.</param>
/// <param name="GammaGuardFileName">File name (wildcards allowed) of the Gamma Guard database used by discovery.</param>
/// <param name="CheckInterval">How often the database checks run.</param>
/// <param name="BluetoothCheckInterval">How often the Bluetooth connection is checked (1 s).</param>
public sealed record DeviceSettings(
    string Ct007NamePrefix,
    string Ct007Address,
    string GammaGuardDatabasePath,
    string GammaGuardFileName,
    TimeSpan CheckInterval,
    TimeSpan BluetoothCheckInterval)
{
    public static DeviceSettings Default { get; } =
        new("CT007-M-", "", "", "*.sqlite", TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1));

    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "Calculation", "Core-Information", "device-settings.json");

    public static DeviceSettings Load(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;

            return new DeviceSettings(
                Text(root, "ct007NamePrefix", Default.Ct007NamePrefix) is { Length: > 0 } prefix ? prefix : Default.Ct007NamePrefix,
                Text(root, "ct007Address", Default.Ct007Address),
                Text(root, "gammaGuardDatabasePath", Default.GammaGuardDatabasePath),
                Text(root, "gammaGuardFileName", Default.GammaGuardFileName) is { Length: > 0 } file ? file : Default.GammaGuardFileName,
                Seconds(root, "checkIntervalSeconds", Default.CheckInterval),
                Seconds(root, "bluetoothCheckIntervalSeconds", Default.BluetoothCheckInterval));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return Default;
        }
    }

    private static TimeSpan Seconds(JsonElement root, string name, TimeSpan fallback) =>
        root.TryGetProperty(name, out var value) && value.TryGetDouble(out var seconds) && seconds > 0 ? TimeSpan.FromSeconds(seconds) : fallback;

    private static string Text(JsonElement root, string name, string fallback) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()!.Trim() : fallback;
}
