using System.Data;
using Backend.Models.Measurement;
using Microsoft.Data.SqlClient;

namespace Backend.Repositories.Measurement;

/// <summary>
/// Stores uploads in the SPP CT007 table through <see cref="SppCt007Database.UploadProcedure"/>: one call per upload,
/// with every row in a table-valued parameter. The procedure checks for duplicates and inserts in one transaction.
/// </summary>
public sealed class MeasurementUploadRepository(IConfiguration configuration) : IMeasurementUploadRepository
{
    // Unique key violations (2627 constraint, 2601 index): a concurrent upload stored the same captures first.
    private static readonly int[] UniqueViolations = [2627, 2601];

    private readonly string _connectionString = configuration.GetConnectionString("DefaultConnection") is { Length: > 0 } configured
        ? configured
        : throw new InvalidOperationException(
            "Connection string 'DefaultConnection' is not configured. Set the ConnectionStrings__DefaultConnection environment " +
            "variable, or run: dotnet user-secrets set ConnectionStrings:DefaultConnection \"...\" (Backend README).");

    public async Task<MeasurementUploadResponse> SaveAsync(
        MeasurementUploadRequest request, string fingerprint, string? verifiedUploader, CancellationToken cancellationToken)
    {
        var info = request.MeasurementInformation!;

        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(SppCt007Database.UploadProcedure, connection) { CommandType = CommandType.StoredProcedure };

        var parameters = command.Parameters;
        parameters.Add("@Hash", SqlDbType.Char, 64).Value = fingerprint;
        parameters.Add("@SessionId", SqlDbType.UniqueIdentifier).Value = info.SessionId!.Value;
        parameters.Add("@Drillhole", SqlDbType.NVarChar, MeasurementUploadRequest.MaxCodeLength).Value = info.Drillhole!.Trim();
        parameters.Add("@Project", SqlDbType.NVarChar, MeasurementUploadRequest.MaxCodeLength).Value = Nullable(info.Project);
        parameters.Add("@Campaign", SqlDbType.NVarChar, MeasurementUploadRequest.MaxCodeLength).Value = Nullable(info.Campaign);
        parameters.Add("@Operator", SqlDbType.NVarChar, MeasurementUploadRequest.MaxNameLength).Value = info.Operator!.Trim();
        parameters.Add("@Notes", SqlDbType.NVarChar, MeasurementUploadRequest.MaxNotesLength).Value = Nullable(info.Notes);
        parameters.Add("@CreatedUsername", SqlDbType.NVarChar, MeasurementUploadRequest.MaxNameLength).Value = Nullable(info.CreatedUsername);
        parameters.Add("@UploadingUsername", SqlDbType.NVarChar, MeasurementUploadRequest.MaxNameLength).Value = info.UploadingUsername!.Trim();
        parameters.Add("@VerifiedUploader", SqlDbType.NVarChar, MeasurementUploadRequest.MaxNameLength).Value = Nullable(verifiedUploader);

        var records = parameters.Add("@Records", SqlDbType.Structured);
        records.TypeName = SppCt007Database.RecordType;
        records.Value = SppCt007Database.ToRecordTable(request.Records!);

        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException($"{SppCt007Database.UploadProcedure} returned no result.");
            }

            var status = reader.GetString(0);

            switch (status)
            {
                case "Stored":
                case "AlreadyUploaded":
                    return new MeasurementUploadResponse(reader.GetInt64(1), reader.GetInt32(2), status == "AlreadyUploaded", reader.GetDateTimeOffset(3));

                case "Conflict":
                    var conflicts = new List<int>();
                    await reader.NextResultAsync(cancellationToken);

                    while (await reader.ReadAsync(cancellationToken))
                    {
                        conflicts.Add(reader.GetInt32(0));
                    }

                    throw new MeasurementUploadConflictException(conflicts);

                default:
                    throw new InvalidOperationException($"{SppCt007Database.UploadProcedure} returned an unknown status '{status}'.");
            }
        }
        catch (SqlException error) when (SppCt007Database.IsMissingObject(error))
        {
            throw new UploadTablesMissingException(error);
        }
        catch (SqlException error) when (error.Errors.Cast<SqlError>().Any(e => UniqueViolations.Contains(e.Number)))
        {
            // The procedure's transaction was rolled back (XACT_ABORT): nothing of this upload is stored.
            throw new MeasurementUploadConflictException([]);
        }
    }

    private static object Nullable(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
}
