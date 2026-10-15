using Backend.Models.CoreInformation.Drillhole;
using Backend.Repositories.CoreInformation.Drillhole;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Backend.Controllers.CoreInformation.Drillhole;

[ApiController]
[Tags("Core Information")]
[Route("api/core-information/drillholes")]
public sealed class DrillholeController(IDrillholeRepository repository, ILogger<DrillholeController> logger) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<DrillholeResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<IReadOnlyList<DrillholeResponse>>> Get(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await repository.GetAllAsync(cancellationToken));
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "Failed to read drillholes from SQL Server.");
            return Problem("The drillhole database is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
