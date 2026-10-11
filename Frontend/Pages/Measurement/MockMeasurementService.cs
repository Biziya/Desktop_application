using System.Diagnostics;
namespace Frontend.Pages.Measurement;
public sealed record CaptureData(DateTimeOffset StartTime, DateTimeOffset EndTime,
    IReadOnlyList<double> Samples, string Unit);
public interface IMeasurementService
{
    Task<CaptureData> CaptureAsync(DateTimeOffset start, Action<TimeSpan> reportRemaining,
        CancellationToken cancellationToken);
}
public sealed class MockMeasurementService : IMeasurementService
{
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(5);
    public async Task<CaptureData> CaptureAsync(DateTimeOffset start,
        Action<TimeSpan> reportRemaining, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var samples = new List<double>(5);
        // Five synthetic samples, one at each second boundary. Scheduler delays never
        // extend the logical capture window or change the sample count.
        reportRemaining(Duration);
        while (clock.Elapsed < Duration)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var elapsed = clock.Elapsed;
            while (samples.Count < 5 && elapsed.TotalSeconds >= samples.Count + 1)
                samples.Add(10 + Random.Shared.NextDouble() * 5);
            reportRemaining(Duration > elapsed ? Duration - elapsed : TimeSpan.Zero);
            var delay = Duration - clock.Elapsed;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay < TimeSpan.FromMilliseconds(40)
                    ? delay : TimeSpan.FromMilliseconds(40), cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
        while (samples.Count < 5) samples.Add(10 + Random.Shared.NextDouble() * 5);
        reportRemaining(TimeSpan.Zero);
        return new CaptureData(start, start + Duration, samples.AsReadOnly(), "cps");
    }
}
