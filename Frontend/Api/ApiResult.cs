namespace Frontend.Api;

public enum ApiStatus
{
    /// <summary>The backend answered with the expected data.</summary>
    Ok,

    /// <summary>No answer: connection refused, network failure or timeout. The backend is offline.</summary>
    Unreachable,

    /// <summary>The backend answered with an HTTP error status. It is online; this endpoint failed.</summary>
    HttpError,

    /// <summary>The backend answered, but not with the expected JSON.</summary>
    InvalidResponse
}

/// <summary>Typed outcome of one request. <see cref="Value"/> is set only when <see cref="Status"/> is Ok.</summary>
public sealed record ApiResult<T>(ApiStatus Status, T? Value, string Error)
{
    public bool IsOk => Status == ApiStatus.Ok;

    public static ApiResult<T> Ok(T value) => new(ApiStatus.Ok, value, "");

    public static ApiResult<T> Failed(ApiStatus status, string error) => new(status, default, error);
}
