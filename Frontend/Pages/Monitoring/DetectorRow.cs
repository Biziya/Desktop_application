using System.Globalization;
using Frontend.Calculation.CoreInformation.GammaGuardDatabase;
using Frontend.Calculation.Measurement;
using Frontend.Shared.Infrastructure;

namespace Frontend.Pages.Monitoring;

/// <summary>
/// One detector on the Monitoring page: its detector_status row plus the newest reading seen for it. Rows are updated
/// in place on every poll, so the table keeps its scroll position and selection.
/// </summary>
public sealed class DetectorRow(string serial) : ObservableObject
{
    public const int ExpectedResponseTime = 1;

    private string? name;
    private string? mac;
    private string? firmware;
    private string? deviceSessionId;
    private string? state;
    private int? deviceType;
    private long? responseTime;
    private long lastSampleMs;
    private long updatedMs;
    private DeviceSample? latest;
    private string stateText = "Unknown";
    private StatusTone tone = StatusTone.Neutral;
    private string lastReadingAge = "–";
    private string heartbeatAge = "–";
    private string sampleStatus = "";
    private bool isSampling;

    public string Serial { get; } = serial;

    public string DisplayName => string.IsNullOrWhiteSpace(name) ? Serial : name!;

    public string? Name { get => name; private set { if (Set(ref name, value)) Changed(nameof(DisplayName)); } }

    public string? Mac { get => mac; private set => Set(ref mac, value); }

    public string? Firmware { get => firmware; private set => Set(ref firmware, value); }

    public string? DeviceSessionId { get => deviceSessionId; private set => Set(ref deviceSessionId, value); }

    /// <summary>The state as CT008 wrote it (logging, connected, disconnected, …).</summary>
    public string? State { get => state; private set => Set(ref state, value); }

    public string TypeName => DeviceTypes.Name(deviceType);

    public long? ResponseTime
    {
        get => responseTime;
        private set
        {
            if (Set(ref responseTime, value))
            {
                Changed(nameof(ResponseTimeText));
                Changed(nameof(HasResponseTimeWarning));
            }
        }
    }

    public string ResponseTimeText => responseTime switch
    {
        null => "–",
        var rt when deviceType == DeviceTypes.Manometer => $"{rt} s avg",
        0 => "Auto",
        var rt => $"{rt} s"
    };

    /// <summary>The response time is known and is not the 1 s the 5-second sample assumes.</summary>
    public bool HasResponseTimeWarning => responseTime is { } rt && rt != ExpectedResponseTime;

    /// <summary>Shown state: the CT008 state, or Stale / No data when the heartbeat or the readings stopped.</summary>
    public string StateText { get => stateText; private set => Set(ref stateText, value); }

    public StatusTone Tone { get => tone; private set => Set(ref tone, value); }

    public bool IsStale => StateText == "Stale";

    public bool IsLive => State != "disconnected" && !IsStale;

    public string LastReadingAge { get => lastReadingAge; private set => Set(ref lastReadingAge, value); }

    public string HeartbeatAge { get => heartbeatAge; private set => Set(ref heartbeatAge, value); }

    public DeviceSample? Latest
    {
        get => latest;
        private set
        {
            if (Set(ref latest, value))
            {
                Changed(nameof(LatestTime));
            }
        }
    }

    public string LatestTime => latest?.TsMs is { } ts ? MonitorFormat.Time(ts) : "–";

    /// <summary>Progress or result of this detector's 5-second sample; empty when none was taken.</summary>
    public string SampleStatus { get => sampleStatus; internal set => Set(ref sampleStatus, value); }

    public bool IsSampling
    {
        get => isSampling;
        internal set
        {
            if (Set(ref isSampling, value))
            {
                Changed(nameof(CanSample));
            }
        }
    }

    public bool CanSample => !isSampling;

    internal void Apply(DetectorStatusRow row)
    {
        Name = row.Name;
        Mac = row.Mac;
        Firmware = row.Firmware;
        DeviceSessionId = row.DeviceSessionId;
        State = row.State;
        updatedMs = row.UpdatedMs ?? 0;

        if (deviceType != row.DeviceType)
        {
            deviceType = row.DeviceType;
            Changed(nameof(TypeName));
            Changed(nameof(ResponseTimeText));
        }

        ResponseTime = row.ResponseTime;

        if (row.LastSampleMs is { } last && last > lastSampleMs)
        {
            lastSampleMs = last;
        }
    }

    internal void Apply(DeviceSample sample)
    {
        Latest = sample;

        if (sample.TsMs is { } ts && ts > lastSampleMs)
        {
            lastSampleMs = ts;
        }
    }

    /// <summary>
    /// Recomputes the ages and the shown state against <paramref name="nowMs"/>. The heartbeat (updated_ms) says whether
    /// the CT008 instance is alive; the newest reading says whether data is flowing.
    /// </summary>
    internal void Refresh(long nowMs, int staleAfterMs)
    {
        LastReadingAge = lastSampleMs > 0 ? MonitorFormat.Age(nowMs - lastSampleMs) : "–";
        HeartbeatAge = updatedMs > 0 ? MonitorFormat.Age(nowMs - updatedMs) : "–";

        var raw = State ?? "unknown";
        (StateText, Tone) = raw switch
        {
            "disconnected" => ("Disconnected", StatusTone.Neutral),
            _ when updatedMs > 0 && nowMs - updatedMs > staleAfterMs => ("Stale", StatusTone.Danger),
            "logging" when lastSampleMs > 0 && nowMs - lastSampleMs > staleAfterMs => ("No data", StatusTone.Warning),
            "logging" => ("Logging", StatusTone.Success),
            _ => (CultureInfo.InvariantCulture.TextInfo.ToTitleCase(raw), StatusTone.Info)
        };

        Changed(nameof(IsStale));
        Changed(nameof(IsLive));
    }
}

/// <summary>Time and age formatting shared by the Monitoring page.</summary>
public static class MonitorFormat
{
    public static string Time(long unixMs) =>
        DateTimeOffset.FromUnixTimeMilliseconds(unixMs).ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);

    public static string PreciseTime(long unixMs) =>
        DateTimeOffset.FromUnixTimeMilliseconds(unixMs).ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.CurrentCulture);

    public static string Age(long ms)
    {
        ms = Math.Max(0, ms);
        return ms switch
        {
            < 60_000 => $"{ms / 1000.0:0.0} s",
            < 3_600_000 => $"{ms / 60_000.0:0.0} min",
            _ => $"{ms / 3_600_000.0:0.0} h"
        };
    }
}
