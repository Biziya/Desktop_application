using System.Windows;
using Frontend.Pages.Measurement;

namespace Frontend.Windows.ConfirmationWindow;

public sealed class DialogService : IDialogService
{
    public bool ConfirmDelete() =>
        new DeleteConfirmationWindow { Owner = Application.Current.MainWindow }.ShowDialog() == true;
}
