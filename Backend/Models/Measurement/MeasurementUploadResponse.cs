namespace Backend.Models.Measurement;

/// <summary>
/// The stored upload. AlreadyUploaded: the same dataset (same fingerprint) was stored before, so nothing was inserted
/// again; UploadId, RecordCount and UploadedAt then describe that earlier upload.
/// </summary>
public sealed record MeasurementUploadResponse(long UploadId, int RecordCount, bool AlreadyUploaded, DateTimeOffset UploadedAt);
