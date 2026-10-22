using System.Windows;
using System.Windows.Media;
using Frontend.Pages.Measurement;
using Microsoft.Win32;

namespace Frontend.Windows.ConfirmationWindow;

/// <summary>Confirmation and file dialogs, centred over the main window.</summary>
public sealed class DialogService : IDialogService
{
    public bool ConfirmDelete() => Show(new ConfirmationWindow(
        "Delete measurement",
        "Delete this measurement?",
        "It is removed from the table and the database.",
        "Delete",
        (Brush)Application.Current.FindResource("Danger")));

    public bool ConfirmRemoveImported() => Show(new ConfirmationWindow(
        "Remove measurement",
        "Remove this measurement from the upload?",
        "It is removed from this import only. The CSV file and the central database are not changed.",
        "Remove",
        (Brush)Application.Current.FindResource("Danger")));

    public bool ConfirmUpload(int recordCount, string drillhole) => Show(new ConfirmationWindow(
        "Upload data",
        $"Upload {recordCount} measurement{(recordCount == 1 ? "" : "s")} of {drillhole}?",
        "They are stored in the central database.",
        "Upload"));

    public string? PickCsvFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import measurements",
            Filter = "CSV files (*.csv)|*.csv",
            DefaultExt = ".csv",
            CheckFileExists = true,
            Multiselect = false
        };

        return dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FileName : null;
    }

    private static bool Show(Window dialog)
    {
        dialog.Owner = Application.Current.MainWindow;
        return dialog.ShowDialog() == true;
    }
}
