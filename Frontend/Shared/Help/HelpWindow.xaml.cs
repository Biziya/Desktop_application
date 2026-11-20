using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Frontend.Shared.Help;

/// <summary>
/// The single floating Help window, owned by MainWindow. Closing it only hides it (HelpPanel.Close), so it reopens where
/// the user left it. It scrolls to the section of the page the user is on. While measuring, a right-click in it
/// captures exactly like a right-click in the main window.
/// </summary>
public partial class HelpWindow : Window
{
    private readonly HelpPanel help;
    private readonly Func<bool> rightClickCaptures;

    /// <param name="onMouseDown">MainWindow's capture handler, so right-click works with this window active.</param>
    /// <param name="rightClickCaptures">True while right-click is reserved for capture (context menus are suppressed).</param>
    /// <param name="onKeyDown">MainWindow's F1 / Esc handler, so the shortcuts behave the same in both windows.</param>
    public HelpWindow(HelpPanel help, MouseButtonEventHandler onMouseDown, Func<bool> rightClickCaptures, KeyEventHandler onKeyDown)
    {
        this.help = help;
        this.rightClickCaptures = rightClickCaptures;
        InitializeComponent();
        DataContext = help;
        Infrastructure.WindowCaption.Apply(this);
        AddHandler(Mouse.PreviewMouseDownEvent, onMouseDown, handledEventsToo: true);
        AddHandler(Keyboard.PreviewKeyDownEvent, onKeyDown, handledEventsToo: true);
        help.SectionRequested += ScrollToCurrentSection;
    }

    /// <summary>Set by MainWindow when the application closes; until then × only hides the window.</summary>
    public bool AllowClose { get; set; }

    /// <summary>The large Close button in the footer.</summary>
    public Button CloseHelpButton => CloseButton;

    /// <summary>Vertical offset of the section last scrolled to (tests).</summary>
    public double LastSectionOffset { get; private set; } = double.NaN;

    /// <summary>Scrolls the section of <see cref="HelpPanel.Page"/> to the top, once layout is done.</summary>
    public void ScrollToCurrentSection()
    {
        if (!IsVisible)
        {
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (help.Page is { } page && PageSections.ItemContainerGenerator.ContainerFromItem(page) is FrameworkElement section)
            {
                ScrollTo(section);
            }
        });
    }

    private void ShowShortcuts(object sender, RoutedEventArgs e) => ScrollTo(ShortcutsSection);

    private void ScrollTo(FrameworkElement section)
    {
        UpdateLayout();
        var offset = section.TransformToAncestor(Sections).Transform(new Point(0, 0)).Y;
        Scroller.ScrollToVerticalOffset(offset);
        LastSectionOffset = offset;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            help.Close();
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        help.SectionRequested -= ScrollToCurrentSection;
        base.OnClosed(e);
    }

    protected override void OnContextMenuOpening(ContextMenuEventArgs e)
    {
        base.OnContextMenuOpening(e);

        if (rightClickCaptures())
        {
            e.Handled = true;
        }
    }
}
