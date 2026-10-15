using System.IO;
using System.Text.Json;

namespace Frontend.Calculation.CoreInformation;

/// <summary>
/// Rules for the local Device Status checks, read from Calculation/Core-Information/device-settings.json next to the
/// executable (editable without rebuilding). Missing or invalid values fall back to the defaults below.
/// </summary>
/// <param name="Ct007Names">A paired Bluetooth device whose name contains one of these (ignoring case, spaces and
/// dashes) is the CT-007.</param>
/// <param name="Ct007Address">Optional Bluetooth address (e.g. "00:11:22:AA:BB:CC"); when set it identifies the device
/// on its own, whatever its name.</param>
/// <param name="GammaGuardDatabasePath">Optional fixed path of the Gamma Guard database (environment variables such
/// as %USERPROFILE% are expanded). Empty: the path is discovered.</param>
/// <param name="GammaGuardFileName">File name (wildcards allowed) of the Gamma Guard database used by discovery.</param>
/// <param name="CheckInterval">How often the local checks run.</param>
public sealed record DeviceSettings(
    IReadOnlyList<string> Ct007Names,
    string Ct007Address,
    string GammaGuardDatabasePath,
    string GammaGuardFileName,
    TimeSpan CheckInterval)
{
    public static DeviceSettings Default { get; } = new(["CT007", "CT-007"], "", "", "*.sqlite", TimeSpan.FromSeconds(5));

    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "Calculation", "Core-Information", "device-settings.json");

    public static DeviceSettings Load(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;

            var names = root.TryGetProperty("ct007Names", out var list) && list.ValueKind == JsonValueKind.Array
                ? list.EnumerateArray().Select(n => n.GetString()?.Trim() ?? "").Where(n => n.Length > 0).ToList()
                : [];

            return new DeviceSettings(
                names.Count > 0 ? names : Default.Ct007Names,
                Text(root, "ct007Address", Default.Ct007Address),
                Text(root, "gammaGuardDatabasePath", Default.GammaGuardDatabasePath),
                Text(root, "gammaGuardFileName", Default.GammaGuardFileName) is { Length: > 0 } file ? file : Default.GammaGuardFileName,
                root.TryGetProperty("checkIntervalSeconds", out var seconds) && seconds.TryGetDouble(out var value) && value > 0
                    ? TimeSpan.FromSeconds(value) : Default.CheckInterval);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return Default;
        }
    }

    private static string Text(JsonElement root, string name, string fallback) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()!.Trim() : fallback;
}
