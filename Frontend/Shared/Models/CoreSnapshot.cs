namespace Frontend.Shared.Models;

public sealed record CoreSnapshot(
    string DrillholeId,
    string DrillholeName,
    string ProjectArea,
    string CoreId,
    string Operator,
    string Notes);
