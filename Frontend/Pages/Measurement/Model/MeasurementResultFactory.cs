using Frontend.Calculation.Measurement;
using Frontend.Shared.Models;

namespace Frontend.Pages.Measurement;

/// <summary>Turns a finished capture into a table row: device samples are averaged, other services' values are used as delivered.</summary>
public static class MeasurementResultFactory
{
    /// <exception cref="CaptureException">The capture has no samples; nothing may be saved.</exception>
    public static MeasurementResult Create(CaptureData data, MeasurementSession session, CoreSnapshot core, int captureNumber,
        long fromMm, long toMm, long stepMm, TimeSpan duration)
    {
        var deviceSamples = data.DeviceSamples;
        var sampleCount = deviceSamples?.Count ?? data.Samples.Count;

        if (sampleCount == 0)
        {
            throw new CaptureException(deviceSamples is null
                ? "The capture returned no samples. Nothing was saved."
                : $"No samples were received from {data.Device?.Name ?? data.Device?.Serial ?? "the device"} during the {duration.TotalSeconds:0} s capture. Nothing was saved.");
        }

        var summary = deviceSamples is null ? null : SampleAveraging.Summarize(deviceSamples);
        var device = deviceSamples is null ? null : data.Device;

        return new MeasurementResult
        {
            SessionId = session.Id,
            CaptureNumber = captureNumber,
            StartTime = data.StartTime,
            EndTime = data.EndTime,
            CompletedAt = data.CompletedAt,
            SampleCount = sampleCount,
            ExpectedSamples = data.ExpectedSamples,
            DurationMs = (long)(data.EndTime - data.StartTime).TotalMilliseconds,
            Reading = summary is null
                ? data.Reading ?? DeviceReading.Empty
                : new DeviceReading(summary.FirstTsMs, summary.Count0, summary.Count0Raw, summary.Hdm, summary.HdmRaw, summary.DoseRate),
            Temperature = summary?.Temperature,
            Pressure = summary?.Pressure,
            DeviceSerial = device?.Serial,
            DeviceName = device?.Name,
            DeviceMac = device?.Mac,
            DeviceSessionId = summary?.DeviceSessionId,
            Samples = deviceSamples,
            AverageValue = deviceSamples is null ? data.Samples.Average() : null,
            Unit = deviceSamples is null ? data.Unit : null,
            Core = core,
            FromMm = fromMm,
            ToMm = toMm,
            StepMm = stepMm
        };
    }
}
