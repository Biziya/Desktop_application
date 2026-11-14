using Frontend.Pages.Measurement;

namespace Frontend.Calculation.Measurement;

/// <summary>Read-only access to the device's samples (the Gamma Guard database).</summary>
public interface ISampleSource
{
    long? MaxSampleId();

    /// <summary>Samples of one detector with id greater than <paramref name="afterId"/>, in id order.</summary>
    IReadOnlyList<DeviceSample> ReadSamplesAfter(string serial, long afterId);

    long? LatestTsMs(string serial);
}

/// <summary>The detector and its sample source, frozen when a capture starts.</summary>
public sealed record CaptureDevice(DetectorIdentity Identity, ISampleSource Samples);

public interface ICaptureDeviceResolver
{
    /// <exception cref="CaptureException">No single detector can be used; the message says why.</exception>
    CaptureDevice Resolve();
}

/// <summary>Unix-ms wall clock and delay, replaceable in tests.</summary>
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
/// Captures the samples the frozen detector recorded in <c>click &lt; ts_ms &lt;= click + duration</c>. Polling starts
/// <see cref="LookbackRows"/> before the newest id so a row written at the click is not missed, then reads only larger
/// ids. A bounded final read (<see cref="FinalReadLimit"/>) picks up late writes; the window never grows to make up
/// for missing samples.
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

        return new CaptureData(
            start,
            start + duration,
            [],
            "",
            DateTimeOffset.Now,
            DeviceSamples: window.Accepted,
            Device: device.Identity,
            ExpectedSamples: window.ExpectedSamples);
    }
}
