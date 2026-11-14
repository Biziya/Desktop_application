using System.Windows;

namespace Frontend.Shared.Infrastructure;

/// <summary>
/// Marks the current page's sidebar button. A typed attached property lets the template use a plain property trigger
/// instead of a relative-source binding or a string Tag.
/// </summary>
public static class SidebarItem
{
    public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.RegisterAttached(
        "IsSelected", typeof(bool), typeof(SidebarItem), new FrameworkPropertyMetadata(false));

    public static bool GetIsSelected(DependencyObject element) => (bool)element.GetValue(IsSelectedProperty);

    public static void SetIsSelected(DependencyObject element, bool value) => element.SetValue(IsSelectedProperty, value);
}
