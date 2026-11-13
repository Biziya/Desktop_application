using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace CT008Monitor
{
    public class StatusRecord
    {
        public string Serial, Name, Mac, Firmware, SessionId, State;
        public int DeviceType;
        public long ResponseTime = -1;   // -1 when the column is NULL (not reported yet)
        public long LastSampleMs, UpdatedMs;
    }

    public class SampleRecord
    {
        public long Id, Seq, TsMs;
        public string Serial, SessionId;
        public double Count0, Count0Raw, Hdm, HdmRaw, DoseRate, Temperature;
    }

    /// <summary>
    /// Read-only access to the CT008 live database. One connection, short autocommit reads, never a write.
    /// The CT008 instances keep the file in WAL mode, so these reads never block them and they never block us.
    /// </summary>
    public class DatabaseReader : IDisposable
    {
        private SQLiteConnection conn;

        public string Path { get; private set; }
        public bool IsOpen { get { return conn != null; } }

        public void Open(string path)
        {
            Close();
            var builder = new SQLiteConnectionStringBuilder();
            builder.DataSource = path;
            builder.Version = 3;
            builder.Pooling = false;
            builder.FailIfMissing = true;   // Never create an empty database at a wrong path

            var c = new SQLiteConnection(builder.ToString());
            c.Open();
            conn = c;
            Path = path;

            // Wait briefly if a writer is mid-transaction instead of failing the poll
            Exec("PRAGMA busy_timeout=2000;");
        }

        public void Close()
        {
            if (conn == null) return;
            try { conn.Close(); } catch { }
            conn.Dispose();
            conn = null;
        }

        public void Dispose()
        {
            Close();
        }

        public string JournalMode()
        {
            return Convert.ToString(Scalar("PRAGMA journal_mode;"));
        }

        public int SchemaVersion()
        {
            return Convert.ToInt32(Scalar("PRAGMA user_version;"));
        }

        public long MaxSampleId()
        {
            return Convert.ToInt64(Scalar("SELECT COALESCE(MAX(id), 0) FROM samples;"));
        }

        public List<StatusRecord> ReadStatus()
        {
            var list = new List<StatusRecord>();
            using (var cmd = new SQLiteCommand(
                "SELECT serial, name, mac, firmware, device_type, session_id, state, response_time, last_sample_ms, updated_ms " +
                "FROM detector_status ORDER BY serial;", conn))
            using (SQLiteDataReader r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    list.Add(new StatusRecord
                    {
                        Serial = Str(r, 0),
                        Name = Str(r, 1),
                        Mac = Str(r, 2),
                        Firmware = Str(r, 3),
                        DeviceType = (int)Int(r, 4),
                        SessionId = Str(r, 5),
                        State = Str(r, 6),
                        ResponseTime = r.IsDBNull(7) ? -1 : r.GetInt64(7),
                        LastSampleMs = Int(r, 8),
                        UpdatedMs = Int(r, 9),
                    });
                }
            }
            return list;
        }

        // The pressure column is left out: the monitor is used with counters only
        private const string SampleColumns =
            "id, serial, session_id, seq, ts_ms, count0, count0_raw, hdm, hdm_raw, dose_rate, temperature";

        /// <summary>
        /// The reader's cursor: everything committed after the last id it processed, in commit order.
        /// Ids are AUTOINCREMENT and every CT008 instance commits through SQLite's single write lock,
        /// so nothing can appear later with a smaller id.
        /// </summary>
        public List<SampleRecord> ReadSamplesAfter(long id, int limit)
        {
            var list = new List<SampleRecord>();
            using (var cmd = new SQLiteCommand(
                "SELECT " + SampleColumns + " FROM samples WHERE id > @id ORDER BY id LIMIT @limit;", conn))
            {
                cmd.Parameters.AddWithValue("@id", id);
                cmd.Parameters.AddWithValue("@limit", limit);
                using (SQLiteDataReader r = cmd.ExecuteReader())
                {
                    while (r.Read()) list.Add(ReadSample(r));
                }
            }
            return list;
        }

        /// <summary>
        /// Newest sample of every detector, used once at startup so rows are populated before new data arrives.
        /// </summary>
        public List<SampleRecord> ReadLatestPerDetector()
        {
            var list = new List<SampleRecord>();
            using (var cmd = new SQLiteCommand(
                "SELECT " + SampleColumns + " FROM samples WHERE id IN (SELECT MAX(id) FROM samples GROUP BY serial);", conn))
            using (SQLiteDataReader r = cmd.ExecuteReader())
            {
                while (r.Read()) list.Add(ReadSample(r));
            }
            return list;
        }

        private static SampleRecord ReadSample(SQLiteDataReader r)
        {
            return new SampleRecord
            {
                Id = Int(r, 0),
                Serial = Str(r, 1),
                SessionId = Str(r, 2),
                Seq = Int(r, 3),
                TsMs = Int(r, 4),
                Count0 = Dbl(r, 5),
                Count0Raw = Dbl(r, 6),
                Hdm = Dbl(r, 7),
                HdmRaw = Dbl(r, 8),
                DoseRate = Dbl(r, 9),
                Temperature = Dbl(r, 10),
            };
        }

        private static string Str(SQLiteDataReader r, int i) { return r.IsDBNull(i) ? null : r.GetString(i); }
        private static long Int(SQLiteDataReader r, int i) { return r.IsDBNull(i) ? 0 : r.GetInt64(i); }
        private static double Dbl(SQLiteDataReader r, int i) { return r.IsDBNull(i) ? 0 : r.GetDouble(i); }

        private void Exec(string sql)
        {
            using (var cmd = new SQLiteCommand(sql, conn)) cmd.ExecuteNonQuery();
        }

        private object Scalar(string sql)
        {
            using (var cmd = new SQLiteCommand(sql, conn)) return cmd.ExecuteScalar();
        }
    }
}
