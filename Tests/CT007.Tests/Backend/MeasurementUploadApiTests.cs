using System.Net.Http.Json;
using System.Text.Json;
using Backend.Models.Measurement;
using Backend.Repositories.Measurement;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Ct007.Tests.BackendTests;

/// <summary>BE-050…BE-056: POST /api/measurements/upload (validation, duplicates, identity, errors, SQL storage).</summary>
public static class MeasurementUploadApiTests
{
    private const string Path = "/api/measurements/upload";
    private static readonly Guid SessionId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly DateTimeOffset Start = new(2026, 10, 16, 9, 0, 0, TimeSpan.FromHours(8));

    private static MeasurementUploadRequest Request(int records = 2, Guid? session = null, string uploader = "CORP\\uploader") => new(
        new MeasurementUploadInformation(session ?? SessionId, "DH-001", "North Ridge", "2024", "CORP\\ann", "notes", "CORP\\creator", uploader),
        Enumerable.Range(1, records).Select(Record).ToList());

    private static MeasurementUploadRecord Record(int capture) => new(
        capture, Start.AddMinutes(capture).AddTicks(1234567), Start.AddMinutes(capture).AddSeconds(5).AddTicks(1234567), 5,
        10m + (capture - 1) * 0.25m, 10.25m + (capture - 1) * 0.25m, 1_760_600_000_000 + capture, 125.5 + capture, 126, 10.5, null, 0.125);

    private static async Task<(HttpStatusCode Status, string? MediaType, string Body)> Post(TestBackend backend, object? body)
    {
        using var http = new HttpClient();
        using var response = await http.PostAsJsonAsync(backend.BaseUrl + Path, body);
        return (response.StatusCode, response.Content.Headers.ContentType?.MediaType, await response.Content.ReadAsStringAsync());
    }

    private static string Detail(string body) => JsonDocument.Parse(body).RootElement.GetProperty("detail").GetString()!;

    [Test("BE-050", "Upload: a valid request is stored once and answered 201 with the upload result")]
    public static async Task SuccessfulUpload()
    {
        var repository = new MemoryUploadRepository();
        await using var backend = await TestBackend.StartAsync(new FakeDrillholeRepository(), uploads: repository);

        var (status, _, body) = await Post(backend, Request());

        Assert.Equal(HttpStatusCode.Created, status, "status");
        using var json = JsonDocument.Parse(body);
        Assert.Equal(1L, json.RootElement.GetProperty("uploadId").GetInt64(), "uploadId");
        Assert.Equal(2, json.RootElement.GetProperty("recordCount").GetInt32(), "recordCount");
        Assert.False(json.RootElement.GetProperty("alreadyUploaded").GetBoolean(), "alreadyUploaded");
        var (stored, fingerprint, verified) = repository.Uploads.Single();
        Assert.Equal(Request().Fingerprint(), fingerprint, "fingerprint computed by the server");
        Assert.Equal(Start.AddMinutes(1).AddTicks(1234567), stored.Records![0].StartTime, "timestamp received with full precision");
        Assert.Equal(Start.AddMinutes(1).AddTicks(1234567).Offset, stored.Records[0].StartTime!.Value.Offset, "timestamp offset kept");
        Assert.True(verified is null, "no authentication: no verified uploader");
        Assert.Equal("CORP\\uploader", stored.MeasurementInformation!.UploadingUsername, "declared uploader kept as declared");
        Assert.Equal("CORP\\creator", stored.MeasurementInformation.CreatedUsername, "created username kept");
    }

    [Test("BE-051", "Upload: invalid or incomplete requests are rejected with 400 and a reason; nothing is stored")]
    public static async Task InvalidRequests()
    {
        var repository = new MemoryUploadRepository();
        await using var backend = await TestBackend.StartAsync(new FakeDrillholeRepository(), uploads: repository);
        var valid = Request();
        var info = valid.MeasurementInformation!;
        var record = valid.Records![0];

        var cases = new (string Name, object? Body, string Detail)[]
        {
            ("no metadata", valid with { MeasurementInformation = null }, "measurementInformation is required."),
            ("no session", valid with { MeasurementInformation = info with { SessionId = null } }, "measurementInformation.sessionId is required."),
            ("no drillhole", valid with { MeasurementInformation = info with { Drillhole = " " } }, "Drillhole is required."),
            ("no operator", valid with { MeasurementInformation = info with { Operator = null } }, "Operator is required."),
            ("no uploader", valid with { MeasurementInformation = info with { UploadingUsername = "" } }, "Uploading Username is required."),
            ("long notes", valid with { MeasurementInformation = info with { Notes = new string('x', 4001) } }, "Notes is longer than 4000 characters."),
            ("no records", valid with { Records = [] }, "At least one measurement is required."),
            ("no capture", valid with { Records = [record with { CaptureNumber = null }] }, "Row 1: captureNumber must be greater than 0."),
            ("duplicate capture", valid with { Records = [record, record] }, "Row 2: capture 1 appears more than once."),
            ("no start", valid with { Records = [record with { StartTime = null }] }, "Row 1: startTime and endTime are required."),
            ("end before start", valid with { Records = [record with { EndTime = record.StartTime!.Value.AddSeconds(-1) }] }, "Row 1: endTime is before startTime."),
            ("future", valid with { Records = [record with { StartTime = DateTimeOffset.UtcNow.AddDays(3), EndTime = DateTimeOffset.UtcNow.AddDays(3) }] }, "not plausible"),
            ("no samples", valid with { Records = [record with { SampleCount = -1 }] }, "Row 1: sampleCount is required and cannot be negative."),
            ("no depth", valid with { Records = [record with { DepthTo = null }] }, "Row 1: depthFrom and depthTo are required."),
            ("depth order", valid with { Records = [record with { DepthTo = record.DepthFrom }] }, "Row 1: depthTo must be greater than depthFrom."),
            ("negative depth", valid with { Records = [record with { DepthFrom = -0.5m }] }, "Row 1: depths must be between 0 and 100000 m with at most 3 decimals."),
            ("sub-mm depth", valid with { Records = [record with { DepthTo = 10.2505m }] }, "Row 1: depths must be between 0 and 100000 m with at most 3 decimals."),
            ("negative ts", valid with { Records = [record with { TsMs = -1 }] }, "Row 1: tsMs cannot be negative.")
        };

        foreach (var (name, body, detail) in cases)
        {
            var (status, mediaType, text) = await Post(backend, body);
            Assert.Equal(HttpStatusCode.BadRequest, status, $"{name}: status");
            Assert.Equal("application/problem+json", mediaType, $"{name}: problem details");
            Assert.Contains(detail, Detail(text), $"{name}: reason");
        }

        using var http = new HttpClient();
        using var malformed = await http.PostAsync(backend.BaseUrl + Path, new StringContent("{\"records\": [ {\"depthFrom\": \"deep\"} ]}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode, "malformed JSON");
        using var empty = await http.PostAsync(backend.BaseUrl + Path, new StringContent("", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode, "empty body");

        Assert.Equal(0, repository.Calls, "repository never called");
    }

    [Test("BE-052", "Upload fingerprint: same data → same key (whoever uploads, any record order); any changed value → another key")]
    public static void Fingerprint()
    {
        var request = Request(3);
        Assert.Equal(64, request.Fingerprint().Length, "SHA-256 hex");
        Assert.Equal(request.Fingerprint(), Request(3, uploader: "CORP\\someone-else").Fingerprint(), "uploader is not part of the dataset");
        Assert.Equal(request.Fingerprint(), (request with { Records = request.Records!.Reverse().ToList() }).Fingerprint(), "record order");
        Assert.False(request.Fingerprint() == Request(2).Fingerprint(), "a record removed");
        Assert.False(request.Fingerprint() == (request with { Records = [.. request.Records!.Take(2), request.Records![2] with { Count0 = 1 }] }).Fingerprint(),
            "a value changed");
        Assert.False(request.Fingerprint() == (request with { MeasurementInformation = request.MeasurementInformation! with { Notes = "other" } }).Fingerprint(),
            "metadata changed");
        Assert.False(request.Fingerprint() == Request(3, Guid.NewGuid()).Fingerprint(), "another session");
    }

    [Test("BE-053", "Upload duplicates: a repeated request answers 200 alreadyUploaded; overlapping captures answer 409; nothing is inserted twice")]
    public static async Task Duplicates()
    {
        var repository = new MemoryUploadRepository();
        await using var backend = await TestBackend.StartAsync(new FakeDrillholeRepository(), uploads: repository);

        Assert.Equal(HttpStatusCode.Created, (await Post(backend, Request(2))).Status, "first upload");
        var (status, _, body) = await Post(backend, Request(2, uploader: "CORP\\retry"));
        Assert.Equal(HttpStatusCode.OK, status, "same dataset again (e.g. retry after a timeout)");
        Assert.True(JsonDocument.Parse(body).RootElement.GetProperty("alreadyUploaded").GetBoolean(), "alreadyUploaded");
        Assert.Equal(1L, JsonDocument.Parse(body).RootElement.GetProperty("uploadId").GetInt64(), "the earlier upload");

        // The same session exported later with one more capture: captures 1 and 2 are stored already.
        var (conflict, mediaType, text) = await Post(backend, Request(3));
        Assert.Equal(HttpStatusCode.Conflict, conflict, "overlapping dataset");
        Assert.Equal("application/problem+json", mediaType, "problem details");
        Assert.Equal("2 of these measurements were already uploaded in an earlier file (Capture No 1, 2). Remove them and upload the rest. Nothing was stored.",
            Detail(text), "which captures");

        Assert.Equal(1, repository.Uploads.Count, "one dataset stored");
        Assert.Equal(2, repository.StoredRecords, "two records stored");
        Assert.Equal(HttpStatusCode.Created, (await Post(backend, Request(3) with { Records = [Record(3)] })).Status, "the rest uploads");
    }

    [Test("BE-054", "Upload: an unreachable SQL Server gives 503 without connection details, and is logged")]
    public static async Task DatabaseUnavailable()
    {
        await using var backend = await TestBackend.StartAsync(new FakeDrillholeRepository(), settings: new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = TestBackend.RefusedSqlConnection
        });

        var (status, _, body) = await Post(backend, Request());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status, "status");
        Assert.Equal("The measurement database is unavailable. Nothing was stored; try again later.", Detail(body), "detail");

        foreach (var secret in new[] { "NotARealPassword-1", "ct007_test_user", "127.0.0.1,1", "SqlException", "   at " })
        {
            Assert.False(body.Contains(secret, StringComparison.Ordinal), $"body does not contain '{secret}'");
        }

        Assert.True(backend.Logs.Entries.Any(e => e.Level == LogLevel.Error && e.Message == "Failed to store a measurement upload in SQL Server."), "error logged");
    }

    [Test("BE-055", "Upload identity: names in the body are never taken as the authenticated uploader; a server identity is stored separately")]
    public static async Task UploaderIdentity()
    {
        var repository = new MemoryUploadRepository();
        await using (var anonymous = await TestBackend.StartAsync(new FakeDrillholeRepository(), uploads: repository))
        {
            await Post(anonymous, Request(1, uploader: "CORP\\administrator"));
        }

        Assert.True(repository.Uploads[0].VerifiedUploader is null, "unauthenticated request: the body's name is not a verified identity");

        await using (var authenticated = await TestBackend.StartAsync(new FakeDrillholeRepository(), uploads: repository, authenticatedUser: "CORP\\real.user"))
        {
            await Post(authenticated, Request(1, Guid.NewGuid(), uploader: "CORP\\administrator"));
        }

        Assert.Equal("CORP\\real.user", repository.Uploads[1].VerifiedUploader, "server-authenticated identity");
        Assert.Equal("CORP\\administrator", repository.Uploads[1].Request.MeasurementInformation!.UploadingUsername, "declared name kept as declared");
    }

    [Test("BE-056", "Upload SQL storage: session and records in one transaction, exact timestamps, no duplicates on retry, rollback on failure",
        Category = "Sql")]
    public static async Task SqlStorage()
    {
        var connectionString = Environment.GetEnvironmentVariable("CT007_TEST_SQL");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Skip("Set CT007_TEST_SQL to a disposable local SQL Server database named CT007_Test* (e.g. LocalDB).");
            return;
        }

        var builder = new SqlConnectionStringBuilder(connectionString);
        var server = builder.DataSource.ToLowerInvariant();
        Assert.True(server.Contains("(localdb)") || server.StartsWith("localhost", StringComparison.Ordinal) || server.StartsWith("127.0.0.1", StringComparison.Ordinal)
            || server.StartsWith(".", StringComparison.Ordinal) || server.StartsWith("tcp:localhost", StringComparison.Ordinal),
            $"refusing a non-local SQL Server ({builder.DataSource})");
        Assert.True(builder.InitialCatalog.StartsWith("CT007_Test", StringComparison.OrdinalIgnoreCase), "refusing a database not named CT007_Test*");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        async Task<object?> Sql(string text)
        {
            await using var command = new SqlCommand(text, connection);
            return await command.ExecuteScalarAsync();
        }

        const string Drop = """
            IF OBJECT_ID('dbo.CT007_MEASUREMENT_UPLOAD_RECORD') IS NOT NULL DROP TABLE dbo.CT007_MEASUREMENT_UPLOAD_RECORD;
            IF OBJECT_ID('dbo.CT007_MEASUREMENT_UPLOAD') IS NOT NULL DROP TABLE dbo.CT007_MEASUREMENT_UPLOAD;
            """;
        await Sql(Drop);
        var migration = File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "Database", "Migrations", "001_CreateMeasurementUploadTables.sql"));
        await Sql(migration);
        await Sql(migration); // safe to run again

        try
        {
            var repository = new MeasurementUploadRepository(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = connectionString }).Build());
            var request = Request(3);

            var first = await repository.SaveAsync(request, request.Fingerprint(), "CORP\\verified", CancellationToken.None);
            Assert.False(first.AlreadyUploaded, "stored");
            Assert.Equal(3, first.RecordCount, "record count");
            Assert.Equal(3, (int)(await Sql($"SELECT COUNT(*) FROM dbo.CT007_MEASUREMENT_UPLOAD_RECORD WHERE UPLOAD_ID = {first.UploadId}"))!,
                "records linked to their session");
            Assert.Equal(Start.AddMinutes(2).AddTicks(1234567),
                (DateTimeOffset)(await Sql($"SELECT START_TIME FROM dbo.CT007_MEASUREMENT_UPLOAD_RECORD WHERE UPLOAD_ID = {first.UploadId} AND CAPTURE_NO = 2"))!,
                "start time stored exactly (ticks and offset)");
            Assert.Equal(1_760_600_000_002L, (long)(await Sql($"SELECT TS_MS FROM dbo.CT007_MEASUREMENT_UPLOAD_RECORD WHERE UPLOAD_ID = {first.UploadId} AND CAPTURE_NO = 2"))!,
                "device timestamp stored");
            Assert.Equal(25.0m, (decimal)(await Sql($"SELECT STEP_CM FROM dbo.CT007_MEASUREMENT_UPLOAD_RECORD WHERE UPLOAD_ID = {first.UploadId} AND CAPTURE_NO = 1"))!,
                "step in cm from the interval");
            Assert.Equal("CORP\\creator|CORP\\uploader|CORP\\verified",
                (string)(await Sql($"SELECT CREATED_USERNAME + '|' + UPLOADING_USERNAME + '|' + UPLOADED_BY_VERIFIED FROM dbo.CT007_MEASUREMENT_UPLOAD WHERE UPLOAD_ID = {first.UploadId}"))!,
                "user names");

            // A retry (e.g. after a client timeout) finds the stored dataset.
            var retry = await repository.SaveAsync(request, request.Fingerprint(), null, CancellationToken.None);
            Assert.True(retry.AlreadyUploaded && retry.UploadId == first.UploadId, "retry: the earlier upload");
            Assert.Equal(1, (int)(await Sql("SELECT COUNT(*) FROM dbo.CT007_MEASUREMENT_UPLOAD"))!, "one session");
            Assert.Equal(3, (int)(await Sql("SELECT COUNT(*) FROM dbo.CT007_MEASUREMENT_UPLOAD_RECORD"))!, "no duplicate records");

            // Overlapping captures of the same desktop session.
            var overlapping = Request(4);
            var conflict = await Assert.ThrowsAsync<MeasurementUploadConflictException>(
                () => repository.SaveAsync(overlapping, overlapping.Fingerprint(), null, CancellationToken.None), "overlap");
            Assert.Sequence(new[] { 1, 2, 3 }, conflict.CaptureNumbers, "conflicting captures");

            // A failure while inserting records rolls back the session row too.
            await Sql("ALTER TABLE dbo.CT007_MEASUREMENT_UPLOAD_RECORD ADD CONSTRAINT CK_TEST_FAIL CHECK (CAPTURE_NO <> 3)");
            var failing = Request(3, Guid.NewGuid());
            await Assert.ThrowsAsync<SqlException>(() => repository.SaveAsync(failing, failing.Fingerprint(), null, CancellationToken.None), "third record fails");
            Assert.Equal(1, (int)(await Sql("SELECT COUNT(*) FROM dbo.CT007_MEASUREMENT_UPLOAD"))!, "rolled back: no session row");
            Assert.Equal(3, (int)(await Sql("SELECT COUNT(*) FROM dbo.CT007_MEASUREMENT_UPLOAD_RECORD"))!, "rolled back: no partial records");
        }
        finally
        {
            await Sql(Drop);
        }
    }
}

/// <summary>
/// In-memory upload storage with the real repository's duplicate rules: same fingerprint → the earlier upload; a capture
/// of the same session stored before → conflict. Thread-safe (requests are served on the thread pool).
/// </summary>
public sealed class MemoryUploadRepository : IMeasurementUploadRepository
{
    private readonly object gate = new();
    private readonly List<(MeasurementUploadRequest Request, string Fingerprint, string? VerifiedUploader)> uploads = [];

    public int Calls;

    /// <summary>Delay of every save (a slow database); the save still completes after the client gave up.</summary>
    public TimeSpan Delay { get; set; }

    public IReadOnlyList<(MeasurementUploadRequest Request, string Fingerprint, string? VerifiedUploader)> Uploads { get { lock (gate) return uploads.ToList(); } }

    public int StoredRecords { get { lock (gate) return uploads.Sum(u => u.Request.Records!.Count); } }

    public async Task<MeasurementUploadResponse> SaveAsync(
        MeasurementUploadRequest request, string fingerprint, string? verifiedUploader, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);

        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, CancellationToken.None);
        }

        lock (gate)
        {
            var index = uploads.FindIndex(u => u.Fingerprint == fingerprint);

            if (index >= 0)
            {
                return new MeasurementUploadResponse(index + 1, uploads[index].Request.Records!.Count, true, DateTimeOffset.UnixEpoch);
            }

            var session = request.MeasurementInformation!.SessionId;
            var stored = uploads.Where(u => u.Request.MeasurementInformation!.SessionId == session)
                .SelectMany(u => u.Request.Records!.Select(r => r.CaptureNumber!.Value)).ToHashSet();

            if (request.Records!.Select(r => r.CaptureNumber!.Value).Where(stored.Contains).Order().ToList() is { Count: > 0 } conflicts)
            {
                throw new MeasurementUploadConflictException(conflicts);
            }

            uploads.Add((request, fingerprint, verifiedUploader));
            return new MeasurementUploadResponse(uploads.Count, request.Records!.Count, false, DateTimeOffset.UnixEpoch);
        }
    }
}
