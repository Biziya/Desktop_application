namespace Frontend.Api.CoreInformation.Drillholes;

/// <summary>One entry of GET /api/core-information/drillholes (HoleId → Drillhole, ProjectCode → Project).</summary>
public sealed record DrillholeResponse(string? HoleId, string? ProjectCode, string? Campaign)
{
    // Text an editable ComboBox shows for the item.
    public override string ToString() => HoleId ?? "";
}
