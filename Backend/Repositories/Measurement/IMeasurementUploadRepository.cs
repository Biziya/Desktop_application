using Backend.Models.Measurement;

namespace Backend.Repositories.Measurement;

public interface IMeasurementUploadRepository
{
    /// <summary>
    /// Stores a validated upload (session and records in one transaction). A dataset with the same
    /// <paramref name="fingerprint"/> already stored is returned with AlreadyUploaded = true and nothing is inserted.
    /// Throws <see cref="MeasurementUploadConflictException"/> when some captures of the session were stored by an
    /// earlier, different upload; nothing is inserted then either.
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
