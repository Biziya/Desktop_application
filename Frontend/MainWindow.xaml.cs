using System.Windows;
using System.Windows.Input;
using Frontend.ViewModels;

namespace Frontend;

public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = viewModel;

        // One window-level tunnel registration, including events already handled by controls.
        // The handler lives as long as the window, so it needs no explicit removal.
        AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(OnMeasurementMouseDown), handledEventsToo: true);
    }

    private bool AcceptMeasurementMouseInput =>
        IsActive && IsEnabled && WindowState != WindowState.Minimized && viewModel.Measurement.IsMeasurementModeActive;

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

    protected override void OnContextMenuOpening(ContextMenuEventArgs e)
    {
        base.OnContextMenuOpening(e);

        // Right-click is reserved for capture while armed; normal menus return after Stop.
        if (AcceptMeasurementMouseInput)
        {
            e.Handled = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        viewModel.Dispose();
        base.OnClosed(e);
    }
}
