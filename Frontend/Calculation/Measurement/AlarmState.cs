namespace Frontend.Calculation.Measurement;

/// <summary>The CPS alarm bell beside Threshold on the Measurement page.</summary>
public enum AlarmState
{
    /// <summary>No valid threshold: nothing is checked.</summary>
    Off,

    /// <summary>A threshold is set and the sound is on.</summary>
    Armed,

    /// <summary>A threshold is set, the sound is muted.</summary>
    Muted,

    /// <summary>The last completed row was above the threshold.</summary>
    Triggered
}
