using Frontend.Api.CoreInformation.Drillholes;

namespace Frontend.Pages.CoreInformation.DrillholeInformation;

/// <summary>
/// One entry of the Drillhole dropdown: either a group heading ("Measured drillholes" / "All drillholes"), which is
/// shown but cannot be selected, or a drillhole from the API list.
/// </summary>
public sealed record DrillholeOption
{
    private DrillholeOption(string header, DrillholeResponse? drillhole, bool showSeparator)
    {
        Header = header;
        Drillhole = drillhole;
        ShowSeparator = showSeparator;
    }

    public string Header { get; }

    public DrillholeResponse? Drillhole { get; }

    /// <summary>True for a group heading (not selectable).</summary>
    public bool IsHeader => Drillhole is null;

    /// <summary>A heading that follows another group gets a thin line above it.</summary>
    public bool ShowSeparator { get; }

    /// <summary>Text shown in the editable box when the entry is picked (TextSearch.TextPath).</summary>
    public string HoleId => Drillhole?.HoleId ?? "";

    public string Details => Drillhole is null ? "" : $"{Drillhole.ProjectCode} · {Drillhole.Campaign}";

    /// <summary>Heading of the first group (no line above it).</summary>
    public static DrillholeOption MeasuredHeader { get; } = Group("Measured drillholes", showSeparator: false);

    /// <summary>"All drillholes" heading as the first group, and below the Measured group (with a line above it).</summary>
    public static DrillholeOption AllHeader { get; } = Group("All drillholes", showSeparator: false);

    public static DrillholeOption AllHeaderAfterMeasured { get; } = Group("All drillholes", showSeparator: true);

    private static DrillholeOption Group(string header, bool showSeparator) => new(header, null, showSeparator);

    public static DrillholeOption Item(DrillholeResponse drillhole) => new("", drillhole, false);
}
