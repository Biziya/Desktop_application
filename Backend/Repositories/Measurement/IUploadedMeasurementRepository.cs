using Backend.Models.Measurement;

namespace Backend.Repositories.Measurement;

/// <summary>Reads what the Upload Data page stored (Data Viewer page). Never writes.</summary>
public interface IUploadedMeasurementRepository
{
    /// <summary>Drillholes with at least one upload, by Hole ID.</summary>
    /// <exception cref="UploadTablesMissingException">The SPP CT007 table has not been created yet.</exception>
    Task<IReadOnlyList<UploadedDrillholeResponse>> GetDrillholesAsync(CancellationToken cancellationToken);

    /// <summary>Every uploaded row of the drillhole, by depth (at most <paramref name="limit"/>).</summary>
    /// <exception cref="UploadTablesMissingException">The SPP CT007 table has not been created yet.</exception>
    Task<IReadOnlyList<UploadedRecordResponse>> GetRecordsAsync(string drillhole, int limit, CancellationToken cancellationToken);
}

/// <summary>The SPP CT007 table, a procedure or the record type does not exist: Database/Migrations/001_CreateSppCt007.sql has not been run.</summary>
public sealed class UploadTablesMissingException(Exception inner)
    : Exception("The SPP CT007 database objects do not exist.", inner);
