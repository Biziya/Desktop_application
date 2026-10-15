using System.IO;

namespace Frontend.Calculation.CoreInformation.GammaGuardDatabase;

/// <summary>
/// Finds the Gamma Guard SQLite database on THIS laptop and checks it read-only (it is never modified).
/// Discovery rules, in order:
/// 1. <see cref="DeviceSettings.GammaGuardDatabasePath"/> when set (environment variables expanded) — used as is,
///    so a wrong path shows as "File not found" with that path.
/// 2. Otherwise the Gamma Guard install layout <c>…\GammaGuard\&lt;version&gt;\Database\&lt;file&gt;.sqlite</c>
///    (e.g. <c>GammaGuard\CT008 1.9.0\Database\ct008-log.sqlite</c>) is searched under the current user's
///    Documents, Desktop and profile folders, LocalAppData, AppData, ProgramData and Program Files: folders whose
///    name contains "GammaGuard" or "Gamma Guard", then files matching <see cref="DeviceSettings.GammaGuardFileName"/>
///    in a "Database" folder below them. The most recently written file wins.
/// The search is bounded in depth, skips folders it may not read, and its result is reused until the file
/// disappears or Retry is used.
/// </summary>
public sealed class GammaGuardDatabaseCheck(DeviceSettings settings, IReadOnlyList<(string Root, int Depth)>? searchRoots = null)
{
    private readonly IReadOnlyList<(string Root, int Depth)> roots = searchRoots ?? DefaultRoots();
    private string? discovered;

    public LocalStatus Check(bool rediscover = false)
    {
        if (settings.GammaGuardDatabasePath.Length > 0)
        {
            var configured = Environment.ExpandEnvironmentVariables(settings.GammaGuardDatabasePath);
            return SqliteFileCheck.Check(configured, "The path set in device-settings.json does not exist on this laptop.");
        }

        if (rediscover || discovered is null || !File.Exists(discovered))
        {
            discovered = Discover();
        }

        return discovered is null
            ? new LocalStatus(LocalState.NotFound, "Not found",
                "No Gamma Guard database was found on this laptop. Set gammaGuardDatabasePath in device-settings.json if it is elsewhere.")
            : SqliteFileCheck.Check(discovered, "The Gamma Guard database was moved or deleted.");
    }

    public string? Discover()
    {
        var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
        var candidates = new List<FileInfo>();

        foreach (var (root, depth) in roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            try
            {
                var installs = Directory.EnumerateDirectories(root, "*", new EnumerationOptions
                    {
                        IgnoreInaccessible = true,
                        RecurseSubdirectories = true,
                        MaxRecursionDepth = depth,
                        AttributesToSkip = FileAttributes.ReparsePoint
                    })
                    .Where(IsGammaGuardFolder);

                foreach (var install in installs)
                {
                    options.MaxRecursionDepth = 3;
                    candidates.AddRange(Directory.EnumerateFiles(install, settings.GammaGuardFileName, options)
                        .Where(f => string.Equals(Path.GetFileName(Path.GetDirectoryName(f)), "Database", StringComparison.OrdinalIgnoreCase))
                        .Select(f => new FileInfo(f)));
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // A root that cannot be searched is skipped; the others are still searched.
            }
        }

        return candidates
            .GroupBy(f => f.FullName, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .OrderByDescending(f => f.LastWriteTimeUtc).ThenBy(f => f.FullName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()?.FullName;
    }

    private static bool IsGammaGuardFolder(string directory)
    {
        var name = Path.GetFileName(directory).Replace(" ", "");
        return name.Contains("GammaGuard", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<(string Root, int Depth)> DefaultRoots()
    {
        string Folder(Environment.SpecialFolder folder) => Environment.GetFolderPath(folder);

        return new (string, int)[]
            {
                (Folder(Environment.SpecialFolder.MyDocuments), 4),
                (Folder(Environment.SpecialFolder.DesktopDirectory), 3),
                (Folder(Environment.SpecialFolder.UserProfile), 2),
                (Folder(Environment.SpecialFolder.LocalApplicationData), 2),
                (Folder(Environment.SpecialFolder.ApplicationData), 2),
                (Folder(Environment.SpecialFolder.CommonApplicationData), 2),
                (Folder(Environment.SpecialFolder.ProgramFiles), 2),
                (Folder(Environment.SpecialFolder.ProgramFilesX86), 2)
            }
            .Where(r => r.Item1.Length > 0)
            .ToList();
    }
}
