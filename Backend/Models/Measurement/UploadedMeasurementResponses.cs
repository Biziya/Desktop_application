namespace Backend.Models.Measurement;

public sealed record UploadedDrillholeResponse(
    string HoleId,
    string ProjectCode,
    string Campaign,
    int UploadCount,
    int RecordCount,
    DateTimeOffset LastUploadedAt);

/// <summary>Count0 is CPS; device values may be null.</summary>
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
