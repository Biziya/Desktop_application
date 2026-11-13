namespace Frontend.Pages.Monitoring;

/// <summary>
/// One completed 5-second sample. <see cref="Readings"/> of <see cref="Expected"/> readings were averaged;
/// <see cref="CoverageSeconds"/> runs from the discarded first reading's timestamp to the last kept reading's
/// (about 5.0 s when complete). Values are null when no reading arrived.
/// </summary>
public sealed record SampleResult(
    string Serial,
    DateTimeOffset TakenAt,
    string Window,
    double? CoverageSeconds,
    int Readings,
    int Expected,
    double? Cpm,
    double? CpmRaw,
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
    double? Cpm,
    double? CpmRaw,
    double? Hdm,
    double? DoseRate,
    double? Temperature,
    string? DeviceSessionId);
