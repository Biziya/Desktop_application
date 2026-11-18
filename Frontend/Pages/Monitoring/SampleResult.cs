namespace Frontend.Pages.Monitoring;

/// <summary>One completed 5-second sample; values are null when no reading arrived. Cps/CpsRaw are counts per second.</summary>
public sealed record SampleResult(
    string Serial,
    DateTimeOffset TakenAt,
    string Window,
    double? CoverageSeconds,
    int Readings,
    int Expected,
    double? Cps,
    double? CpsRaw,
    double? Hdm,
    double? DoseRate,
    double? Temperature)
{
    public bool IsComplete => Readings == Expected;

    public string ReadingsText => $"{Readings} of {Expected}";
}

/// <summary>One row of the Recent readings table.</summary>
public sealed record ReadingRow(
    long Id,
    string Time,
    string Serial,
    long? Seq,
    double? Cps,
    double? CpsRaw,
    double? Hdm,
    double? DoseRate,
    double? Temperature,
    string? DeviceSessionId);
