using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Frontend.Shared.Help;

/// <summary>
/// Floating Help window, owned by MainWindow. Closing it only hides it (HelpPanel.Close), so it reopens where the user
/// left it. While measuring, a right-click in it captures exactly like a right-click in the main window.
/// </summary>
public partial class HelpWindow : Window
{
    private readonly HelpPanel help;
    private readonly Func<bool> rightClickCaptures;

    /// <param name="onMouseDown">MainWindow's capture handler, so right-click works with this window active.</param>
    /// <param name="rightClickCaptures">True while right-click is reserved for capture (context menus are suppressed).</param>
    public HelpWindow(HelpPanel help, MouseButtonEventHandler onMouseDown, Func<bool> rightClickCaptures)
    {
        this.help = help;
        this.rightClickCaptures = rightClickCaptures;
        InitializeComponent();
        DataContext = help;
        Infrastructure.WindowCaption.Apply(this);
        AddHandler(Mouse.PreviewMouseDownEvent, onMouseDown, handledEventsToo: true);
    }

    /// <summary>Set by MainWindow when the application closes; until then × only hides the window.</summary>
    public bool AllowClose { get; set; }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            help.Close();
        }

        base.OnClosing(e);
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
