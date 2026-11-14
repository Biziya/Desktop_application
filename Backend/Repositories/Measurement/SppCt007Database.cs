using System.Globalization;
using System.Text;
using System.Text.Json;
using Backend.Models.Measurement;
using Microsoft.Data.SqlClient;

namespace Backend.Repositories.Measurement;

// The only place naming the SQL objects; rename here and in Database/Migrations/001_CreateSppCt007.sql together.
public static class SppCt007Database
{
    public const string Table = "dbo.SPPCT007_BEK";
    public const string UploadProcedure = "dbo.SPPCT007_BEK_Upload";
    public const string Role = "SPPCT007_APP";

    public const string MigrationScript = "001_CreateSppCt007.sql";

    // Must match the procedure's OPENJSON WITH paths exactly (case-sensitive).
    public static readonly IReadOnlyList<string> RecordKeys =
    [
        "CAPTURE_NO", "ROW_NO", "START_TIME", "END_TIME", "SAMPLE_COUNT", "DEPTH_FROM_M", "DEPTH_TO_M",
        "TS_MS", "COUNT0", "COUNT0_RAW", "HDM", "HDM_RAW", "DOSE_RATE",
    ];

    // 208 invalid object name (table or sequence), 2812 stored procedure not found.
    private static readonly int[] MissingObjectErrors = [208, 2812];

    public static bool IsMissingObject(SqlException error) => error.Errors.Cast<SqlError>().Any(e => MissingObjectErrors.Contains(e.Number));

    // ROW_NO from 1 in request order; times as round-trip ISO 8601 with offset; missing device values as null.
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
                json.WriteString("START_TIME", r.StartTime!.Value.ToString("O", CultureInfo.InvariantCulture));
                json.WriteString("END_TIME", r.EndTime!.Value.ToString("O", CultureInfo.InvariantCulture));
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
