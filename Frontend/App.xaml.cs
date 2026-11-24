using System.IO;
using System.Reflection;
using System.Windows;
using Frontend.Config;
using Frontend.Pages;
using Frontend.Shared.Help;
using Frontend.Shared.Infrastructure;
using Velopack;

namespace Frontend;

public partial class App : Application
{
    [STAThread]
    private static void Main()
    {
        // Velopack must run before WPF starts (it may handle install/update hooks and exit).
        VelopackApp.Build().Run();

        StartJitProfile();

        App app = new();
        app.InitializeComponent();
        app.Run();
    }

    // Multi-core JIT: start-up methods recorded on the previous run are compiled in the background on the next one.
    // Optional: a folder that cannot be written only loses the speed-up.
    private static void StartJitProfile()
    {
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CT007_Measurement_Studio");
            Directory.CreateDirectory(folder);
            System.Runtime.ProfileOptimization.SetProfileRoot(folder);
            System.Runtime.ProfileOptimization.StartProfile("startup.jitprofile");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }

    // A configuration error closes the app instead of opening it half-configured.
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        CatchUnhandledExceptions();
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

    // Every unhandled exception goes to the crash log. One on the UI thread is reported and the app keeps running;
    // when they repeat quickly (an error on every layout pass), the app closes instead of showing message after message.
    private readonly Queue<DateTime> recentErrors = new();

    private void CatchUnhandledExceptions()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            var log = CrashLog.Write("UI thread", e.Exception);
            var now = DateTime.UtcNow;
            recentErrors.Enqueue(now);

            while (recentErrors.Peek() < now.AddSeconds(-10))
            {
                recentErrors.Dequeue();
            }

            if (recentErrors.Count > 3)
            {
                return; // not handled: the app closes
            }

            e.Handled = true;
            MessageBox.Show(
                $"Something went wrong: {e.Exception.Message}\n\nThe app keeps running. If it happens again, send this file to support:\n{log ?? "(the log could not be written)"}",
                ApplicationName,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception error)
            {
                CrashLog.Write("Unhandled", error);
            }
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CrashLog.Write("Unobserved task", e.Exception);
            e.SetObserved();
        };
    }

    // From application.yaml once loaded; otherwise the product name compiled from the same file.
    private string? ApplicationName =>
        TryFindResource("ApplicationName") as string ?? typeof(App).Assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product;
}
