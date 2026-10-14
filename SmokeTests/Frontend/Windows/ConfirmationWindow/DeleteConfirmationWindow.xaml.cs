using System.Windows;

namespace Frontend.Windows.ConfirmationWindow;

public partial class DeleteConfirmationWindow : Window
{
    public DeleteConfirmationWindow() => InitializeComponent();

    private void Confirm(object sender, RoutedEventArgs e) => DialogResult = true;
}
