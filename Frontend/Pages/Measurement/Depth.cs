using System.Globalization;

namespace Frontend.Pages.Measurement;

/// <summary>
/// Depths are stored and calculated as whole millimetres (<see cref="long"/>) and shown in metres: 1 m = 1000 mm.
/// Integer arithmetic means intervals add up exactly (no cumulative floating-point error). Input finer than 1 mm
/// is rejected rather than rounded.
/// </summary>
public static class Depth
{
    /// <summary>Parses a non-negative depth in metres (current culture, as on the Core Information page).</summary>
    public static bool TryParseMetres(string text, out long millimetres)
    {
        millimetres = 0;

        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite,
                CultureInfo.CurrentCulture, out var metres) || metres < 0)
        {
            return false;
        }

        var mm = metres * 1000m;

        if (mm != decimal.Truncate(mm) || mm > long.MaxValue / 2)
        {
            return false;
        }

        millimetres = (long)mm;
        return true;
    }

    /// <summary>Metres with two to three decimals, e.g. 10500 mm → "10.50", 11255 mm → "11.255".</summary>
    public static string Format(long millimetres) =>
        (millimetres / 1000m).ToString("0.00#", CultureInfo.CurrentCulture);
}
