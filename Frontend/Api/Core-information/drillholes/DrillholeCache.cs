using System.IO;
using Microsoft.Data.Sqlite;

namespace Frontend.Api.CoreInformation.Drillholes;

/// <summary>
/// Last successful GET /api/core-information/drillholes response, kept in the application's SQLite database so the
/// list is available immediately on start and while the backend is unreachable. Reference data only: it lives in its
/// own table (DrillholeCache) and never touches measurement sessions or records.
/// </summary>
public sealed class DrillholeCache(string databasePath)
{
    private const string CreateTable = """
        CREATE TABLE IF NOT EXISTS DrillholeCache (
            hole_id      TEXT NOT NULL PRIMARY KEY,
            project_code TEXT,
            campaign     TEXT
        )
        """;

    /// <summary>The cached list, or an empty list when nothing has been cached yet.</summary>
    public Task<IReadOnlyList<DrillholeResponse>> LoadAsync() => Task.Run<IReadOnlyList<DrillholeResponse>>(() =>
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT hole_id, project_code, campaign FROM DrillholeCache";
        using var reader = command.ExecuteReader();
        var drillholes = new List<DrillholeResponse>();

        while (reader.Read())
        {
            drillholes.Add(new DrillholeResponse(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        return drillholes;
    });

    /// <summary>Replaces the whole cached list in one transaction (all or nothing). Call only with a successful response.</summary>
    public Task ReplaceAsync(IReadOnlyList<DrillholeResponse> drillholes) => Task.Run(() =>
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        using (var clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = "DELETE FROM DrillholeCache";
            clear.ExecuteNonQuery();
        }

        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO DrillholeCache (hole_id, project_code, campaign) VALUES ($holeId, $projectCode, $campaign)
            ON CONFLICT (hole_id) DO UPDATE SET project_code = excluded.project_code, campaign = excluded.campaign
            """;

        foreach (var drillhole in drillholes)
        {
            insert.Parameters.Clear();
            insert.Parameters.AddWithValue("$holeId", drillhole.HoleId!);
            insert.Parameters.AddWithValue("$projectCode", (object?)drillhole.ProjectCode ?? DBNull.Value);
            insert.Parameters.AddWithValue("$campaign", (object?)drillhole.Campaign ?? DBNull.Value);
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    });

    private SqliteConnection Open()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString());
        connection.Open();

        using var create = connection.CreateCommand();
        create.CommandText = CreateTable;
        create.ExecuteNonQuery();
        return connection;
    }
}
