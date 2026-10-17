using System.Globalization;

namespace Frontend.Pages.Measurement.Table;

/// <summary>
/// Table display formatting of measurement cells (used through <see cref="MeasurementTableColumn.Format"/>). Display
/// only: models, calculations, SQLite and CSV keep the original values. A missing value is an empty cell, never 0; a
/// value that cannot be formatted is shown as it is.
/// </summary>
public static class MeasurementCellFormat
{
    public const string TimestampFormat = "yyyy-MM-dd HH:mm:ss";

    /// <summary>The format names MeasurementTable.yaml accepts.</summary>
    public static IReadOnlyList<string> Names { get; } = ["dateTime", "metres", "centimetres", "localTime", "decimal2"];

    /// <summary>The formatter of a name from <see cref="Names"/>, or null for an unknown name.</summary>
    public static Func<object?, string?>? Named(string name) => name switch
    {
        "dateTime" => Pattern(TimestampFormat),   // 2026-10-16 09:56:09 (the value's own time zone)
        "metres" => Pattern("0.00#"),             // 10.50, 11.255
        "centimetres" => Pattern("0.#"),          // 25, 2.5
        "localTime" => UnixMillisecondsLocal,     // Unix milliseconds as this computer's local time
        "decimal2" => Decimal,                    // at most two decimals
        _ => null
    };

    /// <summary>A .NET format pattern such as "0.00#" or <see cref="TimestampFormat"/>, applied with the invariant culture.</summary>
    public static Func<object?, string?> Pattern(string format) => value => value switch
    {
        null => null,
        IFormattable formattable => formattable.ToString(format, CultureInfo.InvariantCulture),
        var other => other.ToString()
    };

    /// <summary>A decimal device value with at most two decimals: 1.777777 → 1.78, 2.3456 → 2.35, 5 → 5, 1.5 → 1.5.</summary>
    public static string? Decimal(object? value) => value switch
    {
        null => null,
        double d when !double.IsFinite(d) => d.ToString(CultureInfo.InvariantCulture),
        double d => Clean(Math.Round(d, 2, MidpointRounding.AwayFromZero).ToString("0.##", CultureInfo.InvariantCulture)),
        float f => Decimal((double)f),
        decimal m => Clean(Math.Round(m, 2, MidpointRounding.AwayFromZero).ToString("0.##", CultureInfo.InvariantCulture)),
        IFormattable other => other.ToString(null, CultureInfo.InvariantCulture),
        var other => other.ToString()
    };

    /// <summary>
    /// A Unix time in milliseconds (UTC) as this computer's local time, "yyyy-MM-dd HH:mm:ss". Outside the range a
    /// date can represent, the raw number is shown instead.
    /// </summary>
    public static string? UnixMillisecondsLocal(object? value) => value switch
    {
        null => null,
        long ms when ms is >= MinUnixMs and <= MaxUnixMs =>
            DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture),
        IFormattable other => other.ToString(null, CultureInfo.InvariantCulture),
        var other => other.ToString()
    };

    // DateTimeOffset.FromUnixTimeMilliseconds limits (0001-01-01 to 9999-12-31), with a day of margin for the local offset.
    private const long MinUnixMs = -62_135_596_800_000 + 86_400_000;
    private const long MaxUnixMs = 253_402_300_799_999 - 86_400_000;

    // "-0" (a tiny negative value rounded away) is shown as "0".
    private static string Clean(string text) => text == "-0" ? "0" : text;
}
