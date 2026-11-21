using System.Collections;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Ct007.Tests.FrontendTests;

/// <summary>TIP-01…05: tooltips across the app (one shared style, ⓘ icons, short texts, disabled controls say why).</summary>
public static partial class GammaCoreLogUpdateTests
{
    [Test("TIP-01", "Every icon-only button and every radio button on every page has a tooltip, also given to screen readers")]
    public static async Task EveryIconButtonHasToolTip()
    {
        Wpf.EnsureResources();
        using var folder = new TestFolder();
        var (main, _) = await Shell(folder);
        var window = new Frontend.MainWindow(main) { ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20_000 };

        try
        {
            window.Show();
            var checkedButtons = 0;

            void Check(string page)
            {
                window.UpdateLayout();

                foreach (var control in Descendants(window).OfType<ButtonBase>()
                             .Where(b => b.IsVisible && b.TemplatedParent is null && (IsIconOnly(b) || b is RadioButton)))
                {
                    var what = $"{page}: {AutomationProperties.GetName(control)} {AutomationProperties.GetAutomationId(control)} ({control.GetType().Name})";
                    Assert.True(control.ToolTip is string { Length: > 0 }, $"{what} has a tooltip");
                    Assert.Equal((string)control.ToolTip, AutomationProperties.GetHelpText(control), $"{what}: help text = tooltip");
                    checkedButtons++;
                }
            }

            main.ShowPage(main.Core);
            Check("Core Information (Existing)");
            main.Core.IsNewModeSelected = true;
            Check("Core Information (New)");
            main.Core.IsExistingModeSelected = true;

            await Arm(main);
            Check("Measurement");
            main.ShowPage(main.Monitoring);
            Check("Monitoring");
            main.ShowPage(main.UploadData);
            Check("Upload Data");
            main.ShowPage(main.DataViewer);
            Check("Data Viewer");

            Assert.True(checkedButtons >= 20, $"buttons checked ({checkedButtons})");
        }
        finally
        {
            window.Close();
        }
    }

    [Test("TIP-02", "Every tooltip text is one short sentence (about 12 words at most)")]
    public static void ToolTipTextsAreShort()
    {
        Wpf.EnsureResources();
        var texts = new Dictionary<string, string>();

        void Collect(ResourceDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry is { Key: string key, Value: string text } && (key.StartsWith("Tip", StringComparison.Ordinal) || key.EndsWith("ToolTip", StringComparison.Ordinal)))
                {
                    texts[key] = text;
                }
            }

            foreach (var merged in dictionary.MergedDictionaries)
            {
                Collect(merged);
            }
        }

        Collect(Application.Current.Resources);
        Assert.True(texts.Count >= 50, $"tooltip texts found ({texts.Count})");

        foreach (var (key, text) in texts)
        {
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            // Online / Offline explains both states, as worded in the spec.
            var limit = key == "TipConnection" ? 14 : 12;
            Assert.True(words <= limit, $"{key} has {words} words: \"{text}\"");
            Assert.False(text.Contains('\n'), $"{key} is one line");
        }
    }

    [Test("TIP-03", "Shared tooltip style (6 px corners, 280 px wide at most) and timing: 400 ms / 10 s / 100 ms, on disabled and focused controls")]
    public static void SharedStyleAndTiming()
    {
        Wpf.EnsureResources();
        var tip = new ToolTip { Content = "x" };
        Assert.True(Application.Current.FindResource(typeof(ToolTip)) is Style, "implicit ToolTip style");
        tip.Style = (Style)Application.Current.FindResource(typeof(ToolTip));
        tip.ApplyTemplate();
        var frame = (Border)tip.Template.FindName("Frame", tip);
        Assert.Equal(new CornerRadius(6), frame.CornerRadius, "corners");
        Assert.Equal(280.0, frame.MaxWidth, "max width");
        Assert.Equal(new Thickness(8, 6, 8, 6), frame.Padding, "padding");
        Assert.Equal(13.0, tip.FontSize, "font size");

        var button = new Button();
        Assert.Equal(400, ToolTipService.GetInitialShowDelay(button), "initial delay");
        Assert.Equal(10_000, ToolTipService.GetShowDuration(button), "shown for 10 s");
        Assert.Equal(100, ToolTipService.GetBetweenShowDelay(button), "between delay");
        Assert.True(ToolTipService.GetShowOnDisabled(button), "shown on disabled controls");
        Assert.True(ToolTipService.GetShowsToolTipOnKeyboardFocus(button) == true, "shown on keyboard focus");

        Tip.SetText(button, "Hello");
        Assert.Equal("Hello", button.ToolTip as string, "Tip.Text sets the tooltip");
        Assert.Equal("Hello", AutomationProperties.GetHelpText(button), "and the help text");
        Tip.SetText(button, null);
        Assert.True(button.ToolTip is null && AutomationProperties.GetHelpText(button).Length == 0, "cleared together");
    }

    [Test("TIP-04", "Save & Continue: disabled in New mode says what is missing; enabled says where it goes")]
    public static async Task SaveAndContinueExplainsDisabled()
    {
        Wpf.EnsureResources();
        using var folder = new TestFolder();
        var (main, _) = await Shell(folder);
        var window = new Frontend.MainWindow(main) { ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20_000 };

        try
        {
            window.Show();
            main.ShowPage(main.Core);
            window.UpdateLayout();
            var save = Descendants(window).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "SaveAndContinueButton");
            Assert.Equal("Save and go to Measurement", save.ToolTip as string, "Existing: enabled");

            main.Core.IsNewModeSelected = true;
            window.UpdateLayout();
            Assert.False(save.IsEnabled, "New, empty: disabled");
            Assert.Equal("Fill Drillhole, Project and Campaign to continue", save.ToolTip as string, "says why");
            Assert.Equal("Fill Drillhole, Project and Campaign to continue", AutomationProperties.GetHelpText(save), "help text too");

            main.Core.NewDrillholeId = "DH-TIP-1";
            main.Core.NewProject.Picked = main.Core.NewProject.Choices.First(c => !c.IsAddNew);
            main.Core.NewCampaign.Picked = main.Core.NewCampaign.Choices.First(c => !c.IsAddNew);
            CommandManager.InvalidateRequerySuggested();
            await Assert.Eventually(() => { window.UpdateLayout(); return save.IsEnabled; }, "enabled once filled");
            Assert.Equal("Save and go to Measurement", save.ToolTip as string, "enabled text");
        }
        finally
        {
            window.Close();
        }
    }

    [Test("TIP-05", "ⓘ icons beside Drillhole, Project, Campaign, Starting Depth, Step and Threshold: reachable with Tab, with their text")]
    public static async Task InfoIcons()
    {
        Wpf.EnsureResources();
        using var folder = new TestFolder();
        var (main, _) = await Shell(folder);
        var window = new Frontend.MainWindow(main) { ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20_000 };

        try
        {
            window.Show();

            Dictionary<string, string> Icons()
            {
                window.UpdateLayout();
                var icons = Descendants(window).OfType<Button>()
                    .Where(b => b.IsVisible && AutomationProperties.GetAutomationId(b) == "InfoIcon").ToList();

                foreach (var icon in icons)
                {
                    Assert.True(icon.Focusable && icon.IsTabStop, $"{AutomationProperties.GetName(icon)} is reachable with Tab");
                }

                return icons.ToDictionary(AutomationProperties.GetName, b => (string)b.ToolTip);
            }

            main.ShowPage(main.Core);
            var core = Icons();
            Assert.Equal("The drillhole you are measuring", core["About Drillhole"], "Drillhole");
            Assert.Equal("Project this drillhole belongs to", core["About Project"], "Project");
            Assert.Equal("Drilling campaign, usually the year", core["About Campaign"], "Campaign");

            main.Core.IsNewModeSelected = true;
            var coreNew = Icons();
            Assert.Equal("The drillhole you are measuring", coreNew["About Drillhole ID"], "Drillhole ID (New)");
            Assert.True(coreNew.ContainsKey("About Project") && coreNew.ContainsKey("About Campaign"), "Project and Campaign (New)");
            main.Core.IsExistingModeSelected = true;

            await Arm(main);
            var measurement = Icons();
            Assert.Equal("Depth where this measurement run begins", measurement["About Starting Depth"], "Starting Depth");
            Assert.Equal("Length of each measurement interval", measurement["About Step"], "Step");
            Assert.Equal("Readings above this value are flagged and shown as a red line", measurement["About Threshold"], "Threshold");
        }
        finally
        {
            window.Close();
        }
    }

    // A button that shows only an icon or a glyph (✕, ?, «), no words.
    private static bool IsIconOnly(ButtonBase button) => button.Content switch
    {
        string text => !text.Any(char.IsLetter),
        null => true,
        DependencyObject content => !Descendants(content).Prepend(content).OfType<TextBlock>().Any(t => t.Text.Any(char.IsLetter)),
        _ => false
    };
}
