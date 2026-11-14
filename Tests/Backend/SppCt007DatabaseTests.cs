using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Backend.Controllers.Measurement;
using Backend.Models.Measurement;
using Backend.Repositories.Measurement;
using Microsoft.Extensions.Logging;

namespace Ct007.Tests.BackendTests;

/// <summary>BE-DB: the SPP CT007 table, its JSON insert procedure and permissions (no SQL Server needed).</summary>
public static class SppCt007DatabaseTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 13, 9, 0, 0, TimeSpan.FromHours(8));

    private static string Script() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Database", "Migrations", SppCt007Database.MigrationScript));

    private static MeasurementUploadRecord Record(int capture, double? hdm) =>
        new(capture, Start.AddMinutes(capture), Start.AddMinutes(capture).AddSeconds(5), 5, capture * 0.1m, capture * 0.1m + 0.1m,
            null, 120.5, 121, hdm, null, 0.12);

    [Test("BE-DB-01", "@RecordsJson: one object per row with the procedure's keys in order, row numbers from 1, exact times and numbers, null for missing values")]
    public static void RecordsJson()
    {
        var precise = new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.FromHours(8)).AddTicks(1234567);
        var json = SppCt007Database.ToRecordsJson([Record(7, hdm: 10.5) with { StartTime = precise, TsMs = 1_763_000_000_123 }, Record(3, hdm: null)]);
        using var document = JsonDocument.Parse(json);
        var rows = document.RootElement;

        Assert.Equal(JsonValueKind.Array, rows.ValueKind, "array");
        Assert.Equal(2, rows.GetArrayLength(), "rows");
        Assert.Sequence(SppCt007Database.RecordKeys, rows[0].EnumerateObject().Select(p => p.Name), "keys in order (case-sensitive)");
        Assert.Equal(7, rows[0].GetProperty("CAPTURE_NO").GetInt32(), "capture kept");
        Assert.Sequence(new[] { 1, 2 }, rows.EnumerateArray().Select(r => r.GetProperty("ROW_NO").GetInt32()), "row numbers in request order");
        Assert.Equal("2026-11-13T09:00:00.1234567+08:00", rows[0].GetProperty("START_TIME").GetString(), "time with all decimals and its offset");
        Assert.Equal(0.8m, rows[0].GetProperty("DEPTH_TO_M").GetDecimal(), "depth");
        Assert.Equal(1_763_000_000_123L, rows[0].GetProperty("TS_MS").GetInt64(), "device timestamp");
        Assert.Equal(10.5, rows[0].GetProperty("HDM").GetDouble(), "value kept");
        Assert.True(rows[1].GetProperty("HDM").ValueKind == JsonValueKind.Null && rows[1].GetProperty("TS_MS").ValueKind == JsonValueKind.Null,
            "missing values are null");
        Assert.Equal(0.1 + 0.2, JsonDocument.Parse(SppCt007Database.ToRecordsJson([Record(1, hdm: 0.1 + 0.2)])).RootElement[0].GetProperty("HDM").GetDouble(),
            "doubles round-trip exactly");
    }

    [Test("BE-DB-02", "The script creates the table and the insert procedure the backend calls, its OPENJSON keys match the JSON, and the app role gets EXECUTE and SELECT only")]
    public static void ScriptMatchesBackend()
    {
        var script = Script();

        foreach (var expected in new[]
        {
            $"CREATE TABLE {SppCt007Database.Table} (",
            $"CREATE OR ALTER PROCEDURE {SppCt007Database.UploadProcedure}",
            "@RecordsJson       NVARCHAR(MAX)",
            $"GRANT EXECUTE ON OBJECT::{SppCt007Database.UploadProcedure} TO {SppCt007Database.Role};",
            $"GRANT SELECT ON OBJECT::{SppCt007Database.Table} TO {SppCt007Database.Role};",
        })
        {
            Assert.Contains(expected, script, expected);
        }

        // Only comment lines may mention write rights; no statement grants them (rows are inserted by the procedure only).
        var statements = string.Join('\n', script.Split('\n').Where(line => !line.TrimStart().StartsWith("--", StringComparison.Ordinal)));
        Assert.False(Regex.IsMatch(statements, @"GRANT\s+(INSERT|UPDATE|DELETE|CONTROL|ALTER)", RegexOptions.IgnoreCase), "no direct write rights granted");
        Assert.False(Regex.IsMatch(statements, @"\b(UPDATE|DELETE)\s+(FROM\s+)?dbo\.SPPCT007", RegexOptions.IgnoreCase), "append-only: nothing updates or deletes rows");

        var openJson = Regex.Match(script, @"FROM OPENJSON\(@RecordsJson\)\s*WITH \((?<body>.*?)\n\s*\);", RegexOptions.Singleline);
        Assert.True(openJson.Success, "OPENJSON block found");
        var paths = Regex.Matches(openJson.Groups["body"].Value, @"'\$\.(?<key>[A-Z0-9_]+)'").Select(m => m.Groups["key"].Value).ToList();
        Assert.Sequence(SppCt007Database.RecordKeys, paths, "OPENJSON paths = JSON keys the backend writes, in order");
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
