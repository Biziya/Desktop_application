using System.IO;
using System.Text.Json;

namespace Frontend.Calculation.Measurement;

/// <summary>
/// The capture duration in whole seconds, saved in capture-settings.json beside the measurement database. The single
/// source of the limits; an out-of-range saved value (e.g. from an older version) falls back to the default.
/// </summary>
public sealed class CaptureDurationSettings(string? path)
{
    public const int DefaultSeconds = 5;
    public const int MinSeconds = 2;
    public const int MaxSeconds = 10;

    public static string PathBeside(string databasePath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(databasePath))!, "capture-settings.json");

    public int Load()
    {
        try
        {
            if (path is null || !File.Exists(path))
            {
                return DefaultSeconds;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            // A hand-edited file may hold any JSON: only an object with a whole number in range is used.
            return document.RootElement is { ValueKind: JsonValueKind.Object } root &&
                   root.TryGetProperty("captureDurationSeconds", out var value) &&
                   value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var seconds) &&
                   IsValid(seconds) ? seconds : DefaultSeconds;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return DefaultSeconds;
        }
    }

    // Written to a temporary file, then moved over the old one.
    public void Save(int seconds)
    {
        if (path is null)
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new { captureDurationSeconds = seconds }));
        File.Move(temporary, path, true);
    }

    public static bool IsValid(int seconds) => seconds is >= MinSeconds and <= MaxSeconds;

    public static bool TryParse(string text, out int seconds) =>
        int.TryParse(text.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out seconds) && IsValid(seconds);
}
