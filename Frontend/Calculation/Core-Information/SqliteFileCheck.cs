using System.IO;
using Microsoft.Data.Sqlite;

namespace Frontend.Calculation.CoreInformation;

/// <summary>Read-only availability check of a SQLite file (measurement and Gamma Guard database rows).</summary>
public static class SqliteFileCheck
{
    private const int BusyTimeoutSeconds = 2;

    /// <summary>
    /// The only way to open a database this app must not change: ReadOnly never creates or writes the file, no pooling
    /// keeps it from staying open, and no PRAGMA is set, so the owner's journal settings stay untouched.
    /// </summary>
    public static SqliteConnection OpenReadOnly(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            DefaultTimeout = BusyTimeoutSeconds
        }.ToString());

        try
        {
            connection.Open();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    // A readable (or wrong) database keeps its result while the file is unchanged, so the periodic Device Status check
    // does not open it every few seconds. Busy or unreadable results are never kept: they are checked again each time.
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(60);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string Path, bool Verified), (string Stamp, DateTime CheckedAt, LocalStatus Status)> Results = new();

    /// <param name="verify">Optional: returns why the file is the wrong database ("Wrong database"), or null.</param>
    public static LocalStatus Check(string path, string missingDetail, Func<SqliteConnection, string?>? verify = null)
    {
        var key = (path, verify is not null);

        try
        {
            var file = new FileInfo(path);

            if (!file.Exists)
            {
                Results.TryRemove(key, out _);
                return new LocalStatus(LocalState.Missing, "File not found", missingDetail, path);
            }

            var stamp = Stamp(file);

            if (Results.TryGetValue(key, out var kept) && kept.Stamp == stamp && DateTime.UtcNow - kept.CheckedAt < CacheLifetime)
            {
                return kept.Status;
            }

            var status = Open(path, verify);

            if (status.State is LocalState.Available || status.Text == "Wrong database")
            {
                Results[key] = (stamp, DateTime.UtcNow, status);
            }
            else
            {
                Results.TryRemove(key, out _);
            }

            return status;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Results.TryRemove(key, out _);
            return new LocalStatus(LocalState.Inaccessible, "Cannot be opened", error.Message, path);
        }
    }

    // Size and write time of the database and its WAL file: a write in WAL mode changes only the -wal file.
    private static string Stamp(FileInfo file)
    {
        var wal = new FileInfo(file.FullName + "-wal");
        return $"{file.Length}|{file.LastWriteTimeUtc.Ticks}|{(wal.Exists ? $"{wal.Length}|{wal.LastWriteTimeUtc.Ticks}" : "-")}";
    }

    private static LocalStatus Open(string path, Func<SqliteConnection, string?>? verify)
    {
        try
        {
            using var connection = OpenReadOnly(path);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table'";
            var tables = Convert.ToInt64(command.ExecuteScalar());

            if (verify?.Invoke(connection) is { } problem)
            {
                return new LocalStatus(LocalState.Inaccessible, "Wrong database", problem, path);
            }

            return new LocalStatus(LocalState.Available, "Available", $"Readable SQLite database ({tables} tables).", path);
        }
        catch (SqliteException error) when (error.SqliteErrorCode is 5 or 6) // SQLITE_BUSY, SQLITE_LOCKED
        {
            return new LocalStatus(LocalState.Busy, "Busy", "In use by another program right now; checked again shortly.", path);
        }
        catch (SqliteException error) when (error.SqliteErrorCode is 26) // SQLITE_NOTADB
        {
            return new LocalStatus(LocalState.Inaccessible, "Cannot be read", "The file is not a SQLite database.", path);
        }
        catch (SqliteException error)
        {
            return new LocalStatus(LocalState.Inaccessible, "Cannot be opened", error.Message, path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new LocalStatus(LocalState.Inaccessible, "Cannot be opened", error.Message, path);
        }
    }
}
