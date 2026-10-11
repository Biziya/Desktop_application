using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace Frontend.Pages.Measurement;
// Only pointer hit testing and drop feedback live in the view; collection changes belong to the VM.
public partial class MeasurementPage : UserControl
{
    private Point dragStart;
    private MeasurementResult? candidate;
    private DataGridRow? highlighted;
    public MeasurementPage() => InitializeComponent();
    // Scoped to the countdown card: no window/global subscriptions, and no table/sidebar interception.
    private void CaptureAreaRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Right || !IsVisible || !IsEnabled ||
            Window.GetWindow(this) is not { IsActive: true } ||
            Ancestor<System.Windows.Controls.Primitives.ButtonBase>(e.OriginalSource as DependencyObject) is not null ||
            DataContext is not MeasurementViewModel vm || !vm.IsMeasurementModeActive) return;
        var timestamp = DateTimeOffset.Now;
        // Consume the routed event once, including ignored clicks while a capture is busy.
        e.Handled = true;
        if (vm.TriggerCommand.CanExecute(timestamp)) vm.TriggerCommand.Execute(timestamp);
    }
    private void HandleDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MeasurementResult row })
        { candidate = row; dragStart = e.GetPosition(ResultsGrid); }
    }
    private void EndPointer(object sender, MouseButtonEventArgs e) => candidate = null;
    private void BeginDrag(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) { candidate = null; return; }
        if (candidate is not { } row) return;
        var position = e.GetPosition(ResultsGrid);
        if (Math.Abs(position.X-dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y-dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        candidate = null;
        try { DragDrop.DoDragDrop(ResultsGrid, new DataObject(typeof(MeasurementResult),row), DragDropEffects.Move); }
        finally { ClearFeedback(); }
    }
    private static T? Ancestor<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element is not null) { if (element is T found) return found; element = element is FrameworkContentElement content ? content.Parent : VisualTreeHelper.GetParent(element); }
        return null;
    }
    private void ClearFeedback()
    {
        highlighted?.ClearValue(Control.BorderBrushProperty);
        highlighted?.ClearValue(Control.BorderThicknessProperty);
        highlighted = null;
    }
    private void RowDragLeave(object sender, DragEventArgs e) => ClearFeedback();
    private void RowDragOver(object sender, DragEventArgs e)
    {
        ClearFeedback();
        e.Effects = DragDropEffects.None; e.Handled = true;
        if (!e.Data.GetDataPresent(typeof(MeasurementResult)) || DataContext is not MeasurementViewModel vm ||
            e.Data.GetData(typeof(MeasurementResult)) is not MeasurementResult source || !vm.Results.Contains(source)) return;
        var target = Ancestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (target?.Item is not MeasurementResult) return;
        highlighted = target;
        target.BorderBrush = Brushes.DodgerBlue;
        target.BorderThickness = e.GetPosition(target).Y > target.ActualHeight/2 ? new Thickness(0,0,0,3) : new Thickness(0,3,0,0);
        e.Effects = DragDropEffects.Move;
        // Edge scroll makes rows outside the currently virtualized viewport reachable.
        var point = e.GetPosition(ResultsGrid);
        var viewer = FindScrollViewer(ResultsGrid);
        if (point.Y < 70) viewer?.LineUp();
        else if (point.Y > ResultsGrid.ActualHeight-35) viewer?.LineDown();
    }
    private static ScrollViewer? FindScrollViewer(DependencyObject element)
    {
        if (element is ScrollViewer viewer) return viewer;
        for (var i=0; i<VisualTreeHelper.GetChildrenCount(element); i++)
            if (FindScrollViewer(VisualTreeHelper.GetChild(element,i)) is { } result) return result;
        return null;
    }
    private void RowDrop(object sender, DragEventArgs e)
    {
        ClearFeedback(); e.Handled = true;
        var target = Ancestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (DataContext is MeasurementViewModel vm && e.Data.GetData(typeof(MeasurementResult)) is MeasurementResult source &&
            target?.Item is MeasurementResult destination)
            vm.Move(source,destination,e.GetPosition(target).Y > target.ActualHeight/2);
    }
}
