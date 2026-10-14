namespace Frontend.Shared.Models;

public sealed record CoreSnapshot(
    string DrillholeId,
    string DrillholeName,
    string ProjectArea,
    string Operator,
    string Notes);
