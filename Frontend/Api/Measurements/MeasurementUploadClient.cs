namespace Frontend.Api.Measurements;

/// <summary>Body of POST /api/measurements/upload (Backend/Models/Measurement/MeasurementUploadRequest.cs).</summary>
public sealed record MeasurementUploadRequest(MeasurementUploadInformation MeasurementInformation, IReadOnlyList<MeasurementUploadRecord> Records);

/// <summary>
/// Metadata of the uploaded measurement session. The user names are what this app declares; the backend stores them
/// as such and records a server-verified identity separately when its hosting provides one.
/// </summary>
public sealed record MeasurementUploadInformation(
    Guid SessionId,
    string? Drillhole,
    string? Project,
    string? Campaign,
    string? Operator,
    string? Notes,
    string? CreatedUsername,
    string UploadingUsername);

/// <summary>One measurement row as stored on the Measurement page. Count0 is the CPS (counts per second) value.</summary>
public sealed record MeasurementUploadRecord(
    int CaptureNumber,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    int SampleCount,
    decimal? DepthFrom,
    decimal? DepthTo,
    long? TsMs,
    double? Count0,
    double? Count0Raw,
    double? Hdm,
    double? HdmRaw,
    double? DoseRate);

/// <summary>
/// The stored upload. <see cref="AlreadyUploaded"/>: the same dataset was stored before (e.g. a retry after a timeout
/// whose first attempt had succeeded), so nothing was inserted again.
/// </summary>
public sealed record MeasurementUploadResponse(long UploadId, int RecordCount, bool AlreadyUploaded, DateTimeOffset UploadedAt);

public sealed class MeasurementUploadClient(ApiClient api)
{
    public const string Path = "/api/measurements/upload";

    public Task<ApiResult<MeasurementUploadResponse>> UploadAsync(MeasurementUploadRequest request, CancellationToken cancellationToken) =>
        api.PostAsync<MeasurementUploadResponse>(Path, request, cancellationToken);
}
