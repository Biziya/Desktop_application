namespace Frontend.Calculation.CoreInformation;

public enum LocalState
{
    Checking,
    NotSelected,
    Connected,
    Disconnected,
    Available,
    /// <summary>The expected file does not exist.</summary>
    Missing,
    /// <summary>Discovery found no file at all.</summary>
    NotFound,
    /// <summary>The file exists but cannot be opened or read.</summary>
    Inaccessible,
    /// <summary>Locked by another program right now; a later check usually succeeds.</summary>
    Busy,
    /// <summary>The check itself failed, so the real state is unknown.</summary>
    Unavailable
}

/// <param name="Text">Short status on the right of the row.</param>
/// <param name="Detail">Device name, reason or hint under the label.</param>
public sealed record LocalStatus(LocalState State, string Text, string Detail = "", string Path = "")
{
    public static LocalStatus Checking(string path = "") => new(LocalState.Checking, "Checking…", "", path);

    public bool CanRetry => State is not (LocalState.Checking or LocalState.NotSelected or LocalState.Connected or LocalState.Available);

    public bool HasPath => Path.Length > 0;
}
