using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Frontend.Shared.Help;

namespace Ct007.Tests.FrontendTests;

/// <summary>
/// TIP-01…05: the app shows no tooltips and no ⓘ icons. Field and button explanations are in the Help drawer
/// ("Buttons and fields"); controls keep their screen-reader names and help texts.
/// </summary>
public static partial class GammaCoreLogUpdateTests
{
    [Test("TIP-01", "No tooltip and no ⓘ icon on any page, in Help or its lightbox; icon-only buttons keep a screen-reader name")]
    public static async Task NoToolTipsAnywhere()
    {
        Wpf.EnsureResources();
        using var folder = new TestFolder();
        var (main, _) = await Shell(folder, HelpSettings.Load(AppContext.BaseDirectory));
        var window = new Frontend.MainWindow(main) { ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20_000 };

        try
        {
            window.Show();
            var checkedElements = 0;
            var iconButtons = 0;

            void Check(string page)
            {
                window.UpdateLayout();

                foreach (var element in Descendants(window))
                {
                    var tip = element switch
                    {
                        FrameworkElement e => e.ToolTip,
                        FrameworkContentElement e => e.ToolTip,
                        _ => null
                    };
                    var what = $"{page}: {AutomationProperties.GetName(element)} {AutomationProperties.GetAutomationId(element)} ({element.GetType().Name})";
                    Assert.True(tip is null, $"{what} has no tooltip");
                    Assert.False(AutomationProperties.GetAutomationId(element) == "InfoIcon", $"{what} is not an ⓘ icon");
                    checkedElements++;

                    if (element is ButtonBase { IsVisible: true, TemplatedParent: null } button && IsIconOnly(button))
                    {
                        Assert.True(AutomationProperties.GetName(button).Length > 0 || AutomationProperties.GetHelpText(button).Length > 0,
                            $"{what} has a screen-reader name");
                        iconButtons++;
                    }
                }
            }

            main.ShowPage(main.Core);
            Check("Core Information (Existing)");
            main.Core.IsNewModeSelected = true;
            Check("Core Information (New)");
            main.Core.IsExistingModeSelected = true;

            await Arm(main);
            main.Measurement.Threshold = "150";
            Check("Measurement");
            main.ShowPage(main.Monitoring);
            Check("Monitoring");
            main.ShowPage(main.UploadData);
            Check("Upload Data");
            main.ShowPage(main.DataViewer);
            Check("Data Viewer");
            main.ToggleSidebarCommand.Execute(null);
            Check("Collapsed sidebar");
            main.ToggleSidebarCommand.Execute(null);
            main.ShowHelp();
            Check("Help drawer");
            main.Help.OpenLightboxCommand.Execute(main.Help.Page);
            Check("Help lightbox");

            Assert.True(checkedElements > 500 && iconButtons >= 10, $"elements checked ({checkedElements}), icon buttons ({iconButtons})");
        }
        finally
        {
            window.Close();
        }
    }

    [Test("TIP-02", "No tooltip style, timing or ⓘ icon style is left in the app resources")]
    public static void NoToolTipResources()
    {
        Wpf.EnsureResources();
        var keys = new List<object>();

        void Collect(ResourceDictionary dictionary)
        {
            keys.AddRange(dictionary.Keys.Cast<object>());

            foreach (var merged in dictionary.MergedDictionaries)
            {
                Collect(merged);
            }
        }

        Collect(Application.Current.Resources);
        Assert.False(keys.Contains(typeof(ToolTip)), "no implicit ToolTip style");
        Assert.False(keys.OfType<string>().Any(k => k.Contains("InfoIcon", StringComparison.Ordinal) || k.Contains("ToolTip", StringComparison.OrdinalIgnoreCase)),
            "no ⓘ icon or tooltip resources");
        Assert.Equal(ToolTipService.InitialShowDelayProperty.DefaultMetadata.DefaultValue,
            ToolTipService.GetInitialShowDelay(new Button()), "WPF's own tooltip timing (no app-wide override)");
    }

    [Test("TIP-03", "Save & Continue keeps its screen-reader help text: disabled in New mode it says what is missing")]
    public static async Task SaveAndContinueHelpText()
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
            Assert.Equal("Save and go to Measurement", AutomationProperties.GetHelpText(save), "Existing: enabled");
            Assert.True(save.ToolTip is null, "no tooltip");

            main.Core.IsNewModeSelected = true;
            window.UpdateLayout();
            Assert.False(save.IsEnabled, "New, empty: disabled");
            Assert.Equal("Fill Drillhole, Project and Campaign to continue", AutomationProperties.GetHelpText(save), "says why");

            main.Core.NewDrillholeId = "DH-TIP-1";
            main.Core.NewProject.Picked = main.Core.NewProject.Choices.First(c => !c.IsAddNew);
            main.Core.NewCampaign.Picked = main.Core.NewCampaign.Choices.First(c => !c.IsAddNew);
            CommandManager.InvalidateRequerySuggested();
            await Assert.Eventually(() => { window.UpdateLayout(); return save.IsEnabled; }, "enabled once filled");
            Assert.Equal("Save and go to Measurement", AutomationProperties.GetHelpText(save), "enabled text");
        }
        finally
        {
            window.Close();
        }
    }

    [Test("TIP-04", "Help explains every field and button that used to have a tooltip or an ⓘ icon")]
    public static void HelpCoversFormerToolTips()
    {
        var help = HelpSettings.Load(AppContext.BaseDirectory);
        var expected = new Dictionary<string, string[]>
        {
            ["Core Information"] =
            [
                "Existing / New", "Drillhole", "New drillhole", "Project / Campaign", "Operator / Notes", "Save & Continue",
                "Device Status", "Retry", "Select Database / Change Database", "Local tag", "Bluetooth device"
            ],
            ["Measurement"] =
            [
                "Start / Stop", "Starting Depth (m)", "Step (cm)", "Capture Countdown", "Threshold (CPS)", "Bell", "Live",
                "Eye icon", "Graph", "Export CSV", "New Measurement", "Delete (✕)"
            ],
            ["Monitoring"] = ["State", "Open Core Information", "Take sample", "Response", "Dose rate / Coverage / HDM", "Recent readings"],
            ["Upload Data"] = ["Import CSV", "Uploading User", "Upload Data", "Delete (✕)"],
            ["Data Viewer"] = ["Drillhole", "Refresh", "Retry"]
        };

        foreach (var (title, controls) in expected)
        {
            var page = help.Pages.Single(p => p.Page == title);

            foreach (var control in controls)
            {
                Assert.True(page.Items.Any(i => i.Control == control), $"{title}: Buttons and fields explains {control}");
            }
        }

        // Disabled controls used to say why in their tooltip; Help's Troubleshooting says it now.
        Assert.True(help.Pages.Single(p => p.Page == "Core Information").Troubleshooting.Any(t => t.Problem.Contains("can't be changed")),
            "locked drillhole");
        Assert.True(help.Pages.Single(p => p.Page == "Measurement").Troubleshooting.Count(t => t.Problem.Contains("greyed out")) >= 3,
            "Start, New Measurement and Export CSV greyed out");

        string[] everyPage = ["« / »", "Online / Offline", "? button", "× on a message", "× in Help", "Search", "Page list", "Screenshot", "Left edge of Help"];

        foreach (var control in everyPage)
        {
            Assert.True(help.Controls.Any(c => c.Control == control), $"Controls on every page: {control}");
        }

        Assert.False(help.Pages.SelectMany(p => p.Callouts).Any(c => c.Text.Contains('ⓘ') || c.Text.Contains("Hover", StringComparison.OrdinalIgnoreCase)),
            "no callout points to ⓘ icons or hovering");
    }

    [Test("TIP-05", "The source has no ToolTip, ToolTipService or ⓘ icon left (Frontend .cs, .xaml and .yaml)")]
    public static void SourceHasNoToolTips()
    {
        var frontend = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ThisFile())!, "..", "..", "Frontend"));
        Assert.True(Directory.Exists(frontend), $"source folder {frontend}");
        var skipped = new[] { "bin", "obj", "release" }.Select(d => Path.DirectorySeparatorChar + d + Path.DirectorySeparatorChar).ToArray();
        var files = Directory.EnumerateFiles(frontend, "*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs") || f.EndsWith(".xaml") || f.EndsWith(".yaml"))
            .Where(f => !skipped.Any(s => f.Contains(s, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        Assert.True(files.Count > 50, $"files scanned ({files.Count})");

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            var name = Path.GetRelativePath(frontend, file);
            // Only the comment wording "no tooltips" may mention them.
            var rest = Regex.Replace(text, @"\bno tooltips\b", "", RegexOptions.IgnoreCase);
            Assert.False(Regex.IsMatch(rest, "tooltip|InfoIcon", RegexOptions.IgnoreCase), $"{name} has no tooltip or ⓘ icon");
            Assert.False(text.Contains('ⓘ'), $"{name} has no ⓘ");
        }
    }

    private static string ThisFile([CallerFilePath] string path = "") => path;

    // A button that shows only an icon or a glyph (✕, ?, «), no words.
    private static bool IsIconOnly(ButtonBase button) => button.Content switch
    {
        string text => !text.Any(char.IsLetter),
        null => true,
        DependencyObject content => !Descendants(content).Prepend(content).OfType<TextBlock>().Any(t => t.Text.Any(char.IsLetter)),
        _ => false
    };
}
