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
    /// <summary>
    /// Stores one imported measurement session in the central database. Validated here whatever the client checked.
    /// Repeating a request (e.g. after a client timeout) never stores the dataset twice: the answer is then the earlier
    /// upload with alreadyUploaded = true. The user names in the body are stored as declared by the app; an identity the
    /// server authenticated (Windows authentication in the hosting, when enabled) is stored separately.
    /// </summary>
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
        catch (SqlException ex)
        {
            logger.LogError(ex, "Failed to store a measurement upload in SQL Server.");
            return Problem("The measurement database is unavailable. Nothing was stored; try again later.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
