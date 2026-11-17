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
        viewModel.Help.PropertyChanged += OnHelpChanged;
    }

    // The Help window counts as part of the app: right-click captures while either window is active.
    private bool AcceptMeasurementMouseInput =>
        (IsActive || helpWindow?.IsActive == true) && IsEnabled && WindowState != WindowState.Minimized
        && viewModel.Measurement.IsMeasurementModeActive;

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

    private void ShowHelp(object sender, ExecutedRoutedEventArgs e) => viewModel.ShowHelp();

    private void OnHelpChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(HelpPanel.IsOpen))
        {
            return;
        }

        if (!viewModel.Help.IsOpen)
        {
            helpWindow?.Hide();
            return;
        }

        if (helpWindow is null)
        {
            helpWindow = new HelpWindow(viewModel.Help, OnMeasurementMouseDown, () => AcceptMeasurementMouseInput)
            {
                Owner = this,
                ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
            };
            PlaceHelpWindow(helpWindow);
        }

        helpWindow.Show();
    }

    // First time only: near the top-right corner, inside the main window. Later it opens where the user left it.
    private void PlaceHelpWindow(Window window)
    {
        var source = PresentationSource.FromVisual(this);

        if (source?.CompositionTarget is null)
        {
            return;
        }

        var topRight = source.CompositionTarget.TransformFromDevice.Transform(PointToScreen(new Point(ActualWidth, 0)));
        window.Left = topRight.X - window.Width - 32;
        window.Top = topRight.Y + 72;
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
        viewModel.Dispose();
        base.OnClosed(e);
    }
}
