using System.Windows;
using System.Windows.Media;
using Frontend.Pages.Measurement;

namespace Frontend.Windows.ConfirmationWindow;

/// <summary>Confirmation dialog for deleting a row, centred over the main window.</summary>
public sealed class DialogService : IDialogService
{
    public bool ConfirmDelete() => Show(new ConfirmationWindow(
        "Delete measurement",
        "Delete this measurement?",
        "It is removed from the table and the database.",
        "Delete",
        (Brush)Application.Current.FindResource("Danger")));

    private static bool Show(Window dialog)
    {
        dialog.Owner = Application.Current.MainWindow;
        return dialog.ShowDialog() == true;
    }
}
