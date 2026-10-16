namespace Ct007.Tests.BackendTests;

/// <summary>BE-015…BE-019: depth parsing, formatting, integer arithmetic and row ordering (F12, F16).</summary>
public static class DepthAndOrderTests
{
    private static readonly string[] Cultures = ["", "fr-FR", "ru-RU"];

    private static void InEachCulture(Action check)
    {
        var original = CultureInfo.CurrentCulture;

        try
        {
            foreach (var name in Cultures)
            {
                CultureInfo.CurrentCulture = name.Length == 0 ? CultureInfo.InvariantCulture : new CultureInfo(name);
                check();
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Test("BE-015", "Depth.TryParseMetres: whole millimetres, '.' or ',', no sign, no exponent, no grouping")]
    public static void ParseMetres()
    {
        var cases = new (string Text, long? Mm)[]
        {
            ("0", 0), ("10.25", 10_250), ("10,25", 10_250), (" 10.5 ", 10_500), ("10.255", 10_255), ("10.2550", 10_255),
            ("10.2555", null), ("-0.001", null), ("-0", null), ("+5", null), ("1e3", null), ("1,000.5", null),
            ("", null), ("abc", null), ("١٠", null), ("9999999999999999.999", null)
        };

        InEachCulture(() =>
        {
            foreach (var (text, mm) in cases)
            {
                var ok = Depth.TryParseMetres(text, out var parsed);
                Assert.Equal(mm.HasValue, ok, $"'{text}' accepted ({CultureInfo.CurrentCulture.Name})");

                if (mm is { } expected)
                {
                    Assert.Equal(expected, parsed, $"'{text}' in mm ({CultureInfo.CurrentCulture.Name})");
                }
            }
        });
    }

    [Test("BE-016", "Depth.TryParseCentimetres: whole millimetres; the parser itself accepts 0 (UR-20)")]
    public static void ParseCentimetres()
    {
        var cases = new (string Text, long? Mm)[]
        {
            ("25", 250), ("2.5", 25), ("2,5", 25), ("0", 0), ("0.1", 1), ("1000", 10_000),
            ("0.05", null), ("2.55", null), ("-1", null), ("", null)
        };

        InEachCulture(() =>
        {
            foreach (var (text, mm) in cases)
            {
                var ok = Depth.TryParseCentimetres(text, out var parsed);
                Assert.Equal(mm.HasValue, ok, $"'{text}' accepted ({CultureInfo.CurrentCulture.Name})");

                if (mm is { } expected)
                {
                    Assert.Equal(expected, parsed, $"'{text}' in mm ({CultureInfo.CurrentCulture.Name})");
                }
            }
        });
    }

    [Test("BE-017", "Depth formatting is invariant and round-trips through the parser")]
    public static void Formatting()
    {
        InEachCulture(() =>
        {
            Assert.Equal("0.00", Depth.Format(0), "0 mm");
            Assert.Equal("10.50", Depth.Format(10_500), "10500 mm");
            Assert.Equal("11.255", Depth.Format(11_255), "11255 mm");
            Assert.Equal("0.001", Depth.Format(1), "1 mm");
            Assert.Equal("25", Depth.FormatCentimetres(250), "250 mm in cm");
            Assert.Equal("2.5", Depth.FormatCentimetres(25), "25 mm in cm");
            Assert.Equal("0.5", Depth.FormatCentimetres(5), "5 mm in cm");
        });

        for (long mm = 0; mm <= 100_000; mm += 7)
        {
            Assert.True(Depth.TryParseMetres(Depth.Format(mm), out var back) && back == mm, $"{mm} mm round-trips");
            Assert.True(Depth.TryParseCentimetres(Depth.FormatCentimetres(mm), out var step) && step == mm, $"{mm} mm step round-trips");
        }
    }

    [Test("BE-018", "1000 steps of 2.5 cm from 0 end exactly at 25.00 m")]
    public static void NoCumulativeDrift()
    {
        Assert.True(Depth.TryParseCentimetres("2.5", out var step), "step parsed");
        long depth = 0;

        for (var i = 0; i < 1000; i++)
        {
            var to = depth + step;
            Assert.Equal(25L, to - depth, $"interval {i}");
            depth = to;
        }

        Assert.Equal(25_000L, depth, "final depth (mm)");
        Assert.Equal("25.00", Depth.Format(depth), "final depth shown");
    }

    [Test("BE-019", "MeasurementOrder: From ascending, nulls last, then start time, stored rows, capture number")]
    public static void Ordering()
    {
        var session = Guid.NewGuid();
        var t = new DateTimeOffset(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);
        MeasurementResult R(int capture, long? fromMm, DateTimeOffset start, long? record = null) =>
            Make.Row(session, capture, fromMm, recordId: record, start: start);

        var noDepth = R(1, null, t);
        var deep = R(2, 20_000, t);
        var laterStart = R(3, 10_000, t.AddMinutes(1));
        var unsaved = R(4, 10_000, t);
        var storedB = R(5, 10_000, t, record: 9);
        var storedA = R(6, 10_000, t, record: 3);
        var sameStoredLowCapture = R(0, 10_000, t, record: 3);

        var sorted = MeasurementOrder.Sort(new[] { noDepth, deep, laterStart, unsaved, storedB, storedA, sameStoredLowCapture });
        Assert.Sequence(new[] { 0, 6, 5, 4, 3, 2, 1 }, sorted.Select(r => r.CaptureNumber), "order");

        var random = new Random(42);
        var rows = Enumerable.Range(1, 1000).Select(i => R(i, random.Next(4) == 0 ? null : random.Next(50) * 250L,
            t.AddSeconds(random.Next(20)), random.Next(2) == 0 ? random.Next(1, 500) : null)).ToList();
        var incremental = new List<MeasurementResult>();

        foreach (var row in rows)
        {
            incremental.Insert(MeasurementOrder.InsertIndex(incremental, row), row);
        }

        Assert.Sequence(MeasurementOrder.Sort(rows).Select(r => r.CaptureNumber), incremental.Select(r => r.CaptureNumber),
            "binary insertion gives the full sort order");
    }
}
