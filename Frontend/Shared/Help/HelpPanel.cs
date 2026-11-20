using Frontend.Shared.Infrastructure;

namespace Frontend.Shared.Help;

/// <summary>What asked for Help: the ? button opens it or brings it to the front; F1 toggles it.</summary>
public enum HelpTrigger
{
    Button,
    Key
}

/// <summary>
/// State of the single floating Help window (Shared/Help/HelpWindow), filled from Config/help.yaml. The window shows
/// every page as a section and scrolls to <see cref="Page"/>, the page the user is on.
/// </summary>
public sealed class HelpPanel : ObservableObject
{
    private readonly HelpSettings? settings;
    private bool isOpen;
    private HelpSettings.HelpPage? page;

    /// <param name="settings">help.yaml; null (tests) opens an empty panel.</param>
    public HelpPanel(HelpSettings? settings)
    {
        this.settings = settings;
        CloseCommand = new RelayCommand(_ => Close());
        ShowSectionCommand = new RelayCommand(p =>
        {
            if (p is HelpSettings.HelpPage section)
            {
                Page = section;
                SectionRequested?.Invoke();
            }
        });
    }

    /// <summary>Raised whenever Help must come to the front (opened, or ? pressed while already open).</summary>
    public event Action? FocusRequested;

    /// <summary>Raised whenever the window must scroll to <see cref="Page"/>'s section.</summary>
    public event Action? SectionRequested;

    public RelayCommand CloseCommand { get; }

    /// <summary>The section list on the left: scrolls to the clicked page's section.</summary>
    public RelayCommand ShowSectionCommand { get; }

    public bool IsOpen { get => isOpen; private set => Set(ref isOpen, value); }

    /// <summary>Section of the page on screen (the window scrolls to it).</summary>
    public HelpSettings.HelpPage? Page { get => page; private set => Set(ref page, value); }

    /// <summary>Every page's section, in sidebar order.</summary>
    public IReadOnlyList<HelpSettings.HelpPage> Pages => settings?.Pages ?? [];

    public IReadOnlyList<HelpSettings.HelpShortcut> Shortcuts => settings?.Shortcuts ?? [];

    public bool HasShortcuts => Shortcuts.Count > 0;

    /// <summary>
    /// The one entry point for the ? button and F1. Closed: opens at the current page's section. Open: F1 closes it;
    /// the ? button brings it to the front at the current page's section (never a second window, never nothing).
    /// </summary>
    public void Toggle(string pageTitle, HelpTrigger trigger)
    {
        if (IsOpen && trigger == HelpTrigger.Key)
        {
            Close();
            return;
        }

        Show(pageTitle);
        IsOpen = true;
        FocusRequested?.Invoke();
    }

    /// <summary>Opens Help (or brings it to the front) at <paramref name="pageTitle"/>'s section.</summary>
    public void Open(string pageTitle) => Toggle(pageTitle, HelpTrigger.Button);

    public void Close() => IsOpen = false;

    /// <summary>Follows navigation while the panel is open.</summary>
    public void Show(string pageTitle)
    {
        Page = settings?.For(pageTitle);
        SectionRequested?.Invoke();
    }
}
