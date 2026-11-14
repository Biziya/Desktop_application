using System.IO;
using System.Text.Json;

namespace Frontend.Calculation.CoreInformation;

/// <summary>Device Status rules from device-settings.json beside the executable; invalid values fall back to <see cref="Default"/>.</summary>
/// <param name="Ct007Address">Optional; when set it identifies the device regardless of its name.</param>
/// <param name="GammaGuardDatabasePath">Optional fixed path (environment variables expanded); empty means discover it.</param>
/// <param name="GammaGuardFileName">Discovery file pattern (wildcards allowed).</param>
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
