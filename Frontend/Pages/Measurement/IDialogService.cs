namespace Frontend.Pages.Measurement;

public interface IDialogService
{
    bool ConfirmDelete();

    bool ConfirmRemoveImported();

    bool ConfirmUpload(int recordCount, string drillhole);

    /// <summary>"Discard new drillhole?" (Discard / Keep editing). True = discard what was typed.</summary>
    bool ConfirmDiscardNewDrillhole();

    /// <summary>Full path of the CSV file the user chose to import, or null when cancelled.</summary>
    string? PickCsvFile();

    /// <summary>Full path of the Gamma Guard database file the user chose (opened in the folder of
    /// <paramref name="currentPath"/> when given), or null when cancelled.</summary>
    string? PickGammaGuardDatabase(string? currentPath);
}
