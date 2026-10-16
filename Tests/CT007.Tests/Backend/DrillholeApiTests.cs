using System.Text.Json;
using Backend.Repositories.CoreInformation.Drillhole;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using BackendDrillhole = Backend.Models.CoreInformation.Drillhole.DrillholeResponse;

namespace Ct007.Tests.BackendTests;

/// <summary>BE-001, BE-002, BE-005, BE-006, BE-008, BE-011: the ASP.NET Core drillhole API (F01), hosted in-process.</summary>
public static class DrillholeApiTests
{
    [Test("BE-001", "GET drillholes returns the camelCase contract")]
    public static async Task DrillholesContract()
    {
        var repository = new FakeDrillholeRepository { Rows = [new BackendDrillhole("DH-1", "P1", "2024"), new BackendDrillhole("DH-2", "", "")] };
        await using var backend = await TestBackend.StartAsync(repository);
        using var http = new HttpClient();

        var response = await http.GetAsync(backend.BaseUrl + "/api/core-information/drillholes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode, "status");
        Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType?.ToString(), "content type");
        Assert.Equal("""[{"holeId":"DH-1","projectCode":"P1","campaign":"2024"},{"holeId":"DH-2","projectCode":"","campaign":""}]""",
            await response.Content.ReadAsStringAsync(), "body");

        repository.Rows = [];
        Assert.Equal("[]", await http.GetStringAsync(backend.BaseUrl + "/api/core-information/drillholes"), "empty table");
    }

    [Test("BE-002", "An unreachable SQL Server gives 503 ProblemDetails without connection details, and is logged")]
    public static async Task SqlFailureIs503()
    {
        await using var backend = await TestBackend.StartAsync(settings: new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = TestBackend.RefusedSqlConnection
        });
        using var http = new HttpClient();

        var response = await http.GetAsync(backend.BaseUrl + "/api/core-information/drillholes");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode, "status");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType, "content type");
        Assert.Contains("The drillhole database is unavailable.", body, "problem detail");

        foreach (var secret in new[] { "NotARealPassword-1", "ct007_test_user", "127.0.0.1,1", "SqlException", "   at " })
        {
            Assert.False(body.Contains(secret, StringComparison.Ordinal), $"body does not contain '{secret}'");
        }

        Assert.True(backend.Logs.Entries.Any(e => e.Level == LogLevel.Error && e.Category.EndsWith("DrillholeController", StringComparison.Ordinal)
            && e.Message == "Failed to read drillholes from SQL Server."), "error logged");
    }

    [Test("BE-005", "An aborted request cancels the repository call; it is not reported as a database outage")]
    public static async Task RequestCancellation()
    {
        var repository = new FakeDrillholeRepository { Delay = TimeSpan.FromMinutes(5) };
        await using var backend = await TestBackend.StartAsync(repository);
        using var http = new HttpClient();
        using var abort = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAsync<OperationCanceledException>(() => http.GetAsync(backend.BaseUrl + "/api/core-information/drillholes", abort.Token),
            "client gave up");

        await repository.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(200);
        Assert.False(backend.Logs.Entries.Any(e => e.Message == "Failed to read drillholes from SQL Server."), "no database error logged");
    }

    [Test("BE-006", "SQL projection against a disposable SQL Server: NULL → \"\", campaign = RIGHT(PROSPECT, 4) (UR-08)", Category = "Sql")]
    public static async Task SqlProjection()
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

        async Task Sql(string text)
        {
            await using var command = new SqlCommand(text, connection);
            await command.ExecuteNonQueryAsync();
        }

        await Sql("""
            IF OBJECT_ID('dbo.ZOOVCH_COLLAR_LIST') IS NOT NULL DROP TABLE dbo.ZOOVCH_COLLAR_LIST;
            CREATE TABLE dbo.ZOOVCH_COLLAR_LIST (HOLEID NVARCHAR(100) NULL, PROJECTCODE NVARCHAR(100) NULL, PROSPECT NVARCHAR(100) NULL);
            INSERT INTO dbo.ZOOVCH_COLLAR_LIST VALUES
                (N'DH-1', N'P1', N'ZOV-2024'), (N'DH-2', NULL, NULL), (N'DH-3', N'P3', N'24'),
                (N'DH-4', N'P4', N'ABC-2024 '), (N'ДН-5', N'П5', N'Кампания'), (NULL, N'P6', N'X');
            """);

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = connectionString })
                .Build();
            var rows = await new DrillholeRepository(configuration).GetAllAsync(CancellationToken.None);
            BackendDrillhole Get(string id) => rows.Single(r => r.HoleId == id);

            Assert.Equal(6, rows.Count, "every row returned (no filter)");
            Assert.Equal(new BackendDrillhole("DH-1", "P1", "2024"), Get("DH-1"), "campaign = last 4 characters");
            Assert.Equal(new BackendDrillhole("DH-2", "", ""), Get("DH-2"), "NULL → empty string");
            Assert.Equal("24", Get("DH-3").Campaign, "shorter PROSPECT returned whole");
            Assert.Equal(new BackendDrillhole("ДН-5", "П5", "ания"), Get("ДН-5"), "Unicode preserved");
            Assert.Equal("P6", Get("").ProjectCode, "NULL HOLEID → empty ID");
        }
        finally
        {
            await Sql("DROP TABLE dbo.ZOOVCH_COLLAR_LIST;");
        }
    }

    [Test("BE-008", "The OpenAPI document lists the three Core Information GET endpoints and the drillhole schema")]
    public static async Task OpenApiDocument()
    {
        await using var backend = await TestBackend.StartAsync(new FakeDrillholeRepository());
        using var http = new HttpClient();

        using var document = JsonDocument.Parse(await http.GetStringAsync(backend.BaseUrl + "/swagger/v1/swagger.json"));
        var paths = document.RootElement.GetProperty("paths");

        Assert.Sequence(new[] { "/api/core-information/bluetooth-device", "/api/core-information/drillholes", "/api/core-information/sqlite-database" },
            paths.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal), "paths");

        foreach (var path in paths.EnumerateObject())
        {
            Assert.Sequence(new[] { "get" }, path.Value.EnumerateObject().Select(o => o.Name), $"{path.Name}: GET only");
            Assert.Sequence(new[] { "Core Information" }, path.Value.GetProperty("get").GetProperty("tags").EnumerateArray().Select(t => t.GetString()),
                $"{path.Name}: tag");
        }

        var schema = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("DrillholeResponse").GetProperty("properties");
        Assert.Sequence(new[] { "campaign", "holeId", "projectCode" }, schema.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal),
            "DrillholeResponse properties");
    }

    [Test("BE-011", "Routing contract: HEAD / answers 404 (reachability), wrong methods 405, unknown paths 404 (UR-13)")]
    public static async Task RoutingContract()
    {
        await using var backend = await TestBackend.StartAsync(new FakeDrillholeRepository());
        using var http = new HttpClient();

        using var head = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, backend.BaseUrl + "/"));
        Assert.Equal(HttpStatusCode.NotFound, head.StatusCode, "HEAD /");

        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete })
        {
            using var response = await http.SendAsync(new HttpRequestMessage(method, backend.BaseUrl + "/api/core-information/drillholes"));
            Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode, $"{method} drillholes");
        }

        using var unknown = await http.GetAsync(backend.BaseUrl + "/api/core-information/unknown");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode, "unknown path");
    }
}
