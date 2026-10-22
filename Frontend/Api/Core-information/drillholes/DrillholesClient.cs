namespace Frontend.Api.CoreInformation.Drillholes;

public sealed class DrillholesClient(ApiClient api)
{
    public const string Path = "/api/drillholes";

    public Task<ApiResult<IReadOnlyList<DrillholeResponse>>> GetAsync(CancellationToken cancellationToken) =>
        api.GetAsync<IReadOnlyList<DrillholeResponse>>(Path, cancellationToken);
}
