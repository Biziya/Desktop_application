using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Frontend.Shared.Help;
using Frontend.Shared.Infrastructure;

namespace Ct007.Tests.FrontendTests;

/// <summary>FE-HELP-05…08, HELP-D-01…06: the Help drawer that slides in from the right inside the main window.</summary>
public static partial class GammaCoreLogUpdateTests
{
    // The main window on screen (off-screen, never activated) with help.yaml, Windows animations off unless asked.
    private static async Task<(MainViewModel Main, Frontend.MainWindow Window, HelpDrawer Drawer)> HelpShell(TestFolder folder,
        UiSettings? ui = null, double width = 1320)
    {
        Wpf.EnsureResources();
        var (main, _) = await Shell(folder, HelpSettings.Load(AppContext.BaseDirectory), ui);
        var window = new Frontend.MainWindow(main)
        {
            ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20_000, Width = width, Height = 870
        };
        window.Show();
        window.UpdateLayout();
        return (main, window, window.HelpDrawer);
    }

    // A key press as Windows delivers it: PreviewKeyDown from the window down, then KeyDown back up if nobody took it.
    private static void Press(UIElement target, Key key)
    {
        var source = PresentationSource.FromVisual(target)!;
        var preview = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        target.RaiseEvent(preview);

        if (!preview.Handled)
        {
            target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.KeyDownEvent });
        }
    }

    private static async Task Settle(Window window)
    {
        // Scrolls and focus run once layout is done: wait until everything queued has run.
        await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }

    private static T Within<T>(DependencyObject root, string automationId) where T : FrameworkElement =>
        Descendants(root).OfType<T>().First(e => AutomationProperties.GetAutomationId(e) == automationId);

    [Test("FE-HELP-05", "Help drawer: starts closed; ?, F1 and × open and close it; the ? is highlighted while open; no separate window")]
    public static async Task HelpDrawerToggles()
    {
        HelpDrawer.AnimationsEnabled = () => false;
        using var folder = new TestFolder();
        var (main, window, drawer) = await HelpShell(folder);

        try
        {
            var windowsBefore = Application.Current.Windows.Count;
            Assert.False(main.Help.IsOpen || drawer.Panel.IsVisible, "closed at start");

            var help = Descendants(window).OfType<Button>().First(b => b.IsVisible && b.Command == ApplicationCommands.Help);
            ApplicationCommands.Help.Execute(null, help);                   // ?
            window.UpdateLayout();
            Assert.True(main.Help.IsOpen && drawer.Panel.IsVisible, "? opens the drawer");
            Assert.Equal(0.0, drawer.SlideOffset, "fully in");
            Assert.True(HelpButtonState.GetIsHelpOpen(help), "the ? shows that Help is open");
            Assert.Equal("Open", AutomationProperties.GetItemStatus(help), "and says so to screen readers");
            Assert.Equal(windowsBefore, Application.Current.Windows.Count, "no extra window");
            Assert.True(Descendants(window).Contains(drawer.Panel), "the drawer is part of the main window");

            ApplicationCommands.Help.Execute(null, help);                   // ? again
            window.UpdateLayout();
            Assert.False(main.Help.IsOpen || drawer.Panel.IsVisible, "? closes it");
            Assert.False(HelpButtonState.GetIsHelpOpen(help), "the ? is plain again");

            Press(help, Key.F1);
            Assert.True(main.Help.IsOpen, "F1 opens");
            Press(help, Key.F1);
            Assert.False(main.Help.IsOpen, "F1 closes");

            main.ShowHelp();
            window.UpdateLayout();
            var close = Within<Button>(drawer, "HelpCloseButton");
            Assert.Equal("Close help (Esc)", AutomationProperties.GetHelpText(close), "× help text");
            Assert.True(close.Width == 32 && close.Height == 32, "× is 32 x 32");
            close.Command.Execute(null);
            Assert.False(main.Help.IsOpen, "× closes");
        }
        finally
        {
            window.Close();
            HelpDrawer.AnimationsEnabled = () => SystemParameters.ClientAreaAnimation;
        }
    }

    [Test("FE-HELP-06", "F1 and Esc reach Help whatever has the focus, on every page; Esc closes the enlarged picture first")]
    public static async Task HelpShortcutsAnyFocus()
    {
        HelpDrawer.AnimationsEnabled = () => false;
        using var folder = new TestFolder();
        var (main, window, drawer) = await HelpShell(folder);

        try
        {
            foreach (var page in main.Pages)
            {
                main.ShowPage(page.Page);
                window.UpdateLayout();
                var inner = (UIElement?)Descendants(window).OfType<TextBox>().FirstOrDefault(t => t.IsVisible && !drawer.Contains(t))
                    ?? Descendants(window).OfType<Button>().First(b => b.IsVisible && !drawer.Contains(b));

                Press(inner, Key.F1);
                Assert.True(main.Help.IsOpen, $"{page.Title}: F1 from a control on the page opens Help");
                Assert.Equal(page.Title, main.Help.Page?.Page, $"{page.Title}: Help at this page");
                window.UpdateLayout();

                Press(drawer.SearchField, Key.F1);
                Assert.False(main.Help.IsOpen, $"{page.Title}: F1 inside Help closes it");

                Press(inner, Key.F1);
                Press(drawer.SearchField, Key.Escape);
                Assert.False(main.Help.IsOpen, $"{page.Title}: Esc inside Help closes it");

                Press(inner, Key.F1);
                Press(inner, Key.Escape);
                Assert.False(main.Help.IsOpen, $"{page.Title}: Esc on the page closes it");
            }

            main.ShowPage(main.Measurement);
            main.ShowHelp();
            window.UpdateLayout();
            Within<Button>(drawer, "HelpScreenshot").Command.Execute(main.Help.Page);
            window.UpdateLayout();
            var lightbox = Within<Grid>(drawer, "HelpLightbox");
            Assert.True(main.Help.IsLightboxOpen && lightbox.IsVisible, "clicking the screenshot enlarges it");
            Press(drawer.SearchField, Key.Escape);
            Assert.True(!main.Help.IsLightboxOpen && main.Help.IsOpen, "Esc closes the picture first");
            Press(drawer.SearchField, Key.Escape);
            Assert.False(main.Help.IsOpen, "then Help");

            Assert.False(main.Help.CloseTopmost(), "Esc has nothing to close once Help is closed");
        }
        finally
        {
            window.Close();
            HelpDrawer.AnimationsEnabled = () => SystemParameters.ClientAreaAnimation;
        }
    }

    [Test("FE-HELP-07", "Help is context-sensitive: it opens at the page on screen, follows page changes and keeps each page's scroll")]
    public static async Task HelpFollowsPages()
    {
        HelpDrawer.AnimationsEnabled = () => false;
        using var folder = new TestFolder();
        var (main, window, drawer) = await HelpShell(folder);

        try
        {
            main.ShowPage(main.UploadData);
            main.ShowHelp();
            Assert.Equal("Upload Data", main.Help.Page?.Page, "opens at the page on screen");

            foreach (var page in main.Pages)
            {
                main.ShowPage(page.Page);
                Assert.Equal(page.Title, main.Help.Page?.Page, $"{page.Title}: follows");
            }

            main.ShowPage(main.Measurement);
            await Settle(window);
            Assert.True(drawer.PageScroller.ScrollableHeight > 300, "Measurement help is longer than the drawer");
            drawer.PageScroller.ScrollToVerticalOffset(300);
            window.UpdateLayout();
            await Settle(window);

            main.ShowPage(main.Monitoring);
            await Settle(window);
            Assert.Equal(0.0, drawer.PageScroller.VerticalOffset, "a page seen for the first time starts at the top");

            main.ShowPage(main.Measurement);
            await Assert.Eventually(() => Math.Abs(drawer.PageScroller.VerticalOffset - 300) < 1,
                $"back on Measurement, where it was ({drawer.PageScroller.VerticalOffset})");

            main.Help.Close();
            main.ShowPage(main.Core);
            main.ShowHelp();
            Assert.Equal("Core Information", main.Help.Page?.Page, "reopened at the page on screen");
        }
        finally
        {
            window.Close();
            HelpDrawer.AnimationsEnabled = () => SystemParameters.ClientAreaAnimation;
        }
    }

    [Test("FE-HELP-08", "Backdrop: dims other pages and closes Help on click; on Measurement no dim, and right-click inside Help never captures")]
    public static async Task HelpBackdropAndRightClick()
    {
        HelpDrawer.AnimationsEnabled = () => false;
        using var folder = new TestFolder();
        var (main, window, drawer) = await HelpShell(folder);

        try
        {
            var backdrop = Descendants(drawer).OfType<Border>().First(b => AutomationProperties.GetAutomationId(b) == "HelpBackdrop");
            main.ShowHelp();
            window.UpdateLayout();
            Assert.True(backdrop.IsVisible, "Core Information: dimmed behind Help");
            backdrop.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseDownEvent });
            Assert.False(main.Help.IsOpen, "a click on the dimmed page closes Help");

            await Arm(main);                                                // Measurement, measuring
            main.ShowHelp();
            window.UpdateLayout();
            Assert.False(backdrop.IsVisible, "Measurement: no dim, the graph stays visible");
            Assert.True(drawer.Panel.IsVisible, "Help is open");

            var chart = Descendants(window).OfType<Frontend.Pages.Measurement.CapturePanel.DepthBarChart>().First();
            Assert.True(window.RightClickCaptures(chart, requireActive: false), "right-click on the page captures with Help open");
            Assert.False(window.RightClickCaptures(drawer.SearchField, requireActive: false), "right-click inside Help does not");
            Assert.False(window.RightClickCaptures(Within<Button>(drawer, "HelpScreenshot"), requireActive: false), "nor on its screenshot");

            main.ShowPage(main.Core);
            window.UpdateLayout();
            Assert.True(backdrop.IsVisible, "dimmed again on another page");
        }
        finally
        {
            window.Close();
            HelpDrawer.AnimationsEnabled = () => SystemParameters.ClientAreaAnimation;
        }
    }

    [Test("HELP-D-01", "Drawer width: 420 px, kept between 360 px and half the window (85% below 900 px); a dragged width is remembered")]
    public static async Task HelpDrawerWidth()
    {
        Assert.Equal(420.0, HelpPanel.WidthFor(HelpPanel.DefaultWidth, 1320), "default");
        Assert.Equal(360.0, HelpPanel.WidthFor(200, 1320), "never narrower than 360");
        Assert.Equal(660.0, HelpPanel.WidthFor(900, 1320), "never wider than half the window");
        Assert.Equal(748.0, HelpPanel.WidthFor(420, 880), "85% of a narrow window");

        HelpDrawer.AnimationsEnabled = () => false;
        using var folder = new TestFolder();
        var path = folder.Path("ui-settings.json");
        var (main, window, drawer) = await HelpShell(folder, UiSettings.Load(path));

        try
        {
            main.ShowHelp();
            window.UpdateLayout();
            Assert.Equal(420.0, drawer.Panel.ActualWidth, "420 px at first");

            drawer.WidthGrip.RaiseEvent(new DragDeltaEventArgs(-100, 0));    // left edge dragged 100 px to the left
            drawer.WidthGrip.RaiseEvent(new DragCompletedEventArgs(-100, 0, false));
            window.UpdateLayout();
            Assert.Equal(520.0, drawer.Panel.ActualWidth, "wider after the drag");
            Assert.Equal(520.0, UiSettings.Load(path).HelpDrawerWidth, "remembered for next time");

            window.Width = 960;
            window.UpdateLayout();
            Assert.True(drawer.Panel.ActualWidth <= window.HelpDrawer.ActualWidth / 2 + 0.5, $"half the window at most ({drawer.Panel.ActualWidth})");
        }
        finally
        {
            window.Close();
            HelpDrawer.AnimationsEnabled = () => SystemParameters.ClientAreaAnimation;
        }

        File.WriteAllText(path, "{ not json");
        Assert.Equal(null, UiSettings.Load(path).HelpDrawerWidth, "an unreadable file means the default");
    }

    [Test("HELP-D-02", "Search finds sections on every page; a result, the page selector and the On this page chips go to their section")]
    public static async Task HelpSearchAndNavigation()
    {
        HelpDrawer.AnimationsEnabled = () => false;
        using var folder = new TestFolder();
        var (main, window, drawer) = await HelpShell(folder);
        var help = main.Help;

        try
        {
            main.ShowHelp();
            help.SearchText = "threshold";
            window.UpdateLayout();
            Assert.True(help.SearchResults.Count > 0 && help.SearchResults.All(r => r.Snippet.Contains("threshold", StringComparison.OrdinalIgnoreCase)),
                "results contain the word");
            Assert.True(help.SearchResults.Any(r => r.Entry.Title == "Measurement"), "including Measurement");
            Assert.False(drawer.PageScroller.IsVisible, "results replace the page");

            var result = help.SearchResults.First(r => r.Entry.Title == "Measurement" && r.Section == HelpSections.Troubleshooting);
            help.OpenResultCommand.Execute(result);
            await Settle(window);
            Assert.Equal("Measurement", help.Page?.Page, "the result's page");
            Assert.Equal("", help.SearchText, "search cleared");
            Assert.True(drawer.PageScroller.IsVisible, "page shown again");
            Assert.Near(Math.Min(drawer.OffsetOf(HelpSections.Troubleshooting), drawer.PageScroller.ScrollableHeight),
                drawer.PageScroller.VerticalOffset, 2, "scrolled to its section");

            help.SearchText = "zzzz no such words";
            Assert.True(help.HasNoResults, "nothing found says so");
            help.SearchText = "";

            var selector = Within<ComboBox>(drawer, "HelpPageSelector");
            Assert.Sequence(new[] { "Core Information", "Measurement", "Monitoring", "Upload Data", "Data Viewer", "Keyboard shortcuts" },
                help.Entries.Select(e => e.Title).ToArray(), "page selector entries");
            selector.SelectedItem = help.Entries[^1];
            window.UpdateLayout();
            Assert.True(help.IsShortcutsShown && help.Page is null, "Keyboard shortcuts shown");
            selector.SelectedItem = help.Entries[1];
            await Settle(window);

            Assert.Sequence(new[] { "Before you start", "Steps", "Buttons and fields", "Troubleshooting" },
                help.Chips.Select(c => c.Title).ToArray(), "On this page chips");

            foreach (var chip in help.Chips)
            {
                help.ScrollToSectionCommand.Execute(chip.Section);
                await Settle(window);
                Assert.Near(Math.Min(drawer.OffsetOf(chip.Section), drawer.PageScroller.ScrollableHeight), drawer.PageScroller.VerticalOffset, 2,
                    $"{chip.Title}: scrolled to it");
                Assert.True(chip.IsActive && help.Chips.Count(c => c.IsActive) == 1, $"{chip.Title}: highlighted");
            }

            // Scrolling by hand moves the highlight.
            await Task.Delay(450);
            drawer.PageScroller.ScrollToVerticalOffset(drawer.OffsetOf(HelpSections.Steps) + 30);
            await Assert.Eventually(() => help.ActiveSection == HelpSections.Steps, $"Steps highlighted while in view ({help.ActiveSection})");
        }
        finally
        {
            window.Close();
            HelpDrawer.AnimationsEnabled = () => SystemParameters.ClientAreaAnimation;
        }
    }

    [Test("HELP-D-03", "Focus: opening moves it into Help, closing returns it; Ctrl+F only works while open; Tab can leave Help")]
    public static async Task HelpFocus()
    {
        HelpDrawer.AnimationsEnabled = () => false;
        using var folder = new TestFolder();
        var (main, window, drawer) = await HelpShell(folder);

        try
        {
            var operatorBox = Descendants(window).OfType<TextBox>().First(t => t.IsVisible && AutomationProperties.GetName(t) == "Operator");
            operatorBox.Focus();
            main.ShowHelp();
            await Settle(window);
            Assert.True(ReferenceEquals(FocusManager.GetFocusedElement(window), drawer.SearchField), "focus moved to the search box");

            main.Help.Close();
            await Settle(window);
            Assert.True(ReferenceEquals(FocusManager.GetFocusedElement(window), operatorBox), "focus back on Operator");

            Assert.Equal(KeyboardNavigationMode.Continue, KeyboardNavigation.GetTabNavigation(drawer.Panel), "Tab is not trapped in Help");
            Assert.Equal(KeyboardNavigationMode.Continue, KeyboardNavigation.GetTabNavigation(drawer), "nor in its overlay");
        }
        finally
        {
            window.Close();
            HelpDrawer.AnimationsEnabled = () => SystemParameters.ClientAreaAnimation;
        }
    }

    [Test("HELP-D-04", "Slide: 200 ms in and 150 ms out when Windows animates, instant when animations are off; starts closed")]
    public static async Task HelpSlides()
    {
        using var folder = new TestFolder();
        HelpDrawer.AnimationsEnabled = () => true;
        var (main, window, drawer) = await HelpShell(folder);

        try
        {
            main.ShowHelp();
            Assert.True(drawer.SlideOffset > 0, $"starts off to the right ({drawer.SlideOffset})");
            await Assert.Eventually(() => drawer.SlideOffset == 0, "slides in", 1000);
            main.Help.Close();
            Assert.True(drawer.Panel.IsVisible, "still sliding out");
            await Assert.Eventually(() => !drawer.Panel.IsVisible, "then hidden", 1000);

            HelpDrawer.AnimationsEnabled = () => false;
            main.ShowHelp();
            Assert.Equal(0.0, drawer.SlideOffset, "animations off: in at once");
            main.Help.Close();
            Assert.False(drawer.Panel.IsVisible, "and out at once");
        }
        finally
        {
            window.Close();
            HelpDrawer.AnimationsEnabled = () => SystemParameters.ClientAreaAnimation;
        }
    }

    [Test("HELP-D-05", "Every page's screenshot markers match a numbered Buttons and fields entry, and render on the picture")]
    public static async Task HelpMarkers()
    {
        var settings = HelpSettings.Load(AppContext.BaseDirectory);

        foreach (var page in settings.Pages)
        {
            var numbered = page.Items.Where(i => i.Marker is not null).Select(i => i.Marker!.Value).OrderBy(n => n).ToArray();
            Assert.Equal(string.Join(",", numbered), string.Join(",", page.Markers.Select(m => m.N).OrderBy(n => n)),
                $"{page.Page}: one marker per numbered item");
            Assert.Equal(string.Join(",", Enumerable.Range(1, numbered.Length)), string.Join(",", numbered), $"{page.Page}: numbered 1, 2, 3…");
        }

        HelpDrawer.AnimationsEnabled = () => false;
        using var folder = new TestFolder();
        var (main, window, drawer) = await HelpShell(folder);

        try
        {
            main.ShowPage(main.Measurement);
            main.ShowHelp();
            await Settle(window);
            var shot = Within<Button>(drawer, "HelpScreenshot");
            var markers = Descendants(shot).OfType<Border>().Where(b => b.Child is TextBlock { Text: { Length: > 0 } }).ToList();
            Assert.Equal(main.Help.Page!.Markers.Count, markers.Count, "a circle per marker");
            var image = Descendants(shot).OfType<Image>().Single();
            Assert.True(image.ActualWidth > 300 && image.ActualWidth <= drawer.Panel.ActualWidth, $"the picture fits the drawer ({image.ActualWidth})");

            foreach (var circle in markers)
            {
                var centre = circle.TranslatePoint(new Point(circle.ActualWidth / 2, circle.ActualHeight / 2), image);
                Assert.True(centre.X >= 0 && centre.X <= image.ActualWidth && centre.Y >= 0 && centre.Y <= image.ActualHeight,
                    $"marker {((TextBlock)circle.Child).Text} on the picture");
            }
        }
        finally
        {
            window.Close();
            HelpDrawer.AnimationsEnabled = () => SystemParameters.ClientAreaAnimation;
        }
    }

    [Test("FE-HELP-09", "Help search: the caret, the 'Search help' placeholder and typed text all start at one point after the icon")]
    public static async Task HelpSearchPlaceholderAlignsWithCaret()
    {
        HelpDrawer.AnimationsEnabled = () => false;
        using var folder = new TestFolder();
        var (main, window, drawer) = await HelpShell(folder);

        try
        {
            main.ShowHelp();
            await Settle(window);
            var box = drawer.SearchField;
            var hint = (TextBlock)box.Template.FindName("Hint", box);
            double HintTextLeft() => hint.TranslatePoint(new Point(hint.Padding.Left, 0), box).X;

            Assert.Equal("Search help", hint.Text, "placeholder text");
            Assert.True(hint.IsVisible, "placeholder shown while empty");
            var caret = box.GetRectFromCharacterIndex(0).Left;
            Assert.True(caret > 30, $"text starts after the icon ({caret})");
            Assert.Near(caret, HintTextLeft(), 0.5, "placeholder's first letter is where the caret is");

            box.Text = "a";
            window.UpdateLayout();
            Assert.False(hint.IsVisible, "placeholder hidden once typed");
            Assert.Near(caret, box.GetRectFromCharacterIndex(0).Left, 0.5, "typed text starts at the same point");

            box.Text = "";
            window.UpdateLayout();
            Assert.True(hint.IsVisible, "placeholder back when cleared");
            Assert.Near(caret, HintTextLeft(), 0.5, "in the same place");
            Assert.Near(caret, box.GetRectFromCharacterIndex(0).Left, 0.5, "caret back at the start");
        }
        finally
        {
            window.Close();
            HelpDrawer.AnimationsEnabled = () => SystemParameters.ClientAreaAnimation;
        }
    }
}
