using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Frontend.Shared.Help;
using Frontend.Shared.Infrastructure;
using Frontend.ViewModels;

namespace Frontend;

public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;
    private HelpWindow? helpWindow;

    public MainWindow()
        : this(new MainViewModel())
    {
    }

    public MainWindow(MainViewModel viewModel)
    {
        this.viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        WindowCaption.Apply(this);

        // One window-level tunnel registration, including events already handled by controls.
        // The handler lives as long as the window, so it needs no explicit removal.
        AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(OnMeasurementMouseDown), handledEventsToo: true);

        // F1 / Esc at window level, before any control sees them: they work whatever has keyboard focus.
        AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(OnHelpShortcut), handledEventsToo: true);
        viewModel.Help.PropertyChanged += OnHelpChanged;
        viewModel.Help.FocusRequested += BringHelpToFront;
    }

    /// <summary>The Help window, once created (one instance for the app's lifetime).</summary>
    public HelpWindow? HelpWindowInstance => helpWindow;

    /// <summary>F1 toggles Help; Esc closes it (only when open, so Esc keeps its other uses otherwise).</summary>
    public static bool HandleHelpShortcut(Key key, HelpPanel help, Action<HelpTrigger> toggle)
    {
        switch (key)
        {
            case Key.F1:
                toggle(HelpTrigger.Key);
                return true;
            case Key.Escape when help.IsOpen:
                help.Close();
                return true;
            default:
                return false;
        }
    }

    private void OnHelpShortcut(object sender, KeyEventArgs e)
    {
        if (!e.Handled && HandleHelpShortcut(e.Key == Key.System ? e.SystemKey : e.Key, viewModel.Help, viewModel.ToggleHelp))
        {
            e.Handled = true;
        }
    }

    // The Help window counts as part of the app: right-click captures while either window is active, but only while
    // the Measurement page is on screen. On every other page right-click is an ordinary right-click.
    private bool AcceptMeasurementMouseInput =>
        (IsActive || helpWindow?.IsActive == true) && IsEnabled && WindowState != WindowState.Minimized
        && viewModel.IsMeasurementPageActive && viewModel.Measurement.IsMeasurementModeActive;

    private void OnMeasurementMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Right || !AcceptMeasurementMouseInput)
        {
            return;
        }

        var timestamp = DateTimeOffset.Now;

        // Consume once while armed. The ViewModel rejects capture/processing/cleanup duplicates.
        e.Handled = true;

        if (viewModel.Measurement.TriggerCommand.CanExecute(timestamp))
        {
            viewModel.Measurement.TriggerCommand.Execute(timestamp);
        }
    }

    // The ? button (ApplicationCommands.Help): the same function as F1, but never closes an open Help window.
    private void HelpButtonPressed(object sender, ExecutedRoutedEventArgs e) => viewModel.ToggleHelp(HelpTrigger.Button);

    private void OnHelpChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HelpPanel.IsOpen) && !viewModel.Help.IsOpen)
        {
            helpWindow?.Hide();

            // Back to the app, so the next key press (F1 again, a shortcut) reaches the main window.
            if (IsVisible)
            {
                Activate();
            }
        }
    }

    // Single instance: created once, then only shown, restored and activated. Opening never does nothing silently.
    private void BringHelpToFront()
    {
        if (helpWindow is null)
        {
            helpWindow = new HelpWindow(viewModel.Help, OnMeasurementMouseDown, () => AcceptMeasurementMouseInput,
                OnHelpShortcut)
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.Manual,
            };
            PlaceHelpWindow(helpWindow, firstTime: true);
        }
        else
        {
            PlaceHelpWindow(helpWindow, firstTime: false);
        }

        if (helpWindow.WindowState == WindowState.Minimized)
        {
            helpWindow.WindowState = WindowState.Normal;
        }

        helpWindow.Show();
        helpWindow.Activate();
        helpWindow.ScrollToCurrentSection();
    }

    // First time: near the top-right corner, inside the main window. Later it opens where the user left it, pulled back
    // onto the screen if that place is no longer visible (another monitor, a resolution change).
    private void PlaceHelpWindow(Window window, bool firstTime)
    {
        var area = SystemParameters.WorkArea;

        if (firstTime && PresentationSource.FromVisual(this)?.CompositionTarget is { } target)
        {
            var topRight = target.TransformFromDevice.Transform(PointToScreen(new Point(ActualWidth, 0)));
            window.Left = topRight.X - window.Width - 32;
            window.Top = topRight.Y + 72;
        }
        else if (firstTime || double.IsNaN(window.Left) || double.IsNaN(window.Top))
        {
            window.Left = area.Right - window.Width - 32;
            window.Top = area.Top + 72;
        }

        var screenLeft = SystemParameters.VirtualScreenLeft;
        var screenTop = SystemParameters.VirtualScreenTop;
        window.Left = Math.Clamp(window.Left, screenLeft, Math.Max(screenLeft, screenLeft + SystemParameters.VirtualScreenWidth - window.Width));
        window.Top = Math.Clamp(window.Top, screenTop, Math.Max(screenTop, screenTop + SystemParameters.VirtualScreenHeight - 80));
    }

    protected override void OnContextMenuOpening(ContextMenuEventArgs e)
    {
        base.OnContextMenuOpening(e);

        // Right-click is reserved for capture while armed; normal menus return after Stop.
        if (AcceptMeasurementMouseInput)
        {
            e.Handled = true;
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        if (!e.Cancel && helpWindow is not null)
        {
            helpWindow.AllowClose = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        viewModel.Help.PropertyChanged -= OnHelpChanged;
        viewModel.Help.FocusRequested -= BringHelpToFront;
        viewModel.Dispose();
        base.OnClosed(e);
    }
}
