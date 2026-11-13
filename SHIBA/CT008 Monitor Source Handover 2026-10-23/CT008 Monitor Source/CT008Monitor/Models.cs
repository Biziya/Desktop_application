using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CT008Monitor
{
    public class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            return true;
        }
    }

    /// <summary>
    /// Device type numbers as reported by the detectors, named the same way the CT008 app names them.
    /// </summary>
    public static class DeviceTypes
    {
        private static readonly Dictionary<int, string> names = new Dictionary<int, string>
        {
            { 3, "Manometer" },
            { 10, "Contamination Detector" },
            { 14, "MicroR Meter" },
            { 15, "Nano Nuke" },
            { 16, "4M Meter" },
            { 19, "Neutron Detector" },
            { 20, "DRD" },
        };

        public static string Name(int type)
        {
            string name;
            return names.TryGetValue(type, out name) ? name : "Type " + type;
        }
    }

    /// <summary>
    /// One row of the detectors grid: the detector_status row plus the newest sample seen for that detector.
    /// Updated in place so the grid does not rebuild rows on every poll.
    /// </summary>
    public class DetectorRow : ObservableObject
    {
        private string serial, name, mac, firmware, typeName, state, displayState, sessionId;
        private string sampleTime, lastSampleAge, heartbeatAge;
        private long lastSampleMs, updatedMs;
        private long? seq;
        private double? cpm, cpmRaw, hdm, hdmRaw, doseRate, temperature;
        private int deviceType;
        private int? responseTime;
        private string responseTimeText = "--";
        private bool responseTimeWarning = false;
        private bool canAggregate = true;
        private string aggregateText = "";

        public int DeviceType { get { return deviceType; } set { Set(ref deviceType, value); } }
        public int? ResponseTime { get { return responseTime; } set { Set(ref responseTime, value); } }
        public string ResponseTimeText { get { return responseTimeText; } set { Set(ref responseTimeText, value); } }
        public bool ResponseTimeWarning { get { return responseTimeWarning; } set { Set(ref responseTimeWarning, value); } }
        public bool CanAggregate { get { return canAggregate; } set { Set(ref canAggregate, value); } }
        public string AggregateText { get { return aggregateText; } set { Set(ref aggregateText, value); } }

        public string Serial { get { return serial; } set { Set(ref serial, value); } }
        public string Name { get { return name; } set { Set(ref name, value); } }
        public string Mac { get { return mac; } set { Set(ref mac, value); } }
        public string Firmware { get { return firmware; } set { Set(ref firmware, value); } }
        public string TypeName { get { return typeName; } set { Set(ref typeName, value); } }
        public string State { get { return state; } set { Set(ref state, value); } }
        public string DisplayState { get { return displayState; } set { Set(ref displayState, value); } }
        public string SessionId { get { return sessionId; } set { Set(ref sessionId, value); } }
        public long LastSampleMs { get { return lastSampleMs; } set { Set(ref lastSampleMs, value); } }
        public long UpdatedMs { get { return updatedMs; } set { Set(ref updatedMs, value); } }

        public string SampleTime { get { return sampleTime; } set { Set(ref sampleTime, value); } }
        public string LastSampleAge { get { return lastSampleAge; } set { Set(ref lastSampleAge, value); } }
        public string HeartbeatAge { get { return heartbeatAge; } set { Set(ref heartbeatAge, value); } }
        public long? Seq { get { return seq; } set { Set(ref seq, value); } }
        public double? Cpm { get { return cpm; } set { Set(ref cpm, value); } }
        public double? CpmRaw { get { return cpmRaw; } set { Set(ref cpmRaw, value); } }
        public double? Hdm { get { return hdm; } set { Set(ref hdm, value); } }
        public double? HdmRaw { get { return hdmRaw; } set { Set(ref hdmRaw, value); } }
        public double? DoseRate { get { return doseRate; } set { Set(ref doseRate, value); } }
        public double? Temperature { get { return temperature; } set { Set(ref temperature, value); } }

        /// <summary>
        /// Recomputes the age columns and the displayed state against the current clock.
        /// The heartbeat tells whether the CT008 instance is alive; the sample age tells whether data is flowing.
        /// </summary>
        public void RefreshAges(long nowMs, int staleAfterMs)
        {
            LastSampleAge = LastSampleMs > 0 ? Age(nowMs - LastSampleMs) : "--";
            HeartbeatAge = UpdatedMs > 0 ? Age(nowMs - UpdatedMs) : "--";

            string shown = State ?? "unknown";
            if (shown != "disconnected" && UpdatedMs > 0 && nowMs - UpdatedMs > staleAfterMs)
            {
                shown = "stale";
            }
            else if (shown == "logging" && LastSampleMs > 0 && nowMs - LastSampleMs > staleAfterMs)
            {
                shown = "logging (no data)";
            }
            DisplayState = shown;
        }

        public static string Age(long ms)
        {
            if (ms < 0) ms = 0;
            if (ms < 60000) return (ms / 1000.0).ToString("0.0") + " s";
            if (ms < 3600000) return (ms / 60000.0).ToString("0.0") + " min";
            return (ms / 3600000.0).ToString("0.0") + " h";
        }
    }

    /// <summary>
    /// One completed 5-second sample: the first reading after the click is discarded, the next 5 are averaged.
    /// </summary>
    public class AggregateResult
    {
        public string Serial { get; set; }
        public string Window { get; set; }       // Coverage: discarded reading's time to last kept reading's time
        public double? SpanSeconds { get; set; } // Coverage in seconds, about 5.0 when complete
        public int Samples { get; set; }
        public int Expected { get; set; }
        public bool Complete { get; set; }
        public double? Cpm { get; set; }
        public double? CpmRaw { get; set; }
        public double? Hdm { get; set; }
        public double? DoseRate { get; set; }
        public double? Temperature { get; set; }
    }

    /// <summary>
    /// One row of the recent samples grid.
    /// </summary>
    public class SampleRow
    {
        public long Id { get; set; }
        public string Time { get; set; }
        public string Serial { get; set; }
        public long Seq { get; set; }
        public double Cpm { get; set; }
        public double CpmRaw { get; set; }
        public double Hdm { get; set; }
        public double DoseRate { get; set; }
        public double Temperature { get; set; }
        public string SessionId { get; set; }
    }
}
