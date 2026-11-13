using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Backend.Controllers.Measurement;
using Backend.Models.Measurement;
using Backend.Repositories.Measurement;
using Microsoft.Extensions.Logging;

namespace Ct007.Tests.BackendTests;

/// <summary>BE-DB: the SPP CT007 table, its record type, insert procedure and permissions (no SQL Server needed).</summary>
public static class SppCt007DatabaseTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 13, 9, 0, 0, TimeSpan.FromHours(8));

    private static string Script() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Database", "Migrations", SppCt007Database.MigrationScript));

    private static MeasurementUploadRecord Record(int capture, double? hdm) =>
        new(capture, Start.AddMinutes(capture), Start.AddMinutes(capture).AddSeconds(5), 5, capture * 0.1m, capture * 0.1m + 0.1m,
            null, 120.5, 121, hdm, null, 0.12);

    [Test("BE-DB-01", "The table-valued parameter has the record type's columns in order, row numbers from 1 and DBNull for missing values")]
    public static void RecordTable()
    {
        var table = SppCt007Database.ToRecordTable([Record(7, hdm: 10.5), Record(3, hdm: null)]);

        Assert.Sequence(SppCt007Database.RecordColumns.Select(c => c.Name), table.Columns.Cast<System.Data.DataColumn>().Select(c => c.ColumnName), "column order");
        Assert.Sequence(SppCt007Database.RecordColumns.Select(c => c.Type), table.Columns.Cast<System.Data.DataColumn>().Select(c => c.DataType), "column types");
        Assert.Equal(2, table.Rows.Count, "rows");
        Assert.Equal(7, (int)table.Rows[0]["CAPTURE_NO"], "capture kept");
        Assert.Sequence(new[] { 1, 2 }, table.Rows.Cast<System.Data.DataRow>().Select(r => (int)r["ROW_NO"]), "row numbers in request order");
        Assert.Equal(0.8m, (decimal)table.Rows[0]["DEPTH_TO_M"], "depth");
        Assert.Equal(Start.AddMinutes(3), (DateTimeOffset)table.Rows[1]["START_TIME"], "time with offset");
        Assert.True(table.Rows[1]["HDM"] is DBNull && table.Rows[0]["TS_MS"] is DBNull, "missing values are DBNull");
        Assert.Equal(10.5, (double)table.Rows[0]["HDM"], "value kept");
    }

    [Test("BE-DB-02", "The migration script creates the objects the backend calls, the record type matches the TVP, and the app role gets EXECUTE on the insert and SELECT only")]
    public static void ScriptMatchesBackend()
    {
        var script = Script();

        foreach (var expected in new[]
        {
            $"CREATE TABLE {SppCt007Database.Table} (",
            $"CREATE TYPE {SppCt007Database.RecordType} AS TABLE (",
            $"CREATE OR ALTER PROCEDURE {SppCt007Database.UploadProcedure}",
            $"@Records           {SppCt007Database.RecordType} READONLY",
            $"GRANT EXECUTE ON OBJECT::{SppCt007Database.UploadProcedure} TO {SppCt007Database.Role};",
            $"GRANT EXECUTE ON TYPE::{SppCt007Database.RecordType} TO {SppCt007Database.Role};",
            $"GRANT SELECT ON OBJECT::{SppCt007Database.Table} TO {SppCt007Database.Role};",
        })
        {
            Assert.Contains(expected, script, expected);
        }

        // Only comment lines may mention write rights; no statement grants them (rows are inserted by the procedure only).
        var statements = string.Join('\n', script.Split('\n').Where(line => !line.TrimStart().StartsWith("--", StringComparison.Ordinal)));
        Assert.False(Regex.IsMatch(statements, @"GRANT\s+(INSERT|UPDATE|DELETE|CONTROL|ALTER)", RegexOptions.IgnoreCase), "no direct write rights granted");
        Assert.False(Regex.IsMatch(statements, @"\b(UPDATE|DELETE)\s+(FROM\s+)?dbo\.SPPCT007", RegexOptions.IgnoreCase), "append-only: nothing updates or deletes rows");

        var type = Regex.Match(script, $@"CREATE TYPE {Regex.Escape(SppCt007Database.RecordType)} AS TABLE \((?<body>.*?)\n\s*\);", RegexOptions.Singleline);
        Assert.True(type.Success, "record type found");
        var columns = type.Groups["body"].Value.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith("--", StringComparison.Ordinal))
            .Select(line => line.Split(' ', 2)[0])
            .ToList();
        Assert.Sequence(SppCt007Database.RecordColumns.Select(c => c.Name), columns, "record type columns = TVP columns, in order");
    }

    [Test("BE-DB-03", "Upload before the SPP CT007 table exists: 503 saying the table has not been created, nothing stored, logged as a warning")]
    public static async Task UploadTableMissing()
    {
        await using var backend = await TestBackend.StartAsync(new FakeDrillholeRepository(), uploads: new MissingTableRepository());
        using var http = new HttpClient();

        var request = new MeasurementUploadRequest(
            new MeasurementUploadInformation(Guid.NewGuid(), "DH-001", "P", "C", "CORP\\op", null, null, "CORP\\up"),
            [Record(1, hdm: 1)]);
        using var response = await http.PostAsJsonAsync(backend.BaseUrl + "/api/measurements/upload", request);
        var detail = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("detail").GetString();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode, "status");
        Assert.Equal($"{UploadedMeasurementController.TablesMissingMessage} Nothing was stored.", detail, "detail");
        Assert.True(backend.Logs.Entries.Any(e => e.Level == LogLevel.Warning && e.Message.Contains("001_CreateSppCt007.sql")), "warning logged");
    }

    private sealed class MissingTableRepository : IMeasurementUploadRepository
    {
        public Task<MeasurementUploadResponse> SaveAsync(
            MeasurementUploadRequest request, string fingerprint, string? verifiedUploader, CancellationToken cancellationToken) =>
            throw new UploadTablesMissingException(new Exception("Could not find stored procedure"));
    }
}
