using System.Globalization;

namespace Frontend.Calculation.Measurement;

/// <summary>
/// CPS threshold alarm, checked once per completed row; sounds when CPS is strictly above the threshold. Playback is
/// fire-and-forget so capture is never held up, and a new sound never overlaps one still playing.
/// </summary>
public sealed class CpsAlarm(Action play, TimeSpan soundLength, Func<DateTimeOffset>? clock = null)
{
    private readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.UtcNow);
    private DateTimeOffset? playingUntil;

    public static bool Exceeds(double? cps, double? threshold) => cps is { } value && threshold is { } limit && value > limit;

    /// <summary>Non-negative, at most two decimals (the table's CPS precision); point or comma as separator.</summary>
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

    /// <summary>True when the alarm sounded.</summary>
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

    public void Reset() => playingUntil = null;
}
