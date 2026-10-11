using System.Windows;
using Frontend.ViewModels;
namespace Frontend;
public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel = new();
    public MainWindow() { InitializeComponent(); DataContext = viewModel; }
    protected override void OnClosed(EventArgs e) { viewModel.Dispose(); base.OnClosed(e); }
}
