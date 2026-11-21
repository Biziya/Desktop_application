using System.IO;
using Microsoft.Data.Sqlite;

namespace Frontend.Api.CoreInformation.Drillholes;

/// <summary>
/// Last successful drillhole list, cached in SQLite so it is available on start and while offline.
/// Lives in its own table and never touches measurement data.
/// </summary>
public sealed class DrillholeCache(string databasePath)
{
    // LocalDrillholes: drillholes created on this laptop (Core Information, New mode). They are never replaced by a
    // backend refresh and never sent to the central drillhole list; only the Drillhole, Project and Campaign columns of
    // uploaded measurements carry their values.
    private const string CreateTable = """
        CREATE TABLE IF NOT EXISTS DrillholeCache (
            hole_id      TEXT NOT NULL PRIMARY KEY,
            project_code TEXT,
            campaign     TEXT
        );
        CREATE TABLE IF NOT EXISTS LocalDrillholes (
            hole_id      TEXT NOT NULL PRIMARY KEY COLLATE NOCASE,
            project_code TEXT NOT NULL,
            campaign     TEXT NOT NULL,
            created_at   TEXT NOT NULL
        );
        """;

    public Task<IReadOnlyList<DrillholeResponse>> LoadLocalAsync() => Task.Run<IReadOnlyList<DrillholeResponse>>(() =>
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT hole_id, project_code, campaign FROM LocalDrillholes ORDER BY created_at";
        using var reader = command.ExecuteReader();
        var drillholes = new List<DrillholeResponse>();

        while (reader.Read())
        {
            drillholes.Add(new DrillholeResponse(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        return drillholes;
    });

    /// <summary>Stores a drillhole created on this laptop. All three values are required.</summary>
    /// <exception cref="InvalidOperationException">A local drillhole with this Hole ID (any case) already exists.</exception>
    public Task AddLocalAsync(DrillholeResponse drillhole) => Task.Run(() =>
    {
        if (string.IsNullOrWhiteSpace(drillhole.HoleId) || string.IsNullOrWhiteSpace(drillhole.ProjectCode) ||
            string.IsNullOrWhiteSpace(drillhole.Campaign))
        {
            throw new ArgumentException("Drillhole, Project and Campaign are required.", nameof(drillhole));
        }

        using var connection = Open();
        using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO LocalDrillholes (hole_id, project_code, campaign, created_at)
            VALUES ($holeId, $projectCode, $campaign, $createdAt)
            ON CONFLICT (hole_id) DO NOTHING
            """;
        insert.Parameters.AddWithValue("$holeId", drillhole.HoleId.Trim());
        insert.Parameters.AddWithValue("$projectCode", drillhole.ProjectCode.Trim());
        insert.Parameters.AddWithValue("$campaign", drillhole.Campaign.Trim());
        insert.Parameters.AddWithValue("$createdAt", DateTimeOffset.Now.ToString("O"));

        if (insert.ExecuteNonQuery() == 0)
        {
            throw new InvalidOperationException($"Drillhole {drillhole.HoleId.Trim()} already exists.");
        }
    });

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

    // All or nothing; call only with a successful response.
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

