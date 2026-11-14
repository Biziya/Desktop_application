using Backend.Controllers.Measurement;
using Backend.Models.Measurement;
using Backend.Repositories.Measurement;
using Microsoft.Extensions.Logging;

namespace Ct007.Tests.BackendTests;

/// <summary>BE-DV: the read-only Data Viewer API over the upload tables, hosted in-process with a fake repository.</summary>
public static class UploadedMeasurementApiTests
{
    private static readonly DateTimeOffset Uploaded = new(2026, 11, 10, 9, 30, 0, TimeSpan.FromHours(8));

    [Test("BE-DV-01", "Data Viewer API returns drillholes and records in the camelCase contract; drillhole is required")]
    public static async Task Contract()
    {
        var repository = new FakeUploadedRepository
        {
            Drillholes = [new UploadedDrillholeResponse("DH-001", "Project A", "2026", 2, 30, Uploaded)],
            Records =
            [
                new UploadedRecordResponse(Guid.Parse("8c4e0000-0000-0000-0000-000000000001"), 1, Uploaded.AddHours(-1), Uploaded.AddHours(-1).AddSeconds(5),
                    5, 0.000m, 0.100m, 1792115769651, 125.5, 126, null, null, 0.12, 7, "DOMAIN\\op", Uploaded)
            ]
        };
        await using var backend = await TestBackend.StartAsync(new FakeDrillholeRepository(), uploaded: repository);
        using var http = new HttpClient();

        var drillholes = await http.GetStringAsync(backend.BaseUrl + "/api/measurements/uploaded/drillholes");
        Assert.Contains("\"holeId\":\"DH-001\"", drillholes, "hole id");
        Assert.Contains("\"projectCode\":\"Project A\"", drillholes, "project");
        Assert.Contains("\"uploadCount\":2", drillholes, "upload count");

        var records = await http.GetStringAsync(backend.BaseUrl + "/api/measurements/uploaded/records?drillhole=%20DH-001%20");
        Assert.Contains("\"captureNumber\":1", records, "capture number");
        Assert.Contains("\"depthTo\":0.100", records, "depth keeps its decimals");
        Assert.Contains("\"hdm\":null", records, "missing device value stays null");
        Assert.Equal("DH-001", repository.LastDrillhole, "drillhole trimmed");
        Assert.Equal(UploadedMeasurementController.MaxRecords, repository.LastLimit, "row cap");

        var missing = await http.GetAsync(backend.BaseUrl + "/api/measurements/uploaded/records");
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode, "drillhole required");
        var tooLong = await http.GetAsync(backend.BaseUrl + "/api/measurements/uploaded/records?drillhole=" + new string('x', 101));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode, "drillhole too long");
    }

    [Test("BE-DV-02", "Missing upload tables give 503 saying so; an unreachable SQL Server gives the generic 503 without details")]
    public static async Task Unavailable()
    {
        var repository = new FakeUploadedRepository { Throw = new UploadTablesMissingException(new Exception("Invalid object name")) };
        await using (var backend = await TestBackend.StartAsync(new FakeDrillholeRepository(), uploaded: repository))
        {
            using var http = new HttpClient();
            var response = await http.GetAsync(backend.BaseUrl + "/api/measurements/uploaded/drillholes");
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode, "status");
            Assert.Contains(UploadedMeasurementController.TablesMissingMessage, body, "table missing text");
            Assert.False(body.Contains("Invalid object name", StringComparison.Ordinal), "no SQL details");
            Assert.True(backend.Logs.Entries.Any(e => e.Level == LogLevel.Warning && e.Category.EndsWith("UploadedMeasurementController", StringComparison.Ordinal)),
                "warning logged");
        }

        await using (var backend = await TestBackend.StartAsync(new FakeDrillholeRepository(), settings: new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = TestBackend.RefusedSqlConnection
        }))
        {
            using var http = new HttpClient();
            var response = await http.GetAsync(backend.BaseUrl + "/api/measurements/uploaded/records?drillhole=DH-001");
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode, "status");
            Assert.Contains(UploadedMeasurementController.UnavailableMessage, body, "generic text");

            foreach (var secret in new[] { "NotARealPassword-1", "ct007_test_user", "SqlException", "   at " })
            {
                Assert.False(body.Contains(secret, StringComparison.Ordinal), $"body does not contain '{secret}'");
            }
        }
    }

    private sealed class FakeUploadedRepository : IUploadedMeasurementRepository
    {
        public IReadOnlyList<UploadedDrillholeResponse> Drillholes { get; init; } = [];

        public IReadOnlyList<UploadedRecordResponse> Records { get; init; } = [];

        public Exception? Throw { get; init; }

        public string? LastDrillhole { get; private set; }

        public int LastLimit { get; private set; }

        public Task<IReadOnlyList<UploadedDrillholeResponse>> GetDrillholesAsync(CancellationToken cancellationToken) =>
            Throw is { } error ? Task.FromException<IReadOnlyList<UploadedDrillholeResponse>>(error) : Task.FromResult(Drillholes);

        public Task<IReadOnlyList<UploadedRecordResponse>> GetRecordsAsync(string drillhole, int limit, CancellationToken cancellationToken)
        {
            LastDrillhole = drillhole;
            LastLimit = limit;
            return Throw is { } error ? Task.FromException<IReadOnlyList<UploadedRecordResponse>>(error) : Task.FromResult(Records);
        }
    }
}
