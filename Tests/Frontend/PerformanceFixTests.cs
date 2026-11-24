using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using Frontend.Api.Measurements;
using Frontend.Pages.Measurement;
using Frontend.Pages.Measurement.CapturePanel;

namespace Ct007.Tests.FrontendTests;

/// <summary>Regression tests for the performance refactor: no leaks, no repeated work.</summary>
public static class PerformanceFixTests
{
    [Test("FE-PERF-04", "A depth chart taken off screen is released although its rows collection lives on")]
    public static async Task ChartReleasedAfterLeavingPage()
    {
        Wpf.EnsureResources();
        var rows = new ObservableCollection<MeasurementResult>();
        var window = new Window
        {
            Width = 400, Height = 300, ShowActivated = false, ShowInTaskbar = false,
            WindowStyle = WindowStyle.None, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20_000
        };

        try
        {
            window.Show();
            var chart = await ShowAndRemoveChart(window, rows);

            // The page is gone: a new row must not reach the old chart, and the chart must be collectable.
            rows.Add(Make.Row(Guid.NewGuid(), 1, 0, reading: new DeviceReading(null, 100, null, null, null, null)));
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.False(chart.IsAlive, "the removed chart is garbage-collected");
        }
        finally
        {
            window.Close();
        }
    }

    [Test("FE-PERF-05", "A depth chart on screen still redraws when rows are added")]
    public static async Task ChartFollowsRowsWhileShown()
    {
        Wpf.EnsureResources();
        var rows = new ObservableCollection<MeasurementResult>();
        var chart = new DepthBarChart { ItemsSource = rows };
        var window = new Window
        {
            Content = chart, Width = 400, Height = 300, ShowActivated = false, ShowInTaskbar = false,
            WindowStyle = WindowStyle.None, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20_000
        };

        try
        {
            window.Show();
            await Idle();
            Assert.True(chart.DrawnTickLabelsLeft is null, "no y scale without rows");
            rows.Add(Make.Row(Guid.NewGuid(), 1, 0, reading: new DeviceReading(null, 100, null, null, null, null)));
            await Idle();
            Assert.True(chart.DrawnTickLabelsLeft is not null, "the new row was drawn");
        }
        finally
        {
            window.Close();
        }
    }

    // In its own method so no local keeps the chart alive.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> ShowAndRemoveChart(Window window, ObservableCollection<MeasurementResult> rows)
    {
        var chart = new DepthBarChart { ItemsSource = rows };
        window.Content = chart;
        await Idle();
        Assert.True(chart.IsLoaded, "chart shown");
        window.Content = null;
        await Idle();
        return new WeakReference(chart);
    }

    private static Task Idle() => Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle).Task;
}
