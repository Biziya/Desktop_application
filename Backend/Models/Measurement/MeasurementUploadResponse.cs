namespace Backend.Models.Measurement;

/// <summary>When AlreadyUploaded, nothing was inserted and the other fields describe the earlier upload.</summary>
public sealed record MeasurementUploadResponse(long UploadId, int RecordCount, bool AlreadyUploaded, DateTimeOffset UploadedAt);
