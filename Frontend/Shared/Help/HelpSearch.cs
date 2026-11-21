namespace Frontend.Shared.Help;

/// <summary>An entry of the page selector: a page's help, or Keyboard shortcuts (no page).</summary>
public sealed class HelpEntry(string title, HelpSettings.HelpPage? page)
{
    public const string ShortcutsTitle = "Keyboard shortcuts";

    public string Title { get; } = title;

    public HelpSettings.HelpPage? Page { get; } = page;

    public bool IsShortcuts => Page is null;

    public override string ToString() => Title;
}

/// <summary>An "On this page" chip: scrolls to its section; highlighted while that section is in view.</summary>
public sealed class HelpChip(string section) : Infrastructure.ObservableObject
{
    private bool isActive;

    public string Section { get; } = section;

    public string Title => HelpSections.Title(Section);

    public bool IsActive { get => isActive; set => Set(ref isActive, value); }
}

/// <summary>A search hit: where it is (entry and section) and the matching text.</summary>
public sealed record HelpSearchResult(HelpEntry Entry, string Section, string Snippet)
{
    public string Location => Section.Length == 0 ? Entry.Title : $"{Entry.Title} · {HelpSections.Title(Section)}";
}

/// <summary>Finds help text containing every word typed, case-insensitive, in page order.</summary>
public static class HelpSearch
{
    public const int MaxResults = 40;

    public static IReadOnlyList<HelpSearchResult> Find(IReadOnlyList<HelpEntry> entries, IReadOnlyList<HelpSettings.HelpShortcut> shortcuts, IReadOnlyList<HelpSettings.HelpItem> controls, string? text)
    {
        var words = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (words.Length == 0)
        {
            return [];
        }

        bool Matches(string candidate) => words.All(w => candidate.Contains(w, StringComparison.OrdinalIgnoreCase));

        var results = new List<HelpSearchResult>();

        foreach (var entry in entries)
        {
            foreach (var (section, snippet) in Pieces(entry, shortcuts, controls))
            {
                if (Matches(snippet) && results.Count < MaxResults)
                {
                    results.Add(new HelpSearchResult(entry, section, snippet));
                }
            }
        }

        return results;
    }

    private static IEnumerable<(string Section, string Text)> Pieces(HelpEntry entry, IReadOnlyList<HelpSettings.HelpShortcut> shortcuts, IReadOnlyList<HelpSettings.HelpItem> controls)
    {
        if (entry.Page is not { } page)
        {
            foreach (var shortcut in shortcuts)
            {
                yield return ("", $"{shortcut.Keys}: {shortcut.Does}");
            }

            foreach (var control in controls)
            {
                yield return ("", $"{control.Control}: {control.Does}");
            }

            yield break;
        }

        yield return ("", $"{page.Page}: {page.Purpose}");

        foreach (var line in page.BeforeYouStart)
        {
            yield return (HelpSections.BeforeYouStart, line);
        }

        foreach (var step in page.Steps)
        {
            yield return (HelpSections.Steps, step);
        }

        foreach (var item in page.Items)
        {
            yield return (HelpSections.Items, $"{item.Control}: {item.Does}");
        }

        foreach (var problem in page.Troubleshooting)
        {
            yield return (HelpSections.Troubleshooting, $"{problem.Problem} {problem.Fix}");
        }

        foreach (var callout in page.Callouts)
        {
            yield return (callout.Section, $"{callout.Label}: {callout.Text}");
        }
    }

}
