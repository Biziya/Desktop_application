
using System;
using System.IO;
using System.Windows;
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

    // Load YAML configuration before opening MainWindow.
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            PageSettings.AddTo(Resources, AppContext.BaseDirectory);
        }
        catch (InvalidDataException error)
        {
            MessageBox.Show(
                error.Message,
                "SPP CT-007 Measurement Studio — configuration error",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );

            Shutdown(1);
            return;
        }

        new MainWindow().Show();
    }
}
