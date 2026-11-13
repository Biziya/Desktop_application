using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Configuration;
using System.IO;
using System.Linq;

namespace CT008Monitor
{
    /// <summary>
    /// Polls the database and keeps the grids current. Everything runs on the UI thread from a timer;
    /// each poll is a handful of indexed reads and takes milliseconds.
    /// </summary>
    public class MonitorViewModel : ObservableObject, IDisposable
    {
        private const int RecentSamplesShown = 50;
        private const int AggregationsShown = 100;
        private const int MaxSamplesPerPoll = 5000;
        public const int ReadingsPerSample = 5;
        // Give up on a sample after this long, for instance when logging stopped (a full sample takes about 6 s)
        private const int SampleTimeoutMs = 12000;
        public const int ExpectedResponseTime = 1;

        // A 5-reading sample in progress for one detector. Readings are counted, not timed, because the
        // detector delivers roughly one per second with jitter, so a fixed 5 s window can hold 4 or 6.
        // The first reading after the click is discarded: with a 1 s response time it integrates the second
        // before its timestamp, which began before the click. The next 5 readings then cover exactly the
        // 5 seconds from the discarded reading's timestamp to the last reading's timestamp.
        private class SampleJob
        {
            public string Serial;
            public long StartMs;
            public SampleRecord Anchor;      // The discarded first reading; the sample's coverage starts at its timestamp
            public List<SampleRecord> Readings = new List<SampleRecord>();
        }

        private readonly DatabaseReader reader = new DatabaseReader();
        private readonly Dictionary<string, DetectorRow> rowsBySerial = new Dictionary<string, DetectorRow>();
        private readonly Dictionary<string, SampleJob> jobs = new Dictionary<string, SampleJob>();
        private readonly int staleAfterMs;
        private long cursor = 0;
        private long rowsRead = 0;
        private long rateWindowStartMs = 0;
        private long rateWindowRows = 0;
        private double rowsPerSecond = 0;

        private string databasePath;
        private string connectionText = "Not connected";
        private string statusText = "";
        private string warningText = "";

        public ObservableCollection<DetectorRow> Detectors { get; private set; }
        public ObservableCollection<SampleRow> RecentSamples { get; private set; }
        public ObservableCollection<AggregateResult> Aggregations { get; private set; }

        public int PollIntervalMs { get; private set; }

        public string DatabasePath
        {
            get { return databasePath; }
            set
            {
                if (Set(ref databasePath, value)) SettingsStore.SaveDatabasePath(value);
            }
        }

        public string ConnectionText { get { return connectionText; } set { Set(ref connectionText, value); } }
        public string StatusText { get { return statusText; } set { Set(ref statusText, value); } }
        public string WarningText { get { return warningText; } set { Set(ref warningText, value); } }

        public MonitorViewModel()
        {
            Detectors = new ObservableCollection<DetectorRow>();
            RecentSamples = new ObservableCollection<SampleRow>();
            Aggregations = new ObservableCollection<AggregateResult>();
            PollIntervalMs = ReadIntSetting("PollIntervalMs", 1000);
            staleAfterMs = ReadIntSetting("StaleAfterMs", 5000);
            databasePath = SettingsStore.LoadDatabasePath() ?? DefaultDatabasePath();
        }

        public static long NowMs()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        /// <summary>
        /// Drops the connection and all rows, then reads everything again from the current path.
        /// Completed samples are kept.
        /// </summary>
        public void Reconnect()
        {
            reader.Close();
            rowsBySerial.Clear();
            Detectors.Clear();
            RecentSamples.Clear();
            jobs.Clear();
            cursor = 0;
            rowsRead = 0;
            rateWindowRows = 0;
            rowsPerSecond = 0;
            ConnectionText = "Not connected";
            WarningText = "";
            Poll();
        }

        /// <summary>
        /// Starts a 5-second sample for one detector: the first reading after the click is discarded,
        /// the next 5 are averaged.
        /// </summary>
        public void StartAggregation(string serial)
        {
            if (string.IsNullOrEmpty(serial) || jobs.ContainsKey(serial)) return;
            jobs[serial] = new SampleJob { Serial = serial, StartMs = NowMs() };
            DetectorRow row = RowFor(serial);
            row.CanAggregate = false;
            row.AggregateText = "waiting for first reading";
        }

        public void Poll()
        {
            long now = NowMs();
            try
            {
                if (!reader.IsOpen)
                {
                    if (!File.Exists(DatabasePath))
                    {
                        ConnectionText = "Waiting for database: " + DatabasePath;
                        return;
                    }
                    reader.Open(DatabasePath);
                    cursor = reader.MaxSampleId();
                    ConnectionText = "Open, journal " + reader.JournalMode() + ", schema v" + reader.SchemaVersion();

                    // Populate the latest data columns before any new sample arrives
                    foreach (SampleRecord s in reader.ReadLatestPerDetector())
                    {
                        ApplySample(s);
                    }
                    rateWindowStartMs = now;
                    rateWindowRows = 0;
                }

                foreach (StatusRecord st in reader.ReadStatus())
                {
                    ApplyStatus(st);
                }

                // A database that was deleted and recreated starts its ids again at 1; follow it rather than
                // waiting for the new ids to climb past the old cursor
                long maxId = reader.MaxSampleId();
                if (maxId < cursor)
                {
                    cursor = maxId;
                    jobs.Clear();
                }

                List<SampleRecord> fresh = reader.ReadSamplesAfter(cursor, MaxSamplesPerPoll);
                foreach (SampleRecord s in fresh)
                {
                    ApplySample(s);
                    AddRecent(s);
                    cursor = s.Id;

                    SampleJob job;
                    if (jobs.TryGetValue(s.Serial ?? "", out job) && s.TsMs >= job.StartMs)
                    {
                        if (job.Anchor == null)
                        {
                            job.Anchor = s;   // Discarded: its integration period started before the click
                        }
                        else if (job.Readings.Count < ReadingsPerSample)
                        {
                            job.Readings.Add(s);
                        }
                    }
                }
                rowsRead += fresh.Count;
                rateWindowRows += fresh.Count;
                if (now - rateWindowStartMs >= 5000)
                {
                    rowsPerSecond = rateWindowRows * 1000.0 / Math.Max(1, now - rateWindowStartMs);
                    rateWindowStartMs = now;
                    rateWindowRows = 0;
                }

                foreach (DetectorRow row in Detectors)
                {
                    row.RefreshAges(now, staleAfterMs);
                }

                StatusText = string.Format("Last poll {0:HH:mm:ss}    cursor id {1}    rows read {2}    {3:0.0} rows/s    {4} detector(s)",
                    DateTime.Now, cursor, rowsRead, rowsPerSecond, Detectors.Count);
            }
            catch (Exception e)
            {
                // A missing table, a locked file, or a database that moved: show it and reopen on the next poll
                ConnectionText = "Error: " + e.Message + " (retrying)";
                reader.Close();
            }

            UpdateSampleJobs(now);
            UpdateWarning();
        }

        private void UpdateSampleJobs(long now)
        {
            if (jobs.Count == 0) return;
            foreach (SampleJob job in jobs.Values.ToList())
            {
                DetectorRow row = RowFor(job.Serial);
                if (job.Readings.Count >= ReadingsPerSample)
                {
                    FinishSample(job, row, true);
                }
                else if (now - job.StartMs >= SampleTimeoutMs)
                {
                    FinishSample(job, row, false);
                }
                else
                {
                    row.AggregateText = job.Anchor == null
                        ? "waiting for first reading"
                        : string.Format("collecting {0} of {1} readings", job.Readings.Count, ReadingsPerSample);
                }
            }
        }

        private void FinishSample(SampleJob job, DetectorRow row, bool complete)
        {
            jobs.Remove(job.Serial);
            int n = job.Readings.Count;
            var result = new AggregateResult
            {
                Serial = job.Serial,
                Samples = n,
                Expected = ReadingsPerSample,
                Complete = complete,
            };

            if (n > 0)
            {
                // Coverage runs from the discarded reading's timestamp (where the first kept reading's second begins)
                // to the last kept reading's timestamp: about 5.0 s for a complete sample
                long fromMs = job.Anchor != null ? job.Anchor.TsMs : job.Readings[0].TsMs;
                SampleRecord last = job.Readings[n - 1];
                result.Window = LocalTime(fromMs) + " to " + LocalTime(last.TsMs);
                result.SpanSeconds = (last.TsMs - fromMs) / 1000.0;
                result.Cpm = job.Readings.Average(s => s.Count0);
                result.CpmRaw = job.Readings.Average(s => s.Count0Raw);
                result.Hdm = job.Readings.Average(s => s.Hdm);
                result.DoseRate = job.Readings.Average(s => s.DoseRate);
                result.Temperature = job.Readings.Average(s => s.Temperature);
                row.AggregateText = complete
                    ? string.Format("{0} readings, mean {1:0} CPM", n, result.Cpm)
                    : string.Format("only {0} of {1} readings, mean {2:0} CPM", n, ReadingsPerSample, result.Cpm);
            }
            else
            {
                result.Window = "--";
                row.AggregateText = "no readings within " + (SampleTimeoutMs / 1000) + " s";
            }

            Aggregations.Insert(0, result);
            while (Aggregations.Count > AggregationsShown)
            {
                Aggregations.RemoveAt(Aggregations.Count - 1);
            }
            row.CanAggregate = true;
        }

        // Warn about any live detector whose response time is known and not the expected 1 s
        private void UpdateWarning()
        {
            var bad = new List<string>();
            foreach (DetectorRow row in Detectors)
            {
                if (!row.ResponseTimeWarning) continue;
                if (row.State == "disconnected" || row.DisplayState == "stale") continue;
                bad.Add(row.Serial + " is " + row.ResponseTimeText);
            }
            WarningText = bad.Count == 0
                ? ""
                : "Warning: response time should be " + ExpectedResponseTime + " s for 5 second samples. " + string.Join("; ", bad) + ".";
        }

        private DetectorRow RowFor(string serial)
        {
            DetectorRow row;
            if (!rowsBySerial.TryGetValue(serial, out row))
            {
                row = new DetectorRow { Serial = serial, State = "unknown", DisplayState = "unknown" };
                rowsBySerial[serial] = row;
                Detectors.Add(row);
            }
            return row;
        }

        private void ApplyStatus(StatusRecord st)
        {
            if (string.IsNullOrEmpty(st.Serial)) return;
            DetectorRow row = RowFor(st.Serial);
            row.Name = st.Name;
            row.Mac = st.Mac;
            row.Firmware = st.Firmware;
            row.DeviceType = st.DeviceType;
            row.TypeName = DeviceTypes.Name(st.DeviceType);
            row.State = st.State;
            row.SessionId = st.SessionId;
            row.UpdatedMs = st.UpdatedMs;
            if (st.LastSampleMs > row.LastSampleMs) row.LastSampleMs = st.LastSampleMs;

            if (st.ResponseTime < 0)
            {
                row.ResponseTime = null;
                row.ResponseTimeText = "--";
                row.ResponseTimeWarning = false;
            }
            else
            {
                int rt = (int)st.ResponseTime;
                row.ResponseTime = rt;
                row.ResponseTimeText = st.DeviceType == 3 ? rt + " s avg" : (rt == 0 ? "Auto" : rt + " s");
                row.ResponseTimeWarning = rt != ExpectedResponseTime;
            }
        }

        private void ApplySample(SampleRecord s)
        {
            if (string.IsNullOrEmpty(s.Serial)) return;
            DetectorRow row = RowFor(s.Serial);
            row.Seq = s.Seq;
            row.SampleTime = LocalTime(s.TsMs);
            row.Cpm = s.Count0;
            row.CpmRaw = s.Count0Raw;
            row.Hdm = s.Hdm;
            row.HdmRaw = s.HdmRaw;
            row.DoseRate = s.DoseRate;
            row.Temperature = s.Temperature;
            if (s.TsMs > row.LastSampleMs) row.LastSampleMs = s.TsMs;
        }

        private void AddRecent(SampleRecord s)
        {
            RecentSamples.Insert(0, new SampleRow
            {
                Id = s.Id,
                Time = LocalTime(s.TsMs),
                Serial = s.Serial,
                Seq = s.Seq,
                Cpm = s.Count0,
                CpmRaw = s.Count0Raw,
                Hdm = s.Hdm,
                DoseRate = s.DoseRate,
                Temperature = s.Temperature,
                SessionId = s.SessionId,
            });
            while (RecentSamples.Count > RecentSamplesShown)
            {
                RecentSamples.RemoveAt(RecentSamples.Count - 1);
            }
        }

        public static string LocalTime(long ms)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime().ToString("HH:mm:ss.fff");
        }

        private static string DefaultDatabasePath()
        {
            string configured = ConfigurationManager.AppSettings["DefaultDatabasePath"];
            if (string.IsNullOrWhiteSpace(configured)) configured = "ct008-log.sqlite";
            configured = Environment.ExpandEnvironmentVariables(configured.Trim());
            if (Path.IsPathRooted(configured)) return configured;
            return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configured));
        }

        private static int ReadIntSetting(string key, int defaultValue)
        {
            int value;
            string text = ConfigurationManager.AppSettings[key];
            return text != null && int.TryParse(text.Trim(), out value) && value > 0 ? value : defaultValue;
        }

        public void Dispose()
        {
            reader.Dispose();
        }
    }

    /// <summary>
    /// Remembers the last database path per Windows user, so the monitor reopens the same file next time.
    /// </summary>
    public static class SettingsStore
    {
        private static readonly string file = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CT008Monitor", "database-path.txt");

        public static string LoadDatabasePath()
        {
            try
            {
                if (File.Exists(file))
                {
                    string path = File.ReadAllText(file).Trim();
                    if (path.Length > 0) return path;
                }
            }
            catch { }
            return null;
        }

        public static void SaveDatabasePath(string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllText(file, path ?? "");
            }
            catch { }
        }
    }
}
