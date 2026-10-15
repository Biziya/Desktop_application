using System.Globalization;
using Backend.Models.CoreInformation.Drillhole;
using Microsoft.Data.SqlClient;

namespace Backend.Repositories.CoreInformation.Drillhole;

public sealed class DrillholeRepository(IConfiguration configuration) : IDrillholeRepository
{
    private const string SelectDrillholesQuery = """
        SELECT
            HOLEID,
            PROJECTCODE,
            RIGHT(PROSPECT, 4) AS CAMPAIGN
        FROM ZOOVCH_COLLAR_LIST;
        """;

    private readonly string _connectionString = configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

    public async Task<IReadOnlyList<DrillholeResponse>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(SelectDrillholesQuery, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var drillholes = new List<DrillholeResponse>();
        while (await reader.ReadAsync(cancellationToken))
        {
            drillholes.Add(new DrillholeResponse(
                HoleId: ReadString(reader, 0),
                ProjectCode: ReadString(reader, 1),
                Campaign: ReadString(reader, 2)));
        }

        return drillholes;
    }

    private static string ReadString(SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? string.Empty
            : Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture) ?? string.Empty;
}
