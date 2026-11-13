using System.Collections.ObjectModel;
using Frontend.Calculation.CoreInformation.GammaGuardDatabase;
using Frontend.Calculation.Measurement;

namespace Frontend.Pages.Monitoring;

/// <summary>
/// The Monitoring page's behaviour, ported from EIC's CT008 Monitor (MonitorViewModel). It holds no database
/// connection and no timer: <see cref="Apply"/> takes one poll's <see cref="MonitorSnapshot"/>, <see cref="Tick"/>
/// advances time-based rules. Call both on the UI thread; the collections are bound directly.
/// Rules kept from the CT008 integration guide:
/// * new data is read by id (<see cref="Cursor"/>), never by timestamp; a database whose ids went backwards was
///   recreated and the cursor follows it;
/// * a detector whose heartbeat is older than the stale limit is Stale, unless it is disconnected;
/// * a 5-second sample discards the first reading after the request and averages the next 5;
/// * a live detector whose response time is not 1 s is warned about.
/// </summary>
public sealed class MonitorEngine(MonitoringSettings settings)
{
    public const int ReadingsPerSample = 5;

    /// <summary>A sample gives up after this long (a full one takes about 6 s), e.g. when logging stopped.</summary>
    public const int SampleTimeoutMs = 12_000;

    // Readings are counted, not timed: the detector delivers about one per second with jitter, so a fixed 5 s window
    // can hold 4 or 6. The first reading after the request is discarded because, with a 1 s response time, it
    // integrates the second before its timestamp, which began before the request. The next 5 then cover exactly the
    // 5 seconds from the discarded reading's timestamp to the last reading's.
    private sealed class SampleJob(string serial, long startMs)
    {
        public string Serial { get; } = serial;
        public long StartMs { get; } = startMs;
        public DeviceSample? Anchor { get; set; }
        public List<DeviceSample> Readings { get; } = [];
    }

    private readonly Dictionary<string, DetectorRow> rows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SampleJob> jobs = new(StringComparer.Ordinal);

    public ObservableCollection<DetectorRow> Detectors { get; } = [];

    public ObservableCollection<ReadingRow> RecentReadings { get; } = [];

    /// <summary>Completed samples, newest first. Kept across reconnects; memory only.</summary>
    public ObservableCollection<SampleResult> Samples { get; } = [];

    /// <summary>Id of the last sample row processed; null until the first snapshot after a reset.</summary>
    public long? Cursor { get; private set; }

    public bool HasRunningSamples => jobs.Count > 0;

    /// <summary>Live detectors whose response time is not 1 s, as "serial (2 s)"; empty when all are fine.</summary>
    public IReadOnlyList<string> ResponseTimeWarnings { get; private set; } = [];

    /// <summary>Forgets the connection state (cursor, detectors, readings, running samples). Completed samples stay.</summary>
    public void Reset()
    {
        Cursor = null;
        rows.Clear();
        jobs.Clear();
        Detectors.Clear();
        RecentReadings.Clear();
        ResponseTimeWarnings = [];
    }

    public void Apply(MonitorSnapshot snapshot, long nowMs)
    {
        foreach (var status in snapshot.Detectors)
        {
            RowFor(status.Serial).Apply(status);
        }

        if (Cursor is null)
        {
            // First read: show every detector's newest reading at once, then follow new rows from the newest id.
            foreach (var sample in snapshot.Samples)
            {
                if (sample.Serial.Length > 0)
                {
                    RowFor(sample.Serial).Apply(sample);
                }
            }

            Cursor = snapshot.MaxSampleId;
        }
        else if (snapshot.MaxSampleId < Cursor)
        {
            // The file was deleted and recreated: ids start again at 1. Follow it instead of waiting for the new ids to
            // pass the old cursor; samples in progress cannot continue across files.
            Cursor = snapshot.MaxSampleId;
            CancelJobs("database was recreated");
        }
        else
        {
            foreach (var sample in snapshot.Samples)
            {
                Process(sample);
                Cursor = sample.SourceId;
            }
        }

        Tick(nowMs);
    }

    /// <summary>
    /// Starts a 5-second sample for <paramref name="serial"/>. Ignored while one is already running for it.
    /// </summary>
    public bool StartSample(string serial, long nowMs)
    {
        if (string.IsNullOrEmpty(serial) || jobs.ContainsKey(serial))
        {
            return false;
        }

        jobs[serial] = new SampleJob(serial, nowMs);
        var row = RowFor(serial);
        row.IsSampling = true;
        row.SampleStatus = "Waiting for first reading";
        return true;
    }

    /// <summary>Recomputes ages and states, finishes or times out samples and updates the warning.</summary>
    public void Tick(long nowMs)
    {
        foreach (var row in Detectors)
        {
            row.Refresh(nowMs, settings.StaleAfterMs);
        }

        foreach (var job in jobs.Values.ToList())
        {
            var row = RowFor(job.Serial);

            if (job.Readings.Count >= ReadingsPerSample)
            {
                Finish(job, row, nowMs);
            }
            else if (nowMs - job.StartMs >= SampleTimeoutMs)
            {
                Finish(job, row, nowMs);
            }
            else
            {
                row.SampleStatus = job.Anchor is null
                    ? "Waiting for first reading"
                    : $"Collecting {job.Readings.Count} of {ReadingsPerSample}";
            }
        }

        ResponseTimeWarnings = Detectors
            .Where(r => r.HasResponseTimeWarning && r.IsLive)
            .Select(r => $"{r.DisplayName} ({r.ResponseTimeText})")
            .ToList();
    }

    private void Process(DeviceSample sample)
    {
        if (sample.Serial.Length == 0)
        {
            return;
        }

        RowFor(sample.Serial).Apply(sample);
        AddRecent(sample);

        if (jobs.TryGetValue(sample.Serial, out var job) && sample.TsMs is { } ts && ts >= job.StartMs)
        {
            if (job.Anchor is null)
            {
                job.Anchor = sample;
            }
            else if (job.Readings.Count < ReadingsPerSample)
            {
                job.Readings.Add(sample);
            }
        }
    }

    private void Finish(SampleJob job, DetectorRow row, long nowMs)
    {
        jobs.Remove(job.Serial);
        var readings = job.Readings;
        var n = readings.Count;
        string window = "–";
        double? coverage = null;

        if (n > 0)
        {
            // Coverage starts where the first kept reading's second begins: the discarded reading's timestamp.
            var from = job.Anchor?.TsMs ?? readings[0].TsMs;
            var to = readings[^1].TsMs;

            if (from is { } f && to is { } t)
            {
                window = $"{MonitorFormat.PreciseTime(f)} – {MonitorFormat.PreciseTime(t)}";
                coverage = (t - f) / 1000.0;
            }
        }

        var result = new SampleResult(
            job.Serial,
            DateTimeOffset.FromUnixTimeMilliseconds(nowMs).ToLocalTime(),
            window,
            coverage,
            n,
            ReadingsPerSample,
            Mean(readings, s => s.Count0),
            Mean(readings, s => s.Count0Raw),
            Mean(readings, s => s.Hdm),
            Mean(readings, s => s.DoseRate),
            Mean(readings, s => s.Temperature));

        row.SampleStatus = n == 0
            ? $"No readings within {SampleTimeoutMs / 1000} s"
            : result.IsComplete
                ? $"Done: mean {result.Cpm:0} CPM"
                : $"Incomplete: {n} of {ReadingsPerSample} readings";
        row.IsSampling = false;

        Samples.Insert(0, result);
        Trim(Samples, settings.SamplesShown);
    }

    private void CancelJobs(string reason)
    {
        foreach (var job in jobs.Values)
        {
            var row = RowFor(job.Serial);
            row.IsSampling = false;
            row.SampleStatus = $"Cancelled: {reason}";
        }

        jobs.Clear();
    }

    private DetectorRow RowFor(string serial)
    {
        if (!rows.TryGetValue(serial, out var row))
        {
            row = new DetectorRow(serial);
            rows[serial] = row;
            Detectors.Add(row);
        }

        return row;
    }

    private void AddRecent(DeviceSample s)
    {
        RecentReadings.Insert(0, new ReadingRow(
            s.SourceId, s.TsMs is { } ts ? MonitorFormat.Time(ts) : "–", s.Serial, s.Seq,
            s.Count0, s.Count0Raw, s.Hdm, s.DoseRate, s.Temperature, s.DeviceSessionId));
        Trim(RecentReadings, settings.RecentReadingsShown);
    }

    // Mean over the readings that have a value; missing values are not counted as zero.
    private static double? Mean(IEnumerable<DeviceSample> readings, Func<DeviceSample, double?> field)
    {
        var values = readings.Select(field).Where(v => v is { } d && double.IsFinite(d)).Select(v => v!.Value).ToList();
        return values.Count == 0 ? null : values.Average();
    }

    private static void Trim<T>(ObservableCollection<T> items, int max)
    {
        while (items.Count > max)
        {
            items.RemoveAt(items.Count - 1);
        }
    }
}
