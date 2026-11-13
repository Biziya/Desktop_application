using System.Data;
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
    public const string RecordType = "dbo.SPPCT007_BEK_RECORD";
    public const string UploadProcedure = "dbo.SPPCT007_BEK_Upload";
    public const string Role = "SPPCT007_APP";

    /// <summary>The migration script's file name (under Database/Migrations).</summary>
    public const string MigrationScript = "001_CreateSppCt007.sql";

    /// <summary>The columns of <see cref="RecordType"/>, in its order.</summary>
    public static readonly IReadOnlyList<(string Name, Type Type)> RecordColumns =
    [
        ("CAPTURE_NO", typeof(int)),
        ("ROW_NO", typeof(int)),
        ("START_TIME", typeof(DateTimeOffset)),
        ("END_TIME", typeof(DateTimeOffset)),
        ("SAMPLE_COUNT", typeof(int)),
        ("DEPTH_FROM_M", typeof(decimal)),
        ("DEPTH_TO_M", typeof(decimal)),
        ("TS_MS", typeof(long)),
        ("COUNT0", typeof(double)),
        ("COUNT0_RAW", typeof(double)),
        ("HDM", typeof(double)),
        ("HDM_RAW", typeof(double)),
        ("DOSE_RATE", typeof(double)),
    ];

    // 208 invalid object name (table), 2812 stored procedure not found, 2715 type not found (the TVP's record type).
    private static readonly int[] MissingObjectErrors = [208, 2812, 2715];

    /// <summary>The table, the insert procedure or the record type does not exist yet.</summary>
    public static bool IsMissingObject(SqlException error) => error.Errors.Cast<SqlError>().Any(e => MissingObjectErrors.Contains(e.Number));

    /// <summary>
    /// The rows of a validated upload as the table-valued parameter: <see cref="RecordColumns"/> in order, ROW_NO from 1 in
    /// request order, DBNull for missing device values.
    /// </summary>
    public static DataTable ToRecordTable(IReadOnlyList<MeasurementUploadRecord> records)
    {
        var table = new DataTable();

        foreach (var (name, type) in RecordColumns)
        {
            table.Columns.Add(name, type);
        }

        table.BeginLoadData();

        for (var i = 0; i < records.Count; i++)
        {
            var r = records[i];
            table.Rows.Add(
                r.CaptureNumber!.Value, i + 1, r.StartTime!.Value, r.EndTime!.Value, r.SampleCount!.Value,
                r.DepthFrom!.Value, r.DepthTo!.Value,
                Value(r.TsMs), Value(r.Count0), Value(r.Count0Raw), Value(r.Hdm), Value(r.HdmRaw), Value(r.DoseRate));
        }

        table.EndLoadData();
        return table;
    }

    private static object Value<T>(T? value) where T : struct => value.HasValue ? value.Value : DBNull.Value;
}
