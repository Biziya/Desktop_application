using Backend.Models.Measurement;
using Backend.Repositories.Measurement;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Backend.Controllers.Measurement;

[ApiController]
[Tags("Measurement")]
[Route("api/measurements")]
public sealed class MeasurementUploadController(IMeasurementUploadRepository repository, ILogger<MeasurementUploadController> logger) : ControllerBase
{
    // Idempotent: a retried request returns the earlier upload with alreadyUploaded = true. Body user names are stored
    // as declared; a server-authenticated identity (Windows auth, when enabled) is stored separately.
    [HttpPost("upload")]
    [ProducesResponseType<MeasurementUploadResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<MeasurementUploadResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<MeasurementUploadResponse>> Upload(MeasurementUploadRequest? request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return Problem("The request body is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.Validate() is { } problem)
        {
            return Problem(problem, statusCode: StatusCodes.Status400BadRequest);
        }

        var verifiedUploader = User.Identity is { IsAuthenticated: true, Name: { Length: > 0 } name } ? name : null;

        try
        {
            var result = await repository.SaveAsync(request, request.Fingerprint(), verifiedUploader, cancellationToken);

            if (result.AlreadyUploaded)
            {
                return Ok(result);
            }

            logger.LogInformation("Stored upload {UploadId}: {RecordCount} measurements of {Drillhole}.",
                result.UploadId, result.RecordCount, request.MeasurementInformation!.Drillhole);
            return StatusCode(StatusCodes.Status201Created, result);
        }
        catch (MeasurementUploadConflictException conflict)
        {
            var captures = conflict.CaptureNumbers;
            return Problem(captures.Count == 0
                    ? "Some of these measurements were uploaded at the same time by another request. Nothing was stored."
                    : $"{captures.Count} of these measurements were already uploaded in an earlier file (Capture No {string.Join(", ", captures.Take(20))}" +
                      $"{(captures.Count > 20 ? ", …" : "")}). Remove them and upload the rest. Nothing was stored.",
                statusCode: StatusCodes.Status409Conflict);
        }
        catch (UploadTablesMissingException ex)
        {
            logger.LogWarning(ex, "The SPP CT007 table or procedures are missing; run Database/Migrations/001_CreateSppCt007.sql.");
            return Problem($"{UploadedMeasurementController.TablesMissingMessage} Nothing was stored.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "Failed to store a measurement upload in SQL Server.");
            return Problem("The measurement database is unavailable. Nothing was stored; try again later.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
