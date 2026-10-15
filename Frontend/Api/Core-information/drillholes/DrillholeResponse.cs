namespace Frontend.Api.CoreInformation.Drillholes;

/// <summary>
/// One entry of GET /api/core-information/drillholes. Form mapping: HoleId → Drillhole, ProjectCode → Project,
/// Campaign → Campaign.
/// </summary>
public sealed record DrillholeResponse(string? HoleId, string? ProjectCode, string? Campaign)
{
    // Text an editable ComboBox shows for the item.
    public override string ToString() => HoleId ?? "";
}
