using Backend.Models.Measurement;

namespace Backend.Repositories.Measurement;

/// <summary>Reads what the Upload Data page stored (Data Viewer page). Never writes.</summary>
public interface IUploadedMeasurementRepository
{
    /// <summary>Drillholes with at least one upload, by Hole ID.</summary>
    /// <exception cref="UploadTablesMissingException">The upload tables have not been created yet.</exception>
    Task<IReadOnlyList<UploadedDrillholeResponse>> GetDrillholesAsync(CancellationToken cancellationToken);

    /// <summary>Every uploaded row of the drillhole, by depth (at most <paramref name="limit"/>).</summary>
    /// <exception cref="UploadTablesMissingException">The upload tables have not been created yet.</exception>
    Task<IReadOnlyList<UploadedRecordResponse>> GetRecordsAsync(string drillhole, int limit, CancellationToken cancellationToken);
}

/// <summary>dbo.CT007_MEASUREMENT_UPLOAD(_RECORD) do not exist: Database/Migrations/001 has not been run.</summary>
public sealed class UploadTablesMissingException(Exception inner)
    : Exception("The measurement upload tables do not exist.", inner);
