using System.IO;
using Microsoft.Data.Sqlite;

namespace Frontend.Calculation.CoreInformation;

/// <summary>
/// Read-only availability check of a SQLite file, shared by the measurement and Gamma Guard database rows. It opens
/// the file with Mode=ReadOnly (which never creates a file), no connection pooling (the file is not kept open) and a
/// short busy timeout, reads the schema table and closes. Nothing is written.
/// </summary>
public static class SqliteFileCheck
{
    private const int BusyTimeoutSeconds = 2;

    public static LocalStatus Check(string path, string missingDetail)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new LocalStatus(LocalState.Missing, "File not found", missingDetail, path);
            }

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
                DefaultTimeout = BusyTimeoutSeconds
            }.ToString();

            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table'";
            var tables = Convert.ToInt64(command.ExecuteScalar());

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
