namespace Frontend.Pages.Measurement;

public interface IDialogService
{
    bool ConfirmDelete();

    /// <summary>Upload Data: removing an imported row changes only the import in memory (not the file, not the database).</summary>
    bool ConfirmRemoveImported();

    bool ConfirmUpload(int recordCount, string drillhole);

    /// <summary>Full path of the CSV file the user chose to import, or null when cancelled.</summary>
    string? PickCsvFile();

    /// <summary>Full path of the Gamma Guard database file the user chose (opened in the folder of
    /// <paramref name="currentPath"/> when given), or null when cancelled.</summary>
    string? PickGammaGuardDatabase(string? currentPath);
}
