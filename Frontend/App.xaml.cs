using System.IO;
using System.Windows;
using Frontend.Pages;

namespace Frontend;

public partial class App : Application
{
    // The section settings (Pages/**/*.yaml) are read before the main window exists; a configuration error is shown
    // and the app closes instead of opening with missing labels or sizes.
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            PageSettings.AddTo(Resources, AppContext.BaseDirectory);
        }
        catch (InvalidDataException error)
        {
            MessageBox.Show(error.Message, "SPP CT-007 Measurement Studio — configuration error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        new MainWindow().Show();
    }
}
