using Frontend.Pages.Measurement;

namespace Frontend.Calculation.Measurement;

/// <summary>Read-only access to the device's samples (the Gamma Guard database).</summary>
public interface ISampleSource
{
    /// <summary>Highest samples.id right now (null when the table is empty).</summary>
    long? MaxSampleId();

    /// <summary>Samples of one detector with id greater than <paramref name="afterId"/>, in id order.</summary>
    IReadOnlyList<DeviceSample> ReadSamplesAfter(string serial, long afterId);

    /// <summary>ts_ms of the detector's newest sample (null when it has none).</summary>
    long? LatestTsMs(string serial);
}

/// <summary>The detector to capture from and where its samples are, resolved (and frozen) when a capture starts.</summary>
public sealed record CaptureDevice(DetectorIdentity Identity, ISampleSource Samples);

public interface ICaptureDeviceResolver
{
    /// <summary>The one connected CT007-M detector; throws <see cref="CaptureException"/> with the reason otherwise.</summary>
    CaptureDevice Resolve();
}

/// <summary>Wall clock (Unix ms, same basis as the click time and ts_ms) and waiting — replaceable in tests.</summary>
public interface ICaptureClock
{
    long NowMs { get; }

    Task Delay(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class SystemCaptureClock : ICaptureClock
{
    public static SystemCaptureClock Instance { get; } = new();

    public long NowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public Task Delay(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);
}

/// <summary>
/// Real capture: reads the samples the selected CT007-M detector recorded after the click from the Gamma Guard
/// database (read-only) and returns those inside <c>click &lt; ts_ms &lt;= click + duration</c>.
/// <list type="number">
/// <item>The click time is the mouse-down timestamp (ms). The detector, its database and the window are frozen at once.</item>
/// <item>The newest samples.id is noted, and reading continues from a little before it (<see cref="LookbackRows"/>,
/// so a row written in the same instant is not missed). Only rows with a larger id are read on each poll — never
/// "the last N rows" — and every row is filtered by serial and by the window; each source id counts once.</item>
/// <item>After the window ends, a short bounded final read (<see cref="FinalReadLimit"/>) picks up rows the device wrote
/// late. The window itself never changes and never grows to replace missing samples.</item>
/// </list>
/// Cancellation (Stop, closing) ends the capture without a result. No values are invented: missing samples are
/// reported by the caller as an incomplete capture, and no samples at all is an error.
/// </summary>
public sealed class DeviceCaptureService(ICaptureDeviceResolver resolver, ICaptureClock? clock = null) : IMeasurementService
{
    public const int LookbackRows = 200;
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan FinalReadLimit = TimeSpan.FromMilliseconds(1500);

    private readonly ICaptureClock clock = clock ?? SystemCaptureClock.Instance;

    public async Task<CaptureData> CaptureAsync(
        DateTimeOffset start,
        TimeSpan duration,
        Action<TimeSpan> reportRemaining,
        CancellationToken cancellationToken)
    {
        var triggerMs = start.ToUnixTimeMilliseconds();
        var durationMs = (long)duration.TotalMilliseconds;
        reportRemaining(duration);

        // Frozen for the whole capture: the detector and its database (no silent switch to another device).
        var device = await Task.Run(resolver.Resolve, cancellationToken);
        var serial = device.Identity.Serial;
        var source = device.Samples;
        var window = new CaptureWindow(serial, triggerMs, durationMs);

        var baseline = await Task.Run(source.MaxSampleId, cancellationToken) ?? 0;
        var afterId = Math.Max(0, baseline - LookbackRows);

        if (await Task.Run(() => source.LatestTsMs(serial), cancellationToken) is { } latest &&
            DeviceTime.Problem(latest, clock.NowMs) is { } problem)
        {
            throw new CaptureException(problem);
        }

        async Task Poll()
        {
            var rows = await Task.Run(() => source.ReadSamplesAfter(serial, afterId), cancellationToken);

            if (rows.Count > 0)
            {
                afterId = Math.Max(afterId, rows.Max(r => r.SourceId));
                window.Add(rows);
            }
        }

        // During the window: read new rows and report the countdown from the click time.
        while (clock.NowMs < window.EndMs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Poll();
            var remaining = TimeSpan.FromMilliseconds(Math.Max(0, window.EndMs - clock.NowMs));
            reportRemaining(remaining);

            if (remaining > TimeSpan.Zero)
            {
                await clock.Delay(remaining < PollInterval ? remaining : PollInterval, cancellationToken);
            }
        }

        reportRemaining(TimeSpan.Zero);

        // Short, bounded final read for late database writes; stops early once every expected sample is in.
        var finalDeadline = clock.NowMs + (long)FinalReadLimit.TotalMilliseconds;
        await Poll();

        while (window.Count < window.ExpectedSamples && clock.NowMs < finalDeadline)
        {
            await clock.Delay(PollInterval, cancellationToken);
            await Poll();
        }

        cancellationToken.ThrowIfCancellationRequested();
        var accepted = window.Accepted;

        return new CaptureData(
            start,
            start + duration,
            [],
            "",
            DateTimeOffset.Now,
            DeviceSamples: accepted,
            Device: device.Identity,
            ExpectedSamples: window.ExpectedSamples);
    }
}
