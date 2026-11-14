using System.IO;
using System.Text.Json;

namespace Frontend.Calculation.CoreInformation.GammaGuardDatabase;

/// <summary>
/// Finds the Gamma Guard SQLite database on this laptop and checks it read-only. Order:
/// 1. the user's saved selection, while usable (it stays saved and is used again once the file is back);
/// 2. <see cref="DeviceSettings.GammaGuardDatabasePath"/> when set, used as is;
/// 3. a bounded search for <c>…\GammaGuard*\&lt;version&gt;\Database\&lt;file&gt;</c> in the usual user/program folders;
///    the newest file wins and is cached until it disappears or Retry is used.
/// Only a selected file must also have the Gamma Guard tables, since it can be any file. Thread-safe.
/// </summary>
/// <param name="selectionFile">Where the selection is saved (JSON); null keeps it in memory only.</param>
public sealed class GammaGuardDatabaseCheck(
    DeviceSettings settings, IReadOnlyList<(string Root, int Depth)>? searchRoots = null, string? selectionFile = null)
{
    private const string SelectionKey = "gammaGuardDatabasePath";

    private readonly IReadOnlyList<(string Root, int Depth)> roots = searchRoots ?? DefaultRoots();
    private readonly object gate = new();
    private string? discovered;
    private string? selected;
    private bool selectionLoaded;

    // Per user and outside the install folder, so updates keep it.
    public static string SelectionFileBeside(string measurementDatabasePath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(measurementDatabasePath))!, "Config", "gamma-guard-database.json");

    public string? SelectedPath
    {
        get
        {
            lock (gate)
            {
                LoadSelection();
                return selected;
            }
        }
    }

    public LocalStatus Check(bool rediscover = false)
    {
        lock (gate)
        {
            LoadSelection();

            if (selected is null)
            {
                return CheckAutomatic(rediscover);
            }

            var manual = Validate(selected, "The selected Gamma Guard database was moved or deleted.");

            if (manual.State is LocalState.Available or LocalState.Busy)
            {
                return manual with { Detail = $"Selected manually. {manual.Detail}" };
            }

            var automatic = CheckAutomatic(rediscover);
            return automatic.State == LocalState.Available
                ? automatic with
                {
                    Detail = $"The selected database cannot be used ({manual.Text.ToLowerInvariant()}: {selected}); using the one found automatically. {automatic.Detail}"
                }
                : manual;
        }
    }

    /// <summary>Saves <paramref name="path"/> as the selection if it is a valid Gamma Guard database; otherwise nothing changes.</summary>
    /// <exception cref="IOException">The selection could not be saved; nothing changed.</exception>
    /// <exception cref="UnauthorizedAccessException">The selection could not be saved; nothing changed.</exception>
    public LocalStatus Select(string path)
    {
        var full = Path.GetFullPath(path);
        var status = Validate(full, "The selected file does not exist.");

        if (status.State != LocalState.Available)
        {
            return status;
        }

        lock (gate)
        {
            SaveSelection(full);
            selected = full;
            selectionLoaded = true;
        }

        return status with { Detail = $"Selected manually. {status.Detail}" };
    }

    public string? Discover()
    {
        var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true, MaxRecursionDepth = 3, AttributesToSkip = FileAttributes.ReparsePoint };
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

    private static LocalStatus Validate(string path, string missingDetail) =>
        SqliteFileCheck.Check(path, missingDetail, GammaGuardDeviceDatabase.StructureProblem);

    // Rules 2 and 3: the configured path, else the discovered one.
    private LocalStatus CheckAutomatic(bool rediscover)
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
                "No Gamma Guard database was found on this laptop. Use Select Database to choose the file.")
            : SqliteFileCheck.Check(discovered, "The Gamma Guard database was moved or deleted.");
    }

    // Read once; a missing or unreadable file means no selection (the automatic rules apply).
    private void LoadSelection()
    {
        if (selectionLoaded)
        {
            return;
        }

        selectionLoaded = true;

        try
        {
            if (selectionFile is null || !File.Exists(selectionFile))
            {
                return;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(selectionFile));
            var root = document.RootElement;
            selected = root.ValueKind == JsonValueKind.Object && root.TryGetProperty(SelectionKey, out var value) &&
                       value.ValueKind == JsonValueKind.String && value.GetString()!.Trim() is { Length: > 0 } path
                ? path
                : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            selected = null;
        }
    }

    // Written to a temporary file, then moved over the old one.
    private void SaveSelection(string path)
    {
        if (selectionFile is null)
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(selectionFile))!);
        var temporary = selectionFile + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new Dictionary<string, string> { [SelectionKey] = path }));
        File.Move(temporary, selectionFile, true);
    }

    // Matches "GammaGuard" and "Gamma Guard".
    private static bool IsGammaGuardFolder(string directory) =>
        Path.GetFileName(directory).Replace(" ", "").Contains("GammaGuard", StringComparison.OrdinalIgnoreCase);

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
