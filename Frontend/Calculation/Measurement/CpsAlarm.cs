using System.Globalization;

namespace Frontend.Calculation.Measurement;

/// <summary>
/// CPS threshold alarm of the Measurement page. It is checked once per completed row, with the same finalized CPS the
/// table shows (the row's averaged count0, counts per second), and sounds only when that value is strictly greater than
/// the threshold. The sound is played without waiting for it, so acquisition, the countdown and the row are never held
/// up or changed. While a sound is still playing, another one is not started (no overlapping alarms).
/// </summary>
public sealed class CpsAlarm(Action play, TimeSpan soundLength, Func<DateTimeOffset>? clock = null)
{
    private readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.UtcNow);
    private DateTimeOffset? playingUntil;

    public static bool Exceeds(double? cps, double? threshold) => cps is { } value && threshold is { } limit && value > limit;

    /// <summary>
    /// Parses a threshold typed on the page: a number of CPS, not negative, at most two decimals (the table's CPS
    /// precision). A point or a comma is accepted as decimal separator, as for depths.
    /// </summary>
    public static bool TryParseThreshold(string text, out double threshold)
    {
        threshold = 0;

        if (!decimal.TryParse(text.Replace(',', '.'), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite,
                CultureInfo.InvariantCulture, out var value) || value < 0 || decimal.Round(value, 2) != value)
        {
            return false;
        }

        threshold = (double)value;
        return true;
    }

    public static string FormatThreshold(double threshold) => threshold.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Sounds the alarm when <paramref name="cps"/> exceeds <paramref name="threshold"/>; true when it sounded.</summary>
    public bool Check(double? cps, double? threshold)
    {
        if (!Exceeds(cps, threshold) || (playingUntil is { } until && now() < until))
        {
            return false;
        }

        playingUntil = now() + soundLength;

        try
        {
            play();
        }
        catch (Exception)
        {
            // No audio device or a driver error must never interrupt a measurement.
            return false;
        }

        return true;
    }

    /// <summary>Stop: the next measurement session starts without a pending sound state.</summary>
    public void Reset() => playingUntil = null;
}
