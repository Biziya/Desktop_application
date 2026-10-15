namespace Frontend.Calculation.CoreInformation.MeasurementDatabase;

/// <summary>
/// Status of this app's own measurement database, at the path the measurement persistence service actually uses
/// (<c>IMeasurementRepository.DatabasePath</c>). Read-only: a missing file is reported, never created.
/// </summary>
public sealed class MeasurementDatabaseCheck(string databasePath)
{
    public string DatabasePath { get; } = databasePath;

    public LocalStatus Check() =>
        SqliteFileCheck.Check(DatabasePath, "Not created yet. The app creates it when it first stores data.");
}
