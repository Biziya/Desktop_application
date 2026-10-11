namespace Frontend.Models;
public sealed record CoreInformation(string DrillholeId, string DrillholeName, string ProjectArea,
    string CoreId, decimal DepthFrom, decimal DepthTo, string Operator, string Notes)
{
    public decimal CoreDepth => DepthTo - DepthFrom;
}
