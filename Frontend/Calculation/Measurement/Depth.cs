using System.Globalization;

namespace Frontend.Calculation.Measurement;

/// <summary>
/// Depths are whole millimetres so intervals add up exactly (no floating-point drift); input finer than 1 mm is
/// rejected, not rounded. Only the UI boundary converts: depths in metres, Step in centimetres.
/// </summary>
public static class Depth
{
    // Point and comma are both accepted regardless of the Windows locale; group separators are not.
    public static bool TryParseMetres(string text, out long millimetres)
    {
        millimetres = 0;

        if (!decimal.TryParse(text.Replace(',', '.'), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite,
                CultureInfo.InvariantCulture, out var metres) || metres < 0)
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

    // 2.5 cm → 25 mm; more than one decimal (finer than 1 mm) is rejected.
    public static bool TryParseCentimetres(string text, out long millimetres)
    {
        millimetres = 0;

        if (!decimal.TryParse(text.Replace(',', '.'), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite,
                CultureInfo.InvariantCulture, out var centimetres) || centimetres < 0)
        {
            return false;
        }

        var mm = centimetres * 10m;

        if (mm != decimal.Truncate(mm) || mm > long.MaxValue / 2)
        {
            return false;
        }

        millimetres = (long)mm;
        return true;
    }

    // 250 mm → "25", 25 mm → "2.5".
    public static string FormatCentimetres(long millimetres) =>
        (millimetres / 10m).ToString("0.#", CultureInfo.InvariantCulture);

    // Metres: 10500 mm → "10.50", 11255 mm → "11.255".
    public static string Format(long millimetres) =>
        (millimetres / 1000m).ToString("0.00#", CultureInfo.InvariantCulture);
}
