using Backend.Models.Measurement;
using Backend.Repositories.Measurement;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Backend.Controllers.Measurement;

/// <summary>Data Viewer page: the measurements stored by POST api/measurements/upload, read-only.</summary>
[ApiController]
[Tags("Measurement")]
[Route("api/measurements/uploaded")]
public sealed class UploadedMeasurementController(IUploadedMeasurementRepository repository, ILogger<UploadedMeasurementController> logger)
    : ControllerBase
{
    /// <summary>Most rows one request returns (the same as the most one upload may contain).</summary>
    public const int MaxRecords = 10_000;

    public const string TablesMissingMessage =
        "The upload tables have not been created on the central database yet (Database/Migrations/001_CreateMeasurementUploadTables.sql).";

    public const string UnavailableMessage = "The measurement database is unavailable. Try again later.";

    [HttpGet("drillholes")]
    [ProducesResponseType<IReadOnlyList<UploadedDrillholeResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public Task<ActionResult<IReadOnlyList<UploadedDrillholeResponse>>> GetDrillholes(CancellationToken cancellationToken) =>
        Read(() => repository.GetDrillholesAsync(cancellationToken));

    [HttpGet("records")]
    [ProducesResponseType<IReadOnlyList<UploadedRecordResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<IReadOnlyList<UploadedRecordResponse>>> GetRecords([FromQuery] string? drillhole, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(drillhole) || drillhole.Length > 100)
        {
            return Problem("drillhole is required (at most 100 characters).", statusCode: StatusCodes.Status400BadRequest);
        }

        return await Read(() => repository.GetRecordsAsync(drillhole.Trim(), MaxRecords, cancellationToken));
    }

    private async Task<ActionResult<IReadOnlyList<T>>> Read<T>(Func<Task<IReadOnlyList<T>>> query)
    {
        try
        {
            return Ok(await query());
        }
        catch (UploadTablesMissingException error)
        {
            logger.LogWarning(error, "Measurement upload tables are missing; run Database/Migrations/001.");
            return Problem(TablesMissingMessage, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (SqlException error)
        {
            logger.LogError(error, "Failed to read uploaded measurements from SQL Server.");
            return Problem(UnavailableMessage, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
