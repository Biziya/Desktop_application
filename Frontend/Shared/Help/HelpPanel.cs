using Frontend.Shared.Infrastructure;

namespace Frontend.Shared.Help;

/// <summary>
/// State of the Help drawer (Shared/Help/HelpDrawer), filled from Config/help.yaml. The drawer shows one entry at a
/// time: the help of the page on screen (it follows navigation) or Keyboard shortcuts. Search, the "On this page"
/// chips, the lightbox and the remembered width live here; animation, scrolling and focus are the view's.
/// </summary>
public sealed class HelpPanel : ObservableObject
{
    public const double DefaultWidth = 420;
    public const double MinWidth = 360;

    private readonly HelpSettings? settings;
    private readonly UiSettings ui;
    private readonly Dictionary<string, double> scrollOffsets = [];
    private bool isOpen;
    private bool dimsPage = true;
    private HelpEntry? selected;
    private string searchText = "";
    private IReadOnlyList<HelpSearchResult> searchResults = [];
    private string activeSection = HelpSections.BeforeYouStart;
    private HelpSettings.HelpPage? lightboxPage;

    /// <param name="settings">help.yaml; null (tests) opens an empty panel.</param>
    /// <param name="ui">Where the drawer width is remembered; null keeps it for this run only.</param>
    public HelpPanel(HelpSettings? settings, UiSettings? ui = null)
    {
        this.settings = settings;
        this.ui = ui ?? new UiSettings();
        Entries = [.. Pages.Select(p => new HelpEntry(p.Page, p)), .. HasShortcuts ? [new HelpEntry(HelpEntry.ShortcutsTitle, null)] : Array.Empty<HelpEntry>()];
        CloseCommand = new RelayCommand(_ => Close());
        ScrollToSectionCommand = new RelayCommand(p =>
        {
            if (p is string section)
            {
                ActiveSection = section;
                SectionRequested?.Invoke(section);
            }
        });
        OpenResultCommand = new RelayCommand(p =>
        {
            if (p is HelpSearchResult result)
            {
                Selected = result.Entry;
                SearchText = "";
                SectionRequested?.Invoke(result.Section);
            }
        });
        OpenLightboxCommand = new RelayCommand(p => LightboxPage = p as HelpSettings.HelpPage ?? Page);
        CloseLightboxCommand = new RelayCommand(_ => LightboxPage = null);
    }

    /// <summary>Raised to scroll the drawer to a section ("" = the top); <see cref="HelpSections"/> names them.</summary>
    public event Action<string>? SectionRequested;

    public RelayCommand CloseCommand { get; }

    /// <summary>"On this page" chips. Parameter: a <see cref="HelpSections"/> name.</summary>
    public RelayCommand ScrollToSectionCommand { get; }

    /// <summary>A search result: shows its entry and scrolls to its section.</summary>
    public RelayCommand OpenResultCommand { get; }

    /// <summary>Enlarges a page's screenshot. Parameter: the page (default: the one shown).</summary>
    public RelayCommand OpenLightboxCommand { get; }

    public RelayCommand CloseLightboxCommand { get; }

    public bool IsOpen
    {
        get => isOpen;
        private set
        {
            if (Set(ref isOpen, value))
            {
                Changed(nameof(ShowsBackdrop));
            }
        }
    }

    /// <summary>False on the Measurement page: no dim, no click-to-close, so the graph stays visible and usable.</summary>
    public bool DimsPage
    {
        get => dimsPage;
        set
        {
            if (Set(ref dimsPage, value))
            {
                Changed(nameof(ShowsBackdrop));
            }
        }
    }

    public bool ShowsBackdrop => IsOpen && DimsPage;

    /// <summary>The page selector: every page, then Keyboard shortcuts.</summary>
    public IReadOnlyList<HelpEntry> Entries { get; }

    /// <summary>The entry shown (the page selector's choice).</summary>
    public HelpEntry? Selected
    {
        get => selected;
        set
        {
            if (Set(ref selected, value))
            {
                Changed(nameof(Page));
                Changed(nameof(IsShortcutsShown));
                ActiveSection = value?.Page is { HasBeforeYouStart: false } ? HelpSections.Steps : HelpSections.BeforeYouStart;
                BuildChips();
            }
        }
    }

    /// <summary>The page whose help is shown; null while Keyboard shortcuts is shown.</summary>
    public HelpSettings.HelpPage? Page => Selected?.Page;

    public bool IsShortcutsShown => Selected?.IsShortcuts == true;

    /// <summary>Every page's help, in sidebar order.</summary>
    public IReadOnlyList<HelpSettings.HelpPage> Pages => settings?.Pages ?? [];

    public IReadOnlyList<HelpSettings.HelpShortcut> Shortcuts => settings?.Shortcuts ?? [];

    public IReadOnlyList<HelpSettings.HelpItem> Controls => settings?.Controls ?? [];

    public bool HasControls => Controls.Count > 0;

    public bool HasShortcuts => Shortcuts.Count > 0;

    /// <summary>Search box text; while not empty, results replace the page.</summary>
    public string SearchText
    {
        get => searchText;
        set
        {
            if (Set(ref searchText, value ?? ""))
            {
                SearchResults = HelpSearch.Find(Entries, Shortcuts, Controls, searchText);
                Changed(nameof(IsSearching));
            }
        }
    }

    public bool IsSearching => SearchText.Trim().Length > 0;

    public IReadOnlyList<HelpSearchResult> SearchResults
    {
        get => searchResults;
        private set
        {
            if (Set(ref searchResults, value))
            {
                Changed(nameof(HasNoResults));
            }
        }
    }

    public bool HasNoResults => IsSearching && SearchResults.Count == 0;

    /// <summary>The section in view (its chip is highlighted). Set by the drawer as it scrolls.</summary>
    public string ActiveSection
    {
        get => activeSection;
        set
        {
            if (Set(ref activeSection, value))
            {
                foreach (var chip in Chips)
                {
                    chip.IsActive = chip.Section == value;
                }
            }
        }
    }

    /// <summary>"On this page" chips of the page shown (Before you start only when the page has it).</summary>
    public IReadOnlyList<HelpChip> Chips { get; private set; } = [];

    private void BuildChips()
    {
        Chips = Page is not { } page
            ? []
            : HelpSections.All.Where(s => s != HelpSections.BeforeYouStart || page.HasBeforeYouStart)
                .Select(s => new HelpChip(s) { IsActive = s == ActiveSection }).ToList();
        Changed(nameof(Chips));
    }

    /// <summary>The page whose screenshot is enlarged; null when the lightbox is closed.</summary>
    public HelpSettings.HelpPage? LightboxPage
    {
        get => lightboxPage;
        private set
        {
            if (Set(ref lightboxPage, value))
            {
                Changed(nameof(IsLightboxOpen));
            }
        }
    }

    public bool IsLightboxOpen => LightboxPage is not null;

    /// <summary>Width the user dragged the drawer to (remembered between runs); <see cref="DefaultWidth"/> until then.</summary>
    public double PreferredWidth => ui.HelpDrawerWidth ?? DefaultWidth;

    public void RememberWidth(double width)
    {
        ui.HelpDrawerWidth = Math.Round(width);
        ui.Save();
        Changed(nameof(PreferredWidth));
    }

    /// <summary>
    /// Drawer width for a window: 85% of it below 900 px; otherwise the preferred width, kept between 360 px and half
    /// the window.
    /// </summary>
    public static double WidthFor(double preferred, double windowWidth)
    {
        if (windowWidth < 900)
        {
            return Math.Round(windowWidth * 0.85);
        }

        return Math.Clamp(preferred, MinWidth, Math.Max(MinWidth, windowWidth / 2));
    }

    /// <summary>Scroll offset of an entry, remembered for this run (0 the first time).</summary>
    public double ScrollOffsetOf(HelpEntry entry) => scrollOffsets.GetValueOrDefault(entry.Title);

    public void RememberScrollOffset(HelpEntry entry, double offset) => scrollOffsets[entry.Title] = offset;

    /// <summary>The ? button and F1: closed, opens at <paramref name="pageTitle"/>'s help; open, closes.</summary>
    public void Toggle(string pageTitle)
    {
        if (IsOpen)
        {
            Close();
        }
        else
        {
            Open(pageTitle);
        }
    }

    /// <summary>Opens the drawer at <paramref name="pageTitle"/>'s help (also when already open).</summary>
    public void Open(string pageTitle)
    {
        Show(pageTitle);
        IsOpen = true;
    }

    /// <summary>Closes the lightbox and the drawer.</summary>
    public void Close()
    {
        LightboxPage = null;
        IsOpen = false;
    }

    /// <summary>Esc: the lightbox first, then the drawer. False when there was nothing to close.</summary>
    public bool CloseTopmost()
    {
        if (IsLightboxOpen)
        {
            LightboxPage = null;
            return true;
        }

        if (IsOpen)
        {
            Close();
            return true;
        }

        return false;
    }

    /// <summary>Shows <paramref name="pageTitle"/>'s help (navigation, also while closed). Clears the search.</summary>
    public void Show(string pageTitle)
    {
        if (Entries.FirstOrDefault(e => e.Title == pageTitle) is { } entry)
        {
            SearchText = "";
            Selected = entry;
        }
    }
}
