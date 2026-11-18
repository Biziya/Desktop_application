using System.IO;
using System.Reflection;

namespace Frontend.Shared.Infrastructure;

/// <summary>
/// Unhandled exceptions, appended to Logs\crash-yyyyMMdd.log under the app's data folder, so a crash on a user's PC
/// leaves its stack trace behind. Writing never throws: a folder that cannot be written only loses the log.
/// </summary>
public static class CrashLog
{
    public static string DefaultFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CT007_Measurement_Studio", "Logs");

    /// <returns>The log file, or null when it could not be written.</returns>
    public static string? Write(string source, Exception error, string? folder = null, DateTime? now = null)
    {
        try
        {
            var time = now ?? DateTime.Now;
            var directory = folder ?? DefaultFolder;
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"crash-{time:yyyyMMdd}.log");
            var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
            File.AppendAllText(path, $"{time:yyyy-MM-dd HH:mm:ss.fff}  {source}  version {version}{Environment.NewLine}{error}{Environment.NewLine}{Environment.NewLine}");
            return path;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
