using System.IO;
using System.Windows;
using Frontend.Shared.Infrastructure;

namespace Frontend.Shared.Help;

/// <summary>Config/help.yaml: the Help panel text, one section per sidebar page.</summary>
public sealed class HelpSettings
{
    public const string File = "Config/help.yaml";

    public const string Resource = "HelpSettings";

    public required List<HelpPage> Pages { get; init; }

    /// <summary>Keyboard and mouse shortcuts, shown as the last Help section. Optional.</summary>
    public List<HelpShortcut> Shortcuts { get; init; } = [];

    public bool HasShortcuts => Shortcuts.Count > 0;

    public static HelpSettings Load(string baseDirectory)
    {
        var settings = YamlSettings.Load<HelpSettings>(baseDirectory, File);

        for (var i = 0; i < settings.Pages.Count; i++)
        {
            var page = settings.Pages[i];

            if (string.IsNullOrWhiteSpace(page.Page))
            {
                throw YamlSettings.Invalid(File, $"pages[{i}].page", "a page title is required.");
            }

            if (settings.Pages.Take(i).Any(p => p.Page == page.Page))
            {
                throw YamlSettings.Invalid(File, $"pages[{i}].page", $"'{page.Page}' is listed twice.");
            }

            if (page.Items.Count == 0)
            {
                throw YamlSettings.Invalid(File, $"pages[{i}].items", "list at least one button or control.");
            }

            if (page.Image is { } image && (Path.IsPathRooted(image) || image.Contains("..", StringComparison.Ordinal)))
            {
                throw YamlSettings.Invalid(File, $"pages[{i}].image", "use a path relative to the app folder, e.g. Help/Images/measurement.png.");
            }

            page.BaseDirectory = baseDirectory;
        }

        return settings;
    }

    public HelpPage? For(string pageTitle) => Pages.FirstOrDefault(p => p.Page == pageTitle);

    public void AddTo(ResourceDictionary resources) => resources.Add(Resource, this);

    public sealed class HelpPage
    {
        public required string Page { get; init; }

        public required string Purpose { get; init; }

        /// <summary>What blocks the work on this page; shown first, in a highlighted box. Optional.</summary>
        public List<string> Important { get; init; } = [];

        public bool HasImportant => Important.Count > 0;

        public required List<HelpItem> Items { get; init; }

        /// <summary>Screenshot of the page, relative to the app folder (e.g. Help/Images/measurement.png). Optional.</summary>
        public string? Image { get; init; }

        internal string BaseDirectory { get; set; } = AppContext.BaseDirectory;

        /// <summary>Full path of <see cref="Image"/> when the file exists, otherwise null (no picture is shown).</summary>
        public string? ImagePath =>
            Image is { Length: > 0 } image && Path.Combine(BaseDirectory, image) is var path && System.IO.File.Exists(path) ? path : null;

        public bool HasImage => ImagePath is not null;
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
    }
}
