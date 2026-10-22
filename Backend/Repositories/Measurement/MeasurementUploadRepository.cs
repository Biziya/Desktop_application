using System.Data;
using Backend.Models.Measurement;
using Microsoft.Data.SqlClient;

namespace Backend.Repositories.Measurement;

/// <summary>
/// Writes uploads to dbo.CT007_MEASUREMENT_UPLOAD and dbo.CT007_MEASUREMENT_UPLOAD_RECORD
/// (Database/Migrations/001_CreateMeasurementUploadTables.sql). Parameterized SQL only; one transaction per upload.
/// </summary>
public sealed class MeasurementUploadRepository(IConfiguration configuration) : IMeasurementUploadRepository
{
    // UPDLOCK + HOLDLOCK: a concurrent upload of the same dataset (a client retry while the first attempt is still
    // running) waits here until the first one commits, then finds it instead of inserting it a second time.
    private const string FindUploadQuery = """
        SELECT UPLOAD_ID, RECORD_COUNT, UPLOADED_AT
        FROM dbo.CT007_MEASUREMENT_UPLOAD WITH (UPDLOCK, HOLDLOCK)
        WHERE DATASET_HASH = @hash;
        """;

    private const string StoredCapturesQuery = """
        SELECT CAPTURE_NO
        FROM dbo.CT007_MEASUREMENT_UPLOAD_RECORD WITH (UPDLOCK, HOLDLOCK)
        WHERE SOURCE_SESSION_ID = @session;
        """;

    private const string InsertUploadQuery = """
        INSERT INTO dbo.CT007_MEASUREMENT_UPLOAD
            (DATASET_HASH, SOURCE_SESSION_ID, DRILLHOLE, PROJECT, CAMPAIGN, OPERATOR, NOTES,
             CREATED_USERNAME, UPLOADING_USERNAME, UPLOADED_BY_VERIFIED, RECORD_COUNT)
        OUTPUT INSERTED.UPLOAD_ID, INSERTED.UPLOADED_AT
        VALUES (@hash, @session, @drillhole, @project, @campaign, @operator, @notes,
                @createdUsername, @uploadingUsername, @verifiedUploader, @recordCount);
        """;

    private const string InsertRecordQuery = """
        INSERT INTO dbo.CT007_MEASUREMENT_UPLOAD_RECORD
            (UPLOAD_ID, SOURCE_SESSION_ID, CAPTURE_NO, ROW_NO, START_TIME, END_TIME, SAMPLE_COUNT,
             DEPTH_FROM_M, DEPTH_TO_M, TS_MS, COUNT0, COUNT0_RAW, HDM, HDM_RAW, DOSE_RATE)
        VALUES (@upload, @session, @capture, @row, @start, @end, @samples,
                @from, @to, @tsMs, @count0, @count0Raw, @hdm, @hdmRaw, @doseRate);
        """;

    // Unique key violations (2627 constraint, 2601 index): a concurrent upload stored the same captures first.
    private static readonly int[] UniqueViolations = [2627, 2601];

    private readonly string _connectionString = configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

    public async Task<MeasurementUploadResponse> SaveAsync(
        MeasurementUploadRequest request, string fingerprint, string? verifiedUploader, CancellationToken cancellationToken)
    {
        var info = request.MeasurementInformation!;
        var records = request.Records!;
        var session = info.SessionId!.Value;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        try
        {
            await using (var find = Command(FindUploadQuery, connection, transaction))
            {
                find.Parameters.Add("@hash", SqlDbType.Char, 64).Value = fingerprint;
                await using var reader = await find.ExecuteReaderAsync(cancellationToken);

                if (await reader.ReadAsync(cancellationToken))
                {
                    var existing = new MeasurementUploadResponse(reader.GetInt64(0), reader.GetInt32(1), AlreadyUploaded: true, reader.GetDateTimeOffset(2));
                    await reader.CloseAsync();
                    await transaction.CommitAsync(cancellationToken);
                    return existing;
                }
            }

            var uploaded = new HashSet<int>();

            await using (var stored = Command(StoredCapturesQuery, connection, transaction))
            {
                stored.Parameters.Add("@session", SqlDbType.UniqueIdentifier).Value = session;
                await using var reader = await stored.ExecuteReaderAsync(cancellationToken);

                while (await reader.ReadAsync(cancellationToken))
                {
                    uploaded.Add(reader.GetInt32(0));
                }
            }

            if (records.Select(r => r.CaptureNumber!.Value).Where(uploaded.Contains).Order().ToList() is { Count: > 0 } conflicts)
            {
                throw new MeasurementUploadConflictException(conflicts);
            }

            long uploadId;
            DateTimeOffset uploadedAt;

            await using (var insert = Command(InsertUploadQuery, connection, transaction))
            {
                insert.Parameters.Add("@hash", SqlDbType.Char, 64).Value = fingerprint;
                insert.Parameters.Add("@session", SqlDbType.UniqueIdentifier).Value = session;
                insert.Parameters.Add("@drillhole", SqlDbType.NVarChar, MeasurementUploadRequest.MaxCodeLength).Value = info.Drillhole!.Trim();
                insert.Parameters.Add("@project", SqlDbType.NVarChar, MeasurementUploadRequest.MaxCodeLength).Value = Nullable(info.Project);
                insert.Parameters.Add("@campaign", SqlDbType.NVarChar, MeasurementUploadRequest.MaxCodeLength).Value = Nullable(info.Campaign);
                insert.Parameters.Add("@operator", SqlDbType.NVarChar, MeasurementUploadRequest.MaxNameLength).Value = info.Operator!.Trim();
                insert.Parameters.Add("@notes", SqlDbType.NVarChar, MeasurementUploadRequest.MaxNotesLength).Value = Nullable(info.Notes);
                insert.Parameters.Add("@createdUsername", SqlDbType.NVarChar, MeasurementUploadRequest.MaxNameLength).Value = Nullable(info.CreatedUsername);
                insert.Parameters.Add("@uploadingUsername", SqlDbType.NVarChar, MeasurementUploadRequest.MaxNameLength).Value = info.UploadingUsername!.Trim();
                insert.Parameters.Add("@verifiedUploader", SqlDbType.NVarChar, MeasurementUploadRequest.MaxNameLength).Value = Nullable(verifiedUploader);
                insert.Parameters.Add("@recordCount", SqlDbType.Int).Value = records.Count;
                await using var reader = await insert.ExecuteReaderAsync(cancellationToken);
                await reader.ReadAsync(cancellationToken);
                uploadId = reader.GetInt64(0);
                uploadedAt = reader.GetDateTimeOffset(1);
            }

            await using (var insert = Command(InsertRecordQuery, connection, transaction))
            {
                var parameters = insert.Parameters;
                parameters.Add("@upload", SqlDbType.BigInt).Value = uploadId;
                parameters.Add("@session", SqlDbType.UniqueIdentifier).Value = session;
                var capture = parameters.Add("@capture", SqlDbType.Int);
                var row = parameters.Add("@row", SqlDbType.Int);
                var start = parameters.Add("@start", SqlDbType.DateTimeOffset);
                var end = parameters.Add("@end", SqlDbType.DateTimeOffset);
                var samples = parameters.Add("@samples", SqlDbType.Int);
                var from = Decimal(parameters.Add("@from", SqlDbType.Decimal));
                var to = Decimal(parameters.Add("@to", SqlDbType.Decimal));
                var tsMs = parameters.Add("@tsMs", SqlDbType.BigInt);
                var count0 = parameters.Add("@count0", SqlDbType.Float);
                var count0Raw = parameters.Add("@count0Raw", SqlDbType.Float);
                var hdm = parameters.Add("@hdm", SqlDbType.Float);
                var hdmRaw = parameters.Add("@hdmRaw", SqlDbType.Float);
                var doseRate = parameters.Add("@doseRate", SqlDbType.Float);

                for (var i = 0; i < records.Count; i++)
                {
                    var record = records[i];
                    capture.Value = record.CaptureNumber!.Value;
                    row.Value = i + 1;
                    start.Value = record.StartTime!.Value;
                    end.Value = record.EndTime!.Value;
                    samples.Value = record.SampleCount!.Value;
                    from.Value = record.DepthFrom!.Value;
                    to.Value = record.DepthTo!.Value;
                    tsMs.Value = (object?)record.TsMs ?? DBNull.Value;
                    count0.Value = (object?)record.Count0 ?? DBNull.Value;
                    count0Raw.Value = (object?)record.Count0Raw ?? DBNull.Value;
                    hdm.Value = (object?)record.Hdm ?? DBNull.Value;
                    hdmRaw.Value = (object?)record.HdmRaw ?? DBNull.Value;
                    doseRate.Value = (object?)record.DoseRate ?? DBNull.Value;
                    await insert.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            await transaction.CommitAsync(cancellationToken);
            return new MeasurementUploadResponse(uploadId, records.Count, AlreadyUploaded: false, uploadedAt);
        }
        catch (Exception error)
        {
            // Nothing of a failed upload stays: no session without records, no partial records.
            await RollbackAsync(transaction);

            if (error is SqlException sql && sql.Errors.Cast<SqlError>().Any(e => UniqueViolations.Contains(e.Number)))
            {
                throw new MeasurementUploadConflictException([]);
            }

            throw;
        }
    }

    private static SqlCommand Command(string text, SqlConnection connection, SqlTransaction transaction) => new(text, connection, transaction);

    private static SqlParameter Decimal(SqlParameter parameter)
    {
        parameter.Precision = 10;
        parameter.Scale = 3;
        return parameter;
    }

    private static object Nullable(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

    private static async Task RollbackAsync(SqlTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch (Exception error) when (error is InvalidOperationException or SqlException)
        {
            // Already completed, or the connection broke (SQL Server then rolls the transaction back itself).
        }
    }
}
