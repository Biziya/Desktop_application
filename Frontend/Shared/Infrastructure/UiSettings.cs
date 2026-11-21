using System.IO;
using System.Text.Json;

namespace Frontend.Shared.Infrastructure;

/// <summary>
/// Small layout choices remembered between runs (today: the Help drawer's width), in
/// %LocalAppData%\CT007_Measurement_Studio\ui-settings.json. A missing or unreadable file means the defaults; a file
/// that can't be written is ignored (the choice then lasts for this run only).
/// </summary>
public sealed class UiSettings
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string? path;

    /// <param name="path">null: kept in memory only (tests).</param>
    public UiSettings(string? path = null) => this.path = path;

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CT007_Measurement_Studio", "ui-settings.json");

    /// <summary>Width the user dragged the Help drawer to; null = the default.</summary>
    public double? HelpDrawerWidth { get; set; }

    public static UiSettings Load(string path)
    {
        var settings = new UiSettings(path);

        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<Stored>(File.ReadAllText(path)) is { } stored
                && stored.HelpDrawerWidth is > 0 and < 10_000)
            {
                settings.HelpDrawerWidth = stored.HelpDrawerWidth;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            // Defaults.
        }

        return settings;
    }

    public void Save()
    {
        if (path is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new Stored { HelpDrawerWidth = HelpDrawerWidth }, Json));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Not remembered; nothing else depends on it.
        }
    }

    private sealed class Stored
    {
        public double? HelpDrawerWidth { get; set; }
    }
}
