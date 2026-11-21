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

        // F1, Esc and Ctrl+F at window level, before any control sees them: they work whatever has keyboard focus.
        AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(OnHelpShortcut), handledEventsToo: true);

        // Esc that a page control did not use (it bubbled back up unhandled) closes Help too.
        AddHandler(Keyboard.KeyDownEvent, new KeyEventHandler(OnUnhandledEscape));
    }

    /// <summary>The Help drawer (an overlay of this window).</summary>
    public HelpDrawer HelpDrawer => HelpOverlay;

    private void OnHelpShortcut(object sender, KeyEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var help = viewModel.Help;

        switch (key)
        {
            case Key.F1:
                viewModel.ToggleHelp();
                e.Handled = true;
                break;
            case Key.F when Keyboard.Modifiers == ModifierKeys.Control && help.IsOpen:
                HelpOverlay.FocusSearch();
                e.Handled = true;
                break;
            // Esc closes the enlarged picture first, then Help: at once when the focus is in Help (or nowhere).
            // With the focus on a page control (the countdown editor, a dropdown), that control gets Esc first.
            case Key.Escape when help.IsLightboxOpen:
            case Key.Escape when help.IsOpen && (Keyboard.FocusedElement is null or Window || HelpOverlay.Contains(Keyboard.FocusedElement)):
                help.CloseTopmost();
                e.Handled = true;
                break;
        }
    }

    private void OnUnhandledEscape(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && viewModel.Help.IsOpen)
        {
            viewModel.Help.CloseTopmost();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Whether a right-click on <paramref name="source"/> captures: only while the Measurement page is on screen and
    /// measuring, never inside Help (the drawer or its enlarged picture).
    /// </summary>
    /// <param name="requireActive">False in tests, whose windows are never activated.</param>
    public bool RightClickCaptures(object? source, bool requireActive = true) =>
        (IsActive || !requireActive) && IsEnabled && WindowState != WindowState.Minimized
        && viewModel.IsMeasurementPageActive && viewModel.Measurement.IsMeasurementModeActive
        && !HelpOverlay.Contains(source);

    private void OnMeasurementMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Right || !RightClickCaptures(e.OriginalSource))
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

    // The ? button (ApplicationCommands.Help): the same function as F1.
    private void HelpButtonPressed(object sender, ExecutedRoutedEventArgs e) => viewModel.ToggleHelp();

    protected override void OnContextMenuOpening(ContextMenuEventArgs e)
    {
        base.OnContextMenuOpening(e);

        // Right-click is reserved for capture while armed; normal menus return after Stop (and always work in Help).
        if (RightClickCaptures(e.OriginalSource))
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
