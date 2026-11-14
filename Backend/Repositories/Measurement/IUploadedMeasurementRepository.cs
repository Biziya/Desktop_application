using Backend.Models.Measurement;

namespace Backend.Repositories.Measurement;

public interface IUploadedMeasurementRepository
{
    /// <exception cref="UploadTablesMissingException" />
    Task<IReadOnlyList<UploadedDrillholeResponse>> GetDrillholesAsync(CancellationToken cancellationToken);

    /// <exception cref="UploadTablesMissingException" />
    Task<IReadOnlyList<UploadedRecordResponse>> GetRecordsAsync(string drillhole, int limit, CancellationToken cancellationToken);
}

/// <summary>The migration 001_CreateSppCt007.sql has not been run.</summary>
public sealed class UploadTablesMissingException(Exception inner)
    : Exception("The SPP CT007 database objects do not exist.", inner);
