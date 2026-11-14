using Backend.Models.Measurement;

namespace Backend.Repositories.Measurement;

public interface IMeasurementUploadRepository
{
    /// <summary>
    /// Stores a validated upload in one transaction; a known fingerprint returns AlreadyUploaded = true.
    /// Throws <see cref="MeasurementUploadConflictException"/> when a different upload already stored some captures.
    /// </summary>
    Task<MeasurementUploadResponse> SaveAsync(
        MeasurementUploadRequest request, string fingerprint, string? verifiedUploader, CancellationToken cancellationToken);
}

/// <summary>Captures (Capture No) of this session that an earlier upload already stored.</summary>
public sealed class MeasurementUploadConflictException(IReadOnlyList<int> captureNumbers)
    : Exception($"{captureNumbers.Count} measurement(s) were already uploaded.")
{
    public IReadOnlyList<int> CaptureNumbers { get; } = captureNumbers;
}
