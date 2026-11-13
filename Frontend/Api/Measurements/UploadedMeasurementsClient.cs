namespace Frontend.Api.Measurements;

/// <summary>A drillhole with uploaded measurements (GET api/measurements/uploaded/drillholes).</summary>
public sealed record UploadedDrillhole(
    string HoleId,
    string? ProjectCode,
    string? Campaign,
    int UploadCount,
    int RecordCount,
    DateTimeOffset LastUploadedAt)
{
    /// <summary>"Project · Campaign" under the Hole ID in the drillhole list.</summary>
    public string Details => string.Join(" · ", new[] { ProjectCode, Campaign }.Where(v => !string.IsNullOrWhiteSpace(v)));

    public override string ToString() => HoleId;
}

/// <summary>One uploaded measurement row (GET api/measurements/uploaded/records). Count0 is CPS.</summary>
public sealed record UploadedRecord(
    Guid SessionId,
    int CaptureNumber,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    int SampleCount,
    decimal DepthFrom,
    decimal DepthTo,
    long? TsMs,
    double? Count0,
    double? Count0Raw,
    double? Hdm,
    double? HdmRaw,
    double? DoseRate,
    long UploadId,
    string? Operator,
    DateTimeOffset UploadedAt);

/// <summary>Reads the measurements stored in the central database (Data Viewer page). Never writes.</summary>
public sealed class UploadedMeasurementsClient(ApiClient api)
{
    public const string DrillholesPath = "/api/measurements/uploaded/drillholes";
    public const string RecordsPath = "/api/measurements/uploaded/records";

    // The backend's problem text ("tables not created yet", "database unavailable") is written for users.
    public Task<ApiResult<IReadOnlyList<UploadedDrillhole>>> GetDrillholesAsync(CancellationToken cancellationToken) =>
        api.GetAsync<IReadOnlyList<UploadedDrillhole>>(DrillholesPath, cancellationToken, showProblemDetail: true);

    public Task<ApiResult<IReadOnlyList<UploadedRecord>>> GetRecordsAsync(string drillhole, CancellationToken cancellationToken) =>
        api.GetAsync<IReadOnlyList<UploadedRecord>>($"{RecordsPath}?drillhole={Uri.EscapeDataString(drillhole)}", cancellationToken,
            showProblemDetail: true);
}
