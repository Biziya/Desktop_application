using Backend.Models.Measurement;
using Microsoft.Data.SqlClient;

namespace Backend.Repositories.Measurement;

/// <summary>Reads the SPP CT007 table (<see cref="SppCt007Database.Table"/>) with parameterized SELECTs. Never writes.</summary>
public sealed class UploadedMeasurementRepository(IConfiguration configuration) : IUploadedMeasurementRepository
{
    // A drillhole uploaded several times may have had its project or campaign corrected in between: the latest wins.
    private const string DrillholesQuery = $"""
        WITH totals AS (
            SELECT DRILLHOLE, COUNT(DISTINCT UPLOAD_ID) AS UPLOADS, COUNT(*) AS RECORDS, MAX(UPLOADED_AT) AS LAST_UPLOADED_AT
            FROM {SppCt007Database.Table}
            GROUP BY DRILLHOLE
        ), latest AS (
            SELECT DRILLHOLE, PROJECT, CAMPAIGN,
                   ROW_NUMBER() OVER (PARTITION BY DRILLHOLE ORDER BY UPLOADED_AT DESC, UPLOAD_ID DESC) AS N
            FROM {SppCt007Database.Table}
        )
        SELECT t.DRILLHOLE, l.PROJECT, l.CAMPAIGN, t.UPLOADS, t.RECORDS, t.LAST_UPLOADED_AT
        FROM totals t
        JOIN latest l ON l.DRILLHOLE = t.DRILLHOLE AND l.N = 1
        ORDER BY t.DRILLHOLE;
        """;

    private const string RecordsQuery = $"""
        SELECT TOP (@Limit)
               SOURCE_SESSION_ID, CAPTURE_NO, START_TIME, END_TIME, SAMPLE_COUNT,
               DEPTH_FROM_M, DEPTH_TO_M, TS_MS, COUNT0, COUNT0_RAW, HDM, HDM_RAW, DOSE_RATE,
               UPLOAD_ID, OPERATOR, UPLOADED_AT
        FROM {SppCt007Database.Table}
        WHERE DRILLHOLE = @Drillhole
        ORDER BY DEPTH_FROM_M, DEPTH_TO_M, UPLOADED_AT, CAPTURE_NO;
        """;

    private readonly string _connectionString = configuration.GetConnectionString("DefaultConnection") is { Length: > 0 } configured
        ? configured
        : throw new InvalidOperationException(
            "Connection string 'DefaultConnection' is not configured. Set the ConnectionStrings__DefaultConnection environment " +
            "variable, or run: dotnet user-secrets set ConnectionStrings:DefaultConnection \"...\" (Backend README).");

    public Task<IReadOnlyList<UploadedDrillholeResponse>> GetDrillholesAsync(CancellationToken cancellationToken) =>
        QueryAsync(DrillholesQuery, _ => { }, reader => new UploadedDrillholeResponse(
            HoleId: reader.GetString(0),
            ProjectCode: reader.IsDBNull(1) ? "" : reader.GetString(1),
            Campaign: reader.IsDBNull(2) ? "" : reader.GetString(2),
            UploadCount: reader.GetInt32(3),
            RecordCount: reader.GetInt32(4),
            LastUploadedAt: reader.GetDateTimeOffset(5)), cancellationToken);

    public Task<IReadOnlyList<UploadedRecordResponse>> GetRecordsAsync(string drillhole, int limit, CancellationToken cancellationToken) =>
        QueryAsync(RecordsQuery, parameters =>
        {
            parameters.Add("@Drillhole", System.Data.SqlDbType.NVarChar, 100).Value = drillhole;
            parameters.Add("@Limit", System.Data.SqlDbType.Int).Value = limit;
        }, reader => new UploadedRecordResponse(
            SessionId: reader.GetGuid(0),
            CaptureNumber: reader.GetInt32(1),
            StartTime: reader.GetDateTimeOffset(2),
            EndTime: reader.GetDateTimeOffset(3),
            SampleCount: reader.GetInt32(4),
            DepthFrom: reader.GetDecimal(5),
            DepthTo: reader.GetDecimal(6),
            TsMs: reader.IsDBNull(7) ? null : reader.GetInt64(7),
            Count0: NullableDouble(reader, 8),
            Count0Raw: NullableDouble(reader, 9),
            Hdm: NullableDouble(reader, 10),
            HdmRaw: NullableDouble(reader, 11),
            DoseRate: NullableDouble(reader, 12),
            UploadId: reader.GetInt64(13),
            Operator: reader.GetString(14),
            UploadedAt: reader.GetDateTimeOffset(15)), cancellationToken);

    private async Task<IReadOnlyList<T>> QueryAsync<T>(
        string sql, Action<SqlParameterCollection> bind, Func<SqlDataReader, T> map, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(sql, connection);
            bind(command.Parameters);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            var rows = new List<T>();
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(map(reader));
            }

            return rows;
        }
        catch (SqlException error) when (SppCt007Database.IsMissingObject(error))
        {
            throw new UploadTablesMissingException(error);
        }
    }

    private static double? NullableDouble(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);
}
