using System.IO;
using System.Reflection;
using System.Windows;
using Frontend.Config;
using Frontend.Pages;
using Frontend.Shared.Help;
using Velopack;

namespace Frontend;

public partial class App : Application
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Velopack must run before WPF starts (it may handle install/update hooks and exit).
        VelopackApp.Build().Run();

        App app = new();
        app.InitializeComponent();
        app.Run();
    }

    // A configuration error closes the app instead of opening it half-configured.
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        MainWindow window;

        try
        {
            ApplicationSettings.Load(AppContext.BaseDirectory).AddTo(Resources);
            PageSettings.AddTo(Resources, AppContext.BaseDirectory);
            DataRetentionSettings.Load(AppContext.BaseDirectory).AddTo(Resources);
            HelpSettings.Load(AppContext.BaseDirectory).AddTo(Resources);

            window = new MainWindow(); // loads ApiSettings, which can also throw InvalidDataException
        }
        catch (InvalidDataException error)
        {
            MessageBox.Show(
                error.Message,
                $"{ApplicationName} — configuration error",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );

            Shutdown(1);
            return;
        }

        window.Show();
    }

    // From application.yaml once loaded; otherwise the product name compiled from the same file.
    private string? ApplicationName =>
        TryFindResource("ApplicationName") as string ?? typeof(App).Assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product;
}
