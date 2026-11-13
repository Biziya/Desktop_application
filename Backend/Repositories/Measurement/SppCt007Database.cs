using System.Text;
using System.Text.Json;
using Backend.Models.Measurement;
using Microsoft.Data.SqlClient;

namespace Backend.Repositories.Measurement;

/// <summary>
/// The SPP CT007 storage on SQL Server (ACQMN01_DEV): rows are inserted only through <see cref="UploadProcedure"/> and
/// read with SELECT (Database/Migrations/001_CreateSppCt007.sql). The only place in the backend that names the database
/// objects; rename them here and in the script together (e.g. for production).
/// </summary>
public static class SppCt007Database
{
    public const string Table = "dbo.SPPCT007_BEK";
    public const string UploadProcedure = "dbo.SPPCT007_BEK_Upload";
    public const string Role = "SPPCT007_APP";

    /// <summary>The migration script's file name (under Database/Migrations).</summary>
    public const string MigrationScript = "001_CreateSppCt007.sql";

    /// <summary>
    /// The keys of one row in the procedure's @RecordsJson, in order. They must match the procedure's OPENJSON WITH
    /// paths exactly (JSON keys are case-sensitive).
    /// </summary>
    public static readonly IReadOnlyList<string> RecordKeys =
    [
        "CAPTURE_NO", "ROW_NO", "START_TIME", "END_TIME", "SAMPLE_COUNT", "DEPTH_FROM_M", "DEPTH_TO_M",
        "TS_MS", "COUNT0", "COUNT0_RAW", "HDM", "HDM_RAW", "DOSE_RATE",
    ];

    // 208 invalid object name (table or sequence), 2812 stored procedure not found.
    private static readonly int[] MissingObjectErrors = [208, 2812];

    /// <summary>The table or the insert procedure does not exist yet.</summary>
    public static bool IsMissingObject(SqlException error) => error.Errors.Cast<SqlError>().Any(e => MissingObjectErrors.Contains(e.Number));

    /// <summary>
    /// The rows of a validated upload as the procedure's @RecordsJson: a JSON array, one object per row with
    /// <see cref="RecordKeys"/>; ROW_NO from 1 in request order; times in ISO 8601 with their offset and all 7 decimals;
    /// numbers written exactly (round-trip); missing device values as null.
    /// </summary>
    public static string ToRecordsJson(IReadOnlyList<MeasurementUploadRecord> records)
    {
        using var buffer = new MemoryStream();

        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartArray();

            for (var i = 0; i < records.Count; i++)
            {
                var r = records[i];
                json.WriteStartObject();
                json.WriteNumber("CAPTURE_NO", r.CaptureNumber!.Value);
                json.WriteNumber("ROW_NO", i + 1);
                json.WriteString("START_TIME", r.StartTime!.Value.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
                json.WriteString("END_TIME", r.EndTime!.Value.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
                json.WriteNumber("SAMPLE_COUNT", r.SampleCount!.Value);
                json.WriteNumber("DEPTH_FROM_M", r.DepthFrom!.Value);
                json.WriteNumber("DEPTH_TO_M", r.DepthTo!.Value);
                Number(json, "TS_MS", r.TsMs);
                Number(json, "COUNT0", r.Count0);
                Number(json, "COUNT0_RAW", r.Count0Raw);
                Number(json, "HDM", r.Hdm);
                Number(json, "HDM_RAW", r.HdmRaw);
                Number(json, "DOSE_RATE", r.DoseRate);
                json.WriteEndObject();
            }

            json.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void Number(Utf8JsonWriter json, string key, long? value)
    {
        if (value is { } number) json.WriteNumber(key, number); else json.WriteNull(key);
    }

    private static void Number(Utf8JsonWriter json, string key, double? value)
    {
        if (value is { } number) json.WriteNumber(key, number); else json.WriteNull(key);
    }
}
