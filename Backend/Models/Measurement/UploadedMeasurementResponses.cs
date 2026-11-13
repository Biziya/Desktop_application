namespace Backend.Models.Measurement;

/// <summary>A drillhole with uploaded measurements, for the Data Viewer's drillhole list.</summary>
public sealed record UploadedDrillholeResponse(
    string HoleId,
    string ProjectCode,
    string Campaign,
    int UploadCount,
    int RecordCount,
    DateTimeOffset LastUploadedAt);

/// <summary>
/// One uploaded measurement row (dbo.SPPCT007_BEK) with the upload it came from. Count0 is CPS, as on
/// the Measurement page; device values may be null.
/// </summary>
public sealed record UploadedRecordResponse(
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
    string Operator,
    DateTimeOffset UploadedAt);
