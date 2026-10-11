using System.Windows;
namespace Frontend.Views;
public partial class DeleteConfirmationWindow : Window
{
    public DeleteConfirmationWindow() => InitializeComponent();
    private void Confirm(object sender, RoutedEventArgs e) => DialogResult = true;
}
