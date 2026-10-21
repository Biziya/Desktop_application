
using System;
using System.IO;
using System.Reflection;
using System.Windows;
using Frontend.Config;
using Frontend.Pages;
using Velopack;

namespace Frontend;

public partial class App : Application
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Initialize Velopack before WPF starts.
        VelopackApp.Build().Run();

        // Start the WPF application normally.
        App app = new();
        app.InitializeComponent();
        app.Run();
    }

    // Load YAML configuration (application metadata, page sections) and the backend API settings before showing
    // MainWindow; a configuration error is shown and the app closes instead of opening half-configured.
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        MainWindow window;

        try
        {
            ApplicationSettings.Load(AppContext.BaseDirectory).AddTo(Resources);
            PageSettings.AddTo(Resources, AppContext.BaseDirectory);

            // MainViewModel reads ApiSettings.LoadConfigured() (APP_ENV, .env.<APP_ENV>, API_* variables).
            window = new MainWindow();
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
