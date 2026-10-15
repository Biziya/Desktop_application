using System.IO.Enumeration;
using System.Runtime.InteropServices;
using System.Security;

namespace Backend.Repositories.CoreInformation.SqliteDatabase;

/// <summary>
/// Locates the CT008 log database (ct008-log.sqlite) on this computer without opening it.
/// <para>
/// Search order:
/// 1. Folders whose name contains "CT008" near the user's Downloads, Desktop, Documents,
///    profile, AppData, Program Files, ProgramData and the system drive root (shallow scan),
///    then a recursive search inside those folders only.
/// 2. Only if nothing was found: a depth-limited search of Downloads, Desktop and Documents.
/// </para>
/// The result is cached briefly so repeated requests do not rescan the disk.
/// </summary>
public sealed class SqliteDatabaseRepository(ILogger<SqliteDatabaseRepository> logger)
{
    public const string DatabaseFileName = "ct008-log.sqlite";

    private const string ApplicationFolderMarker = "CT008";
    private const int ApplicationFolderSearchDepth = 2;
    private const int InsideApplicationFolderSearchDepth = 8;
    private const int UserFolderFallbackSearchDepth = 6;

    private static readonly TimeSpan SearchTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(1);

    // Large or system-owned folders that never contain the CT008 application.
    private static readonly HashSet<string> SkippedFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "$Recycle.Bin", "System Volume Information", "Windows", "WindowsApps",
        "node_modules", ".git", ".vs", ".nuget", "Temp", "Cache", "Caches",
    };

    // Windows FOLDERID_Downloads; Downloads has no Environment.SpecialFolder value.
    private static readonly Guid DownloadsFolderId = new("374DE290-123F-4565-9164-39C4925E467B");

    private readonly SemaphoreSlim _searchLock = new(1, 1);
    private string _cachedPath = string.Empty;
    private DateTime _cachedAtUtc = DateTime.MinValue;

    public async Task<string> FindDatabasePathAsync(CancellationToken cancellationToken)
    {
        await _searchLock.WaitAsync(cancellationToken);
        try
        {
            if (IsCacheValid())
            {
                return _cachedPath;
            }

            _cachedPath = await Task.Run(
                () => DiscoverDatabasePath(GetApplicationSearchRoots(), GetUserSearchRoots(), cancellationToken),
                cancellationToken);
            _cachedAtUtc = DateTime.UtcNow;
            return _cachedPath;
        }
        finally
        {
            _searchLock.Release();
        }
    }

    /// <summary>
    /// Validation shared by automatic discovery and future manual selection.
    /// Only reads file metadata; the database itself is never opened.
    /// </summary>
    public static bool IsDatabaseFile(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && File.Exists(path)
        && string.Equals(Path.GetFileName(path), DatabaseFileName, StringComparison.OrdinalIgnoreCase);

    internal string DiscoverDatabasePath(
        IReadOnlyCollection<string> applicationRoots,
        IReadOnlyCollection<string> userRoots,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(SearchTimeout);

        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var applicationFolders = applicationRoots
                .SelectMany(root => SafeEnumerate(root, ApplicationFolderSearchDepth, isDirectory: true, IsApplicationFolderName, timeout.Token))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var folder in applicationFolders)
            {
                candidates.UnionWith(SafeEnumerate(folder, InsideApplicationFolderSearchDepth, isDirectory: false, IsDatabaseFileName, timeout.Token));
            }

            if (candidates.Count == 0)
            {
                foreach (var root in userRoots)
                {
                    candidates.UnionWith(SafeEnumerate(root, UserFolderFallbackSearchDepth, isDirectory: false, IsDatabaseFileName, timeout.Token));
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("SQLite database search timed out after {Timeout}; using {Count} candidate(s) found so far.", SearchTimeout, candidates.Count);
        }

        return SelectMostRecent(candidates);
    }

    /// <summary>Most recently modified valid file wins; full path breaks ties deterministically.</summary>
    internal static string SelectMostRecent(IEnumerable<string> candidates) =>
        candidates
            .Where(IsDatabaseFile)
            .Select(path => new FileInfo(Path.GetFullPath(path)))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenBy(file => file.FullName, StringComparer.OrdinalIgnoreCase)
            .Select(file => file.FullName)
            .FirstOrDefault() ?? string.Empty;

    private bool IsCacheValid() =>
        DateTime.UtcNow - _cachedAtUtc < CacheDuration
        && (_cachedPath.Length == 0 || IsDatabaseFile(_cachedPath));

    private static bool IsApplicationFolderName(ReadOnlySpan<char> name) =>
        name.Contains(ApplicationFolderMarker, StringComparison.OrdinalIgnoreCase);

    private static bool IsDatabaseFileName(ReadOnlySpan<char> name) =>
        name.Equals(DatabaseFileName, StringComparison.OrdinalIgnoreCase);

    private delegate bool NameFilter(ReadOnlySpan<char> name);

    /// <summary>
    /// Depth-limited enumeration that skips inaccessible folders, symlinks/junctions and
    /// known noisy folders. A missing or unreadable root yields nothing instead of throwing.
    /// </summary>
    private List<string> SafeEnumerate(string root, int maxDepth, bool isDirectory, NameFilter nameFilter, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            MaxRecursionDepth = maxDepth,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint, // AppData is hidden, so hidden folders are not skipped.
        };

        var entries = new FileSystemEnumerable<string>(root, (ref FileSystemEntry entry) => entry.ToFullPath(), options)
        {
            ShouldIncludePredicate = (ref FileSystemEntry entry) =>
                entry.IsDirectory == isDirectory && nameFilter(entry.FileName),
            ShouldRecursePredicate = (ref FileSystemEntry entry) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return !SkippedFolderNames.Contains(entry.FileName.ToString());
            },
        };

        try
        {
            return entries.ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            logger.LogDebug(ex, "Skipping unreadable folder {Folder}.", root);
            return [];
        }
    }

    private static IReadOnlyCollection<string> GetUserSearchRoots() =>
        DistinctExisting(
            GetDownloadsFolder(),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));

    private static IReadOnlyCollection<string> GetApplicationSearchRoots() =>
        DistinctExisting(
        [
            .. GetUserSearchRoots(),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Path.GetPathRoot(Environment.SystemDirectory) ?? string.Empty,
        ]);

    private static IReadOnlyCollection<string> DistinctExisting(params string[] folders) =>
        folders
            .Where(folder => !string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string GetDownloadsFolder()
    {
        try
        {
            // Respects a Downloads folder the user has moved to another drive.
            return SHGetKnownFolderPath(DownloadsFolderId, 0, IntPtr.Zero);
        }
        catch (Exception ex) when (ex is COMException or DllNotFoundException or EntryPointNotFoundException)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, PreserveSig = false)]
    private static extern string SHGetKnownFolderPath(
        [MarshalAs(UnmanagedType.LPStruct)] Guid folderId, uint flags, IntPtr token);
}
