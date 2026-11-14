namespace Frontend.Api.Measurements;

/// <summary>Body of POST /api/measurements/upload (Backend/Models/Measurement/MeasurementUploadRequest.cs).</summary>
public sealed record MeasurementUploadRequest(MeasurementUploadInformation MeasurementInformation, IReadOnlyList<MeasurementUploadRecord> Records);

// User names are self-declared; the backend records a verified identity separately when available.
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

/// <summary><see cref="AlreadyUploaded"/>: the same dataset was stored before (e.g. a retry after a timeout), nothing was inserted.</summary>
public sealed record MeasurementUploadResponse(long UploadId, int RecordCount, bool AlreadyUploaded, DateTimeOffset UploadedAt);

public sealed class MeasurementUploadClient(ApiClient api)
{
    public const string Path = "/api/measurements/upload";

    public Task<ApiResult<MeasurementUploadResponse>> UploadAsync(MeasurementUploadRequest request, CancellationToken cancellationToken) =>
        api.PostAsync<MeasurementUploadResponse>(Path, request, cancellationToken);
}
