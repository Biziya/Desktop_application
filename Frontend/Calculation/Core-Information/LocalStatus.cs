namespace Frontend.Calculation.CoreInformation;

/// <summary>Result of one local Device Status check on this laptop.</summary>
public enum LocalState
{
    Checking,
    Connected,
    Disconnected,
    Available,
    /// <summary>The expected file does not exist.</summary>
    Missing,
    /// <summary>Discovery found no file at all.</summary>
    NotFound,
    /// <summary>The file exists but cannot be opened or read (permissions, not SQLite, locked by the system).</summary>
    Inaccessible,
    /// <summary>The database is locked by another program right now; a later check usually succeeds.</summary>
    Busy,
    /// <summary>The check itself failed, so the real state is unknown.</summary>
    Unavailable
}

/// <param name="State">What was found.</param>
/// <param name="Text">Short status shown on the right of the row.</param>
/// <param name="Detail">Device name, reason or hint shown under the label.</param>
/// <param name="Path">Local path the status is about (databases); empty for the device.</param>
public sealed record LocalStatus(LocalState State, string Text, string Detail = "", string Path = "")
{
    public static LocalStatus Checking(string path = "") => new(LocalState.Checking, "Checking…", "", path);

    /// <summary>Retry makes sense for every result except a good one or a running check.</summary>
    public bool CanRetry => State is not (LocalState.Checking or LocalState.Connected or LocalState.Available);

    public bool HasPath => Path.Length > 0;
}
