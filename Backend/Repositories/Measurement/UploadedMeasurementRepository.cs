using Backend.Models.Measurement;
using Microsoft.Data.SqlClient;

namespace Backend.Repositories.Measurement;

/// <summary>
/// Reads dbo.CT007_MEASUREMENT_UPLOAD and dbo.CT007_MEASUREMENT_UPLOAD_RECORD (Database/Migrations/001). SELECT only.
/// </summary>
public sealed class UploadedMeasurementRepository(IConfiguration configuration) : IUploadedMeasurementRepository
{
    // A drillhole uploaded several times may have had its project or campaign corrected in between: the latest wins.
    private const string DrillholesQuery = """
        SELECT u.DRILLHOLE,
               latest.PROJECT,
               latest.CAMPAIGN,
               COUNT(*) AS UPLOADS,
               SUM(u.RECORD_COUNT) AS RECORDS,
               MAX(u.UPLOADED_AT) AS LAST_UPLOADED_AT
        FROM dbo.CT007_MEASUREMENT_UPLOAD u
        CROSS APPLY (
            SELECT TOP (1) l.PROJECT, l.CAMPAIGN
            FROM dbo.CT007_MEASUREMENT_UPLOAD l
            WHERE l.DRILLHOLE = u.DRILLHOLE
            ORDER BY l.UPLOADED_AT DESC, l.UPLOAD_ID DESC
        ) latest
        GROUP BY u.DRILLHOLE, latest.PROJECT, latest.CAMPAIGN
        ORDER BY u.DRILLHOLE;
        """;

    private const string RecordsQuery = """
        SELECT TOP (@limit)
               r.SOURCE_SESSION_ID, r.CAPTURE_NO, r.START_TIME, r.END_TIME, r.SAMPLE_COUNT,
               r.DEPTH_FROM_M, r.DEPTH_TO_M, r.TS_MS, r.COUNT0, r.COUNT0_RAW, r.HDM, r.HDM_RAW, r.DOSE_RATE,
               u.UPLOAD_ID, u.OPERATOR, u.UPLOADED_AT
        FROM dbo.CT007_MEASUREMENT_UPLOAD_RECORD r
        JOIN dbo.CT007_MEASUREMENT_UPLOAD u ON u.UPLOAD_ID = r.UPLOAD_ID
        WHERE u.DRILLHOLE = @drillhole
        ORDER BY r.DEPTH_FROM_M, r.DEPTH_TO_M, u.UPLOADED_AT, r.CAPTURE_NO;
        """;

    // 208: invalid object name, i.e. the migration has not created the tables yet.
    private const int InvalidObjectName = 208;

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
            parameters.Add("@drillhole", System.Data.SqlDbType.NVarChar, 100).Value = drillhole;
            parameters.Add("@limit", System.Data.SqlDbType.Int).Value = limit;
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
        catch (SqlException error) when (error.Number == InvalidObjectName)
        {
            throw new UploadTablesMissingException(error);
        }
    }

    private static double? NullableDouble(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);
}
