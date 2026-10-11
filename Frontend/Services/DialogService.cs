using System.Windows;
using Frontend.Views;
namespace Frontend.Services;
public interface IDialogService { bool ConfirmDelete(); }
public sealed class DialogService : IDialogService
{
    public bool ConfirmDelete() => new DeleteConfirmationWindow
        { Owner = Application.Current.MainWindow }.ShowDialog() == true;
}
