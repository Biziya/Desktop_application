using Frontend.Shared.Infrastructure;

namespace Frontend.Shared.Help;

/// <summary>
/// The Help panel beside the page (MainWindow): what each button of the current page does, from Config/help.yaml.
/// Opened by the Help button of every page header or F1; closed with its × or Esc.
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
        CloseCommand = new RelayCommand(_ => IsOpen = false);
    }

    public RelayCommand CloseCommand { get; }

    public bool IsOpen { get => isOpen; private set => Set(ref isOpen, value); }

    /// <summary>The section shown; null when help.yaml has none for the page.</summary>
    public HelpSettings.HelpPage? Page { get => page; private set => Set(ref page, value); }

    public void Open(string pageTitle)
    {
        Show(pageTitle);
        IsOpen = true;
    }

    public void Close() => IsOpen = false;

    /// <summary>Follows navigation while the panel is open.</summary>
    public void Show(string pageTitle) => Page = settings?.For(pageTitle);
}
