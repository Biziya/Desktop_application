using System.IO;
using System.Windows;
using Frontend.Shared.Infrastructure;

namespace Frontend.Shared.Help;

/// <summary>
/// Config/help.yaml: the Help drawer text. One section per sidebar page, every one on the same template: purpose,
/// Before you start, screenshot (with numbered markers), Steps, Buttons and fields, Troubleshooting.
/// </summary>
public sealed class HelpSettings
{
    public const string File = "Config/help.yaml";

    public const string Resource = "HelpSettings";

    public static readonly string[] CalloutKinds = ["tip", "note", "warning"];

    public required List<HelpPage> Pages { get; init; }

    /// <summary>Keyboard and mouse shortcuts: the last entry of the page selector. Optional.</summary>
    public List<HelpShortcut> Shortcuts { get; init; } = [];

    public bool HasShortcuts => Shortcuts.Count > 0;

    /// <summary>
    /// Controls found on every page (sidebar, header, the Help drawer itself): shown under the shortcuts, in the same
    /// entry of the page selector. Optional.
    /// </summary>
    public List<HelpItem> Controls { get; init; } = [];

    public static HelpSettings Load(string baseDirectory)
    {
        var settings = YamlSettings.Load<HelpSettings>(baseDirectory, File);

        for (var i = 0; i < settings.Pages.Count; i++)
        {
            var page = settings.Pages[i];
            var key = $"pages[{i}]";

            if (string.IsNullOrWhiteSpace(page.Page))
            {
                throw YamlSettings.Invalid(File, $"{key}.page", "a page title is required.");
            }

            if (settings.Pages.Take(i).Any(p => p.Page == page.Page))
            {
                throw YamlSettings.Invalid(File, $"{key}.page", $"'{page.Page}' is listed twice.");
            }

            if (page.Steps.Count == 0)
            {
                throw YamlSettings.Invalid(File, $"{key}.steps", "list at least one step.");
            }

            if (page.Items.Count == 0)
            {
                throw YamlSettings.Invalid(File, $"{key}.items", "list at least one button or field.");
            }

            if (page.Troubleshooting.Count == 0)
            {
                throw YamlSettings.Invalid(File, $"{key}.troubleshooting", "list at least one common problem.");
            }

            if (page.Image is { } image && (Path.IsPathRooted(image) || image.Contains("..", StringComparison.Ordinal)))
            {
                throw YamlSettings.Invalid(File, $"{key}.image", "use a path relative to the app folder, e.g. Help/Images/measurement.png.");
            }

            for (var m = 0; m < page.Markers.Count; m++)
            {
                var marker = page.Markers[m];

                if (marker.X is <= 0 or >= 1 || marker.Y is <= 0 or >= 1)
                {
                    throw YamlSettings.Invalid(File, $"{key}.markers[{m}]", "x and y are fractions of the picture, between 0 and 1.");
                }

                if (page.Items.All(item => item.Marker != marker.N))
                {
                    throw YamlSettings.Invalid(File, $"{key}.markers[{m}].n", $"no item in 'items' has marker: {marker.N}.");
                }

                if (page.Markers.Take(m).Any(other => other.N == marker.N))
                {
                    throw YamlSettings.Invalid(File, $"{key}.markers[{m}].n", $"marker {marker.N} is listed twice.");
                }
            }

            for (var c = 0; c < page.Callouts.Count; c++)
            {
                var callout = page.Callouts[c];

                if (!CalloutKinds.Contains(callout.Kind))
                {
                    throw YamlSettings.Invalid(File, $"{key}.callouts[{c}].kind", "use tip, note or warning.");
                }

                if (!HelpSections.InPage.Contains(callout.Section))
                {
                    throw YamlSettings.Invalid(File, $"{key}.callouts[{c}].section", "use steps, items or troubleshooting.");
                }
            }

            page.BaseDirectory = baseDirectory;
        }

        return settings;
    }

    public HelpPage? For(string pageTitle) => Pages.FirstOrDefault(p => p.Page == pageTitle);

    public void AddTo(ResourceDictionary resources) => resources.Add(Resource, this);

    public sealed class HelpPage
    {
        /// <summary>1. Title: the sidebar title of the page.</summary>
        public required string Page { get; init; }

        /// <summary>1. One line: what the page is for.</summary>
        public required string Purpose { get; init; }

        /// <summary>2. What must be done first; shown in a Note callout. Optional.</summary>
        public List<string> BeforeYouStart { get; init; } = [];

        public bool HasBeforeYouStart => BeforeYouStart.Count > 0;

        /// <summary>3. Screenshot of the page, relative to the app folder (e.g. Help/Images/measurement.png). Optional.</summary>
        public string? Image { get; init; }

        /// <summary>Numbered markers on the screenshot; each number is the marker of an item.</summary>
        public List<HelpMarker> Markers { get; init; } = [];

        /// <summary>4. Numbered steps.</summary>
        public required List<string> Steps { get; init; }

        /// <summary>5. Buttons and fields.</summary>
        public required List<HelpItem> Items { get; init; }

        /// <summary>6. Common problems.</summary>
        public required List<HelpProblem> Troubleshooting { get; init; }

        /// <summary>Tip / Note / Warning boxes at the end of a section. Optional.</summary>
        public List<HelpCallout> Callouts { get; init; } = [];

        public IReadOnlyList<HelpCallout> StepsCallouts => Callouts.Where(c => c.Section == HelpSections.Steps).ToList();

        public IReadOnlyList<HelpCallout> ItemsCallouts => Callouts.Where(c => c.Section == HelpSections.Items).ToList();

        public IReadOnlyList<HelpCallout> TroubleshootingCallouts => Callouts.Where(c => c.Section == HelpSections.Troubleshooting).ToList();

        /// <summary>Steps with their numbers, for the numbered list.</summary>
        public IReadOnlyList<NumberedStep> NumberedSteps => Steps.Select((text, i) => new NumberedStep(i + 1, text)).ToList();

        internal string BaseDirectory { get; set; } = AppContext.BaseDirectory;

        /// <summary>Full path of <see cref="Image"/> when the file exists, otherwise null (no picture is shown).</summary>
        public string? ImagePath =>
            Image is { Length: > 0 } image && Path.Combine(BaseDirectory, image) is var path && System.IO.File.Exists(path) ? path : null;

        public bool HasImage => ImagePath is not null;

        public override string ToString() => Page;
    }

    public sealed record NumberedStep(int Number, string Text);

    public sealed class HelpMarker
    {
        public required int N { get; init; }

        /// <summary>Centre of the marker, as a fraction of the picture's width (0..1).</summary>
        public required double X { get; init; }

        /// <summary>Centre of the marker, as a fraction of the picture's height (0..1).</summary>
        public required double Y { get; init; }
    }

    public sealed class HelpShortcut
    {
        public required string Keys { get; init; }

        public required string Does { get; init; }
    }

    public sealed class HelpItem
    {
        public required string Control { get; init; }

        public required string Does { get; init; }

        /// <summary>Number of its marker on the screenshot. Optional.</summary>
        public int? Marker { get; init; }
    }

    public sealed class HelpProblem
    {
        public required string Problem { get; init; }

        public required string Fix { get; init; }
    }

    public sealed class HelpCallout
    {
        /// <summary>tip (blue), note (yellow) or warning (red).</summary>
        public required string Kind { get; init; }

        public required string Text { get; init; }

        /// <summary>The section it ends: steps, items or troubleshooting.</summary>
        public required string Section { get; init; }

        public string Label => Kind switch { "tip" => "Tip", "warning" => "Warning", _ => "Note" };
    }
}

/// <summary>The sections of every help page, in order (the "On this page" chips).</summary>
public static class HelpSections
{
    public const string BeforeYouStart = "before";
    public const string Steps = "steps";
    public const string Items = "items";
    public const string Troubleshooting = "troubleshooting";

    public static readonly string[] All = [BeforeYouStart, Steps, Items, Troubleshooting];

    /// <summary>Sections a callout can end.</summary>
    public static readonly string[] InPage = [Steps, Items, Troubleshooting];

    public static string Title(string section) => section switch
    {
        BeforeYouStart => "Before you start",
        Steps => "Steps",
        Items => "Buttons and fields",
        Troubleshooting => "Troubleshooting",
        _ => section
    };
}
