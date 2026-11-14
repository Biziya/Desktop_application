namespace Frontend.Calculation.Measurement;

/// <summary>The CPS alarm bell beside Threshold on the Measurement page.</summary>
public enum AlarmState
{
    /// <summary>No valid threshold: nothing is checked.</summary>
    Off,

    Armed,

    Muted,

    /// <summary>The last completed row was above the threshold.</summary>
    Triggered
}
