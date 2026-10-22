using System.Windows;
using System.Windows.Controls;

namespace Frontend.Pages.Measurement.CapturePanel;

// View only: depth commits and focus of the duration editor. Capture timing, validation and saving belong to the VM.
public partial class CapturePanelView : UserControl
{
    public CapturePanelView() => InitializeComponent();

    // Leaving Starting Depth or Step commits it: invalid text is reported in a toast (typing only colours the border).
    private void DepthCommitted(object sender, RoutedEventArgs e) => (DataContext as MeasurementViewModel)?.CommitDepthInputs();

    private void ThresholdCommitted(object sender, RoutedEventArgs e) => (DataContext as MeasurementViewModel)?.CommitThreshold();

    // The duration editor opens with its text selected, ready to type over.
    private void DurationEditorOpened(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        DurationBox.Focus();
        DurationBox.SelectAll();
    });
}
