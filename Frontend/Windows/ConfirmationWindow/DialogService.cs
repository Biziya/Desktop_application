using System.Windows;
using System.Windows.Media;
using Frontend.Pages.Measurement;

namespace Frontend.Windows.ConfirmationWindow;

/// <summary>Confirmation dialogs for row changes, centred over the main window.</summary>
public sealed class DialogService : IDialogService
{
    public bool ConfirmDelete() => Show(new ConfirmationWindow(
        "Delete measurement",
        "Delete this measurement?",
        "It is removed from the table and the database.",
        "Delete",
        (Brush)Application.Current.FindResource("Danger")));

    public bool ConfirmMove(int fromPosition, int toPosition) => Show(new ConfirmationWindow(
        "Move measurement",
        $"Move this measurement from position {fromPosition} to position {toPosition}?",
        "Only the row order changes; its values and depths stay the same.",
        "Move"));

    private static bool Show(Window dialog)
    {
        dialog.Owner = Application.Current.MainWindow;
        return dialog.ShowDialog() == true;
    }
}
