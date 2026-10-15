using Backend.Models.CoreInformation.Drillhole;

namespace Backend.Repositories.CoreInformation.Drillhole;

public interface IDrillholeRepository
{
    Task<IReadOnlyList<DrillholeResponse>> GetAllAsync(CancellationToken cancellationToken);
}
