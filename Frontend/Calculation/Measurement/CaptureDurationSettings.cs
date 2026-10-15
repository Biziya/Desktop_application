using System.IO;
using System.Text.Json;

namespace Frontend.Calculation.Measurement;

/// <summary>
/// The capture duration (seconds), edited by clicking the countdown and kept in a small JSON file next to the
/// measurement database (%LocalAppData%\CT007_Measurement_Studio\capture-settings.json). This is the one place the
/// limits are defined: whole seconds from <see cref="MinSeconds"/> to <see cref="MaxSeconds"/>; 5 s when nothing is
/// saved. The upper limit keeps one capture (one averaged row, one sample per second) within ten minutes.
/// Historical measurement records are never touched.
/// </summary>
public sealed class CaptureDurationSettings(string? path)
{
    public const int DefaultSeconds = 5;
    public const int MinSeconds = 1;
    public const int MaxSeconds = 600;

    /// <summary>Settings file beside the measurement database.</summary>
    public static string PathBeside(string databasePath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(databasePath))!, "capture-settings.json");

    /// <summary>The saved duration, or 5 s when none (or an invalid one) is saved.</summary>
    public int Load()
    {
        try
        {
            if (path is null || !File.Exists(path))
            {
                return DefaultSeconds;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.TryGetProperty("captureDurationSeconds", out var value) && value.TryGetInt32(out var seconds) &&
                   IsValid(seconds) ? seconds : DefaultSeconds;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return DefaultSeconds;
        }
    }

    /// <summary>Stores the duration (written to a temporary file, then moved over the old one).</summary>
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

    /// <summary>Parses the editor text: whole seconds only, within the limits.</summary>
    public static bool TryParse(string text, out int seconds) =>
        int.TryParse(text.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out seconds) && IsValid(seconds);
}
