using Frontend.Shared.Infrastructure;

namespace Frontend.ViewModels;

/// <summary>One sidebar entry: its label, the Theme.xaml geometry key of its icon, and the page it shows.</summary>
public sealed class NavigationItem(string title, string iconKey, ObservableObject page) : ObservableObject
{
    private bool isSelected;

    public string Title { get; } = title;

    public string IconKey { get; } = iconKey;

    public ObservableObject Page { get; } = page;

    public bool IsSelected
    {
        get => isSelected;
        internal set => Set(ref isSelected, value);
    }
}
