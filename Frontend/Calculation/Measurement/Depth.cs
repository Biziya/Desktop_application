using System.Globalization;

namespace Frontend.Calculation.Measurement;

/// <summary>
/// Depths are stored and calculated as whole millimetres (<see cref="long"/>) and shown in metres: 1 m = 1000 mm.
/// Integer arithmetic means intervals add up exactly (no cumulative floating-point error). Input finer than 1 mm
/// is rejected rather than rounded. Unit conversion happens only here, at the UI boundary: Starting Depth, From and To
/// are entered and shown in metres, Step in centimetres (to_m = from_m + step_cm / 100), storage stays in mm.
/// </summary>
public static class Depth
{
    /// <summary>
    /// Parses a non-negative depth in metres. Both "10.25" and "10,25" are accepted whatever the Windows number
    /// format: with a comma-decimal locale (fr-FR, ru-RU, …) "10.25" was rejected before. Group separators are not.
    /// </summary>
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

    /// <summary>
    /// Parses a step in centimetres (the Step (cm) input) to millimetres: 25 cm → 250 mm, 2.5 cm → 25 mm. Steps are
    /// still stored in millimetres, so a step finer than 1 mm (more than one decimal in cm) is rejected. A point or a
    /// comma is accepted as decimal separator, as for metres.
    /// </summary>
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

    /// <summary>Centimetres for the Step input and the Step (cm) column: 250 mm → "25", 25 mm → "2.5".</summary>
    public static string FormatCentimetres(long millimetres) =>
        (millimetres / 10m).ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>Metres with a decimal point and two to three decimals (the same form the inputs accept), e.g. 10500 mm → "10.50", 11255 mm → "11.255".</summary>
    public static string Format(long millimetres) =>
        (millimetres / 1000m).ToString("0.00#", CultureInfo.InvariantCulture);
}
