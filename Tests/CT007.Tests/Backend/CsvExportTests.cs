namespace Ct007.Tests.BackendTests;

/// <summary>BE-042, BE-043: CSV serialisation contract and spreadsheet-formula neutralisation (F19).</summary>
public static class CsvExportTests
{
    /// <summary>
    /// The exported header row (current contract, UR-04: depths, Created At and Created Username added so exports can be imported on
    /// the Upload Data page); also used by E2E-001.
    /// </summary>
    internal const string Header =
        "\"No\",\"Capture No\",\"Start Time\",\"End Time\",\"from_m\",\"to_m\",\"step_m\",\"Sample\",\"ts_ms\",\"count0\",\"count0_raw\",\"hdm\",\"hdm_raw\",\"dose_rate\"," +
        "\"Drillhole\",\"Project\",\"Campaign\",\"Operator\",\"Notes\",\"Created At\",\"Created Username\",\"session_id\"";

    private static MeasurementSession Session(string notes = "", string operatorName = "Ann", string drillhole = "DH-001") =>
        new(Guid.Parse("33333333-3333-3333-3333-333333333333"), DateTimeOffset.UnixEpoch, drillhole, "North Ridge", "2024", operatorName, notes);

    private static MeasurementResult Row(MeasurementSession session, int number, DeviceReading reading)
    {
        var row = Make.Row(session.Id, number, 10_000, reading: reading,
            start: new DateTimeOffset(2026, 10, 16, 9, 0, 0, TimeSpan.FromHours(8)).AddTicks(1234567));
        row.Number = number;
        return row;
    }

    [Test("BE-042", "CSV: fixed header order, every cell quoted, CRLF, invariant round-trip formats (UR-04, UR-22)")]
    public static void Structure()
    {
        var session = Session();
        var rows = new[]
        {
            Row(session, 1, new DeviceReading(1_760_600_000_001, 0.1, 21, 1.25, 2.5, 0.125)),
            Row(session, 2, new DeviceReading(null, 1e-300, null, null, null, null))
        };
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
        string csv;

        try
        {
            csv = CsvExportService.Serialize(rows, session, "CORP\\ann");
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }

        var meta = "\"DH-001\",\"North Ridge\",\"2024\",\"Ann\",\"\",\"1970-01-01T00:00:00.0000000+00:00\",\"CORP\\ann\",\"33333333-3333-3333-3333-333333333333\"";
        var expected =
            Header + "\r\n" +
            "\"1\",\"1\",\"2026-10-16T09:00:00.1234567+08:00\",\"2026-10-16T09:00:05.1234567+08:00\",\"10\",\"10.25\",\"0.25\",\"5\",\"1760600000001\"," +
            "\"0.1\",\"21\",\"1.25\",\"2.5\",\"0.125\"," + meta + "\r\n" +
            "\"2\",\"2\",\"2026-10-16T09:00:00.1234567+08:00\",\"2026-10-16T09:00:05.1234567+08:00\",\"10\",\"10.25\",\"0.25\",\"5\",\"\"," +
            "\"1E-300\",\"\",\"\",\"\",\"\"," + meta + "\r\n";
        Assert.Equal(expected, csv, "CSV text");
        Assert.False(csv.Contains("temperature", StringComparison.Ordinal), "temperature and pressure are not exported (current contract, UR-04)");

        Assert.Equal(Header + "\r\n", CsvExportService.Serialize([], session, "CORP\\ann"), "no rows: header only");
        var reversed = CsvExportService.Serialize(Enumerable.Reverse(rows).ToList(), session).Split("\r\n");
        Assert.True(reversed[1].StartsWith("\"2\"", StringComparison.Ordinal), "rows written in the given (displayed) order");
    }

    [Test("BE-043", "CSV: user text cannot become a formula; commas, quotes and line breaks stay in one cell; one session only (UR-18)")]
    public static void InjectionAndMixing()
    {
        var cases = new (string Input, string Cell)[]
        {
            ("=1+1", "\"'=1+1\""),
            ("+A", "\"'+A\""),
            ("-2", "\"'-2\""),
            ("@SUM(A1)", "\"'@SUM(A1)\""),
            ("  =x", "\"'  =x\""),
            ("a,b", "\"a,b\""),
            ("line1\r\nline2", "\"line1\r\nline2\""),
            ("say \"hi\"", "\"say \"\"hi\"\"\"")
        };

        foreach (var (input, cell) in cases)
        {
            foreach (var field in new[] { "Notes", "Operator", "Drillhole" })
            {
                var session = field switch
                {
                    "Notes" => Session(notes: input),
                    "Operator" => Session(operatorName: input),
                    _ => Session(drillhole: input)
                };
                var csv = CsvExportService.Serialize([Row(session, 1, new DeviceReading(Count0: -5))], session);
                Assert.Contains("," + cell + ",", csv, $"{field} '{input}'");
                Assert.Contains(",\"-5\",", csv, $"{field} '{input}': measurement cells are not prefixed");
            }
        }

        var a = Session();
        var b = a with { Id = Guid.NewGuid() };
        Assert.Throws<ArgumentException>(() => CsvExportService.Serialize([Row(a, 1, DeviceReading.Empty), Row(b, 2, DeviceReading.Empty)], a),
            "rows of two sessions");
    }
}
