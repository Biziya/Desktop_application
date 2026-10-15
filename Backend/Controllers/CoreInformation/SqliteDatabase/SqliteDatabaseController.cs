using Backend.Models.CoreInformation.SqliteDatabase;
using Backend.Repositories.CoreInformation.SqliteDatabase;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers.CoreInformation.SqliteDatabase;

[ApiController]
[Tags("Core Information")]
[Route("api/core-information/sqlite-database")]
public sealed class SqliteDatabaseController(SqliteDatabaseRepository repository) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<SqliteDatabaseResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SqliteDatabaseResponse>> Get(CancellationToken cancellationToken) =>
        Ok(new SqliteDatabaseResponse(await repository.FindDatabasePathAsync(cancellationToken)));
}
