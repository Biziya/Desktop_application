using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Ct007.Tests.Fixtures;

/// <summary>WPF helpers for view tests: the app's resources, off-screen rendering and binding-error capture.</summary>
public static class Wpf
{
    private static bool resourcesLoaded;

    /// <summary>The app's resources (Theme.xaml + the section YAML files), as App.OnStartup loads them.</summary>
    public static void EnsureResources()
    {
        if (resourcesLoaded)
        {
            return;
        }

        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Frontend;component/Shared/Styles/Theme.xaml") });
        Frontend.Pages.PageSettings.AddTo(app.Resources, AppContext.BaseDirectory);
        Frontend.Config.ApplicationSettings.Load(AppContext.BaseDirectory).AddTo(app.Resources);
        resourcesLoaded = true;
    }

    /// <summary>
    /// Shows <paramref name="content"/> in an off-screen window of the given size and returns the binding errors WPF
    /// reported while it laid out. With CT007_SCREENSHOTS set to a folder, the window is also saved there as PNG.
    /// </summary>
    public static Task<IReadOnlyList<string>> Render(FrameworkElement content, string name, double width = 1100, double height = 820)
    {
        EnsureResources();
        return RenderWindow(new Window { Content = content, Style = (Style)Application.Current.FindResource("AppWindow") }, name, width, height);
    }

    /// <summary>As <see cref="Render"/>, for a whole window (e.g. MainWindow); the window is closed afterwards.</summary>
    public static async Task<IReadOnlyList<string>> RenderWindow(Window window, string name, double width = 1320, double height = 870, Action? beforeCapture = null)
    {
        EnsureResources();
        var errors = new BindingErrors();
        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(errors);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        window.Width = width;
        window.Height = height;
        window.ShowActivated = false;
        window.WindowStyle = WindowStyle.None;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20_000;

        try
        {
            window.Show();
            await Task.Delay(150);
            beforeCapture?.Invoke();
            await Task.Delay(50);
            window.UpdateLayout();

            if (Environment.GetEnvironmentVariable("CT007_SCREENSHOTS") is { Length: > 0 } folder)
            {
                Directory.CreateDirectory(folder);
                var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(window.Content as Visual ?? window);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(folder, name + ".png"));
                encoder.Save(file);
            }

            return errors.Messages;
        }
        finally
        {
            window.Close();
            PresentationTraceSources.DataBindingSource.Listeners.Remove(errors);
        }
    }

    private sealed class BindingErrors : TraceListener
    {
        private readonly StringBuilder current = new();

        public List<string> Messages { get; } = [];

        public override void Write(string? message) => current.Append(message);

        public override void WriteLine(string? message)
        {
            current.Append(message);
            Messages.Add(current.ToString());
            current.Clear();
        }
    }
}
