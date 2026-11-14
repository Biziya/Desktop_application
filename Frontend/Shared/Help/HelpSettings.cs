using System.Windows;
using Frontend.Shared.Infrastructure;

namespace Frontend.Shared.Help;

/// <summary>Config/help.yaml: the Help panel text, one section per sidebar page.</summary>
public sealed class HelpSettings
{
    public const string File = "Config/help.yaml";

    public const string Resource = "HelpSettings";

    public required List<HelpPage> Pages { get; init; }

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
        }

        return settings;
    }

    public HelpPage? For(string pageTitle) => Pages.FirstOrDefault(p => p.Page == pageTitle);

    public void AddTo(ResourceDictionary resources) => resources.Add(Resource, this);

    public sealed class HelpPage
    {
        public required string Page { get; init; }

        public required string Purpose { get; init; }

        public required List<HelpItem> Items { get; init; }
    }

    public sealed class HelpItem
    {
        public required string Control { get; init; }

        public required string Does { get; init; }
    }
}
