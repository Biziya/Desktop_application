using System.Windows;

namespace Frontend.Shared.Infrastructure;

/// <summary>
/// Marks a sidebar navigation button as the page currently shown. A typed bool attached property, so the button
/// template can use a plain property trigger (no relative-source data binding inside the template, and no string
/// "True" compared with an untyped Tag). Bound one-way to MainViewModel.IsCoreSelected / IsMeasurementSelected.
/// </summary>
public static class SidebarItem
{
    public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.RegisterAttached(
        "IsSelected", typeof(bool), typeof(SidebarItem), new FrameworkPropertyMetadata(false));

    public static bool GetIsSelected(DependencyObject element) => (bool)element.GetValue(IsSelectedProperty);

    public static void SetIsSelected(DependencyObject element, bool value) => element.SetValue(IsSelectedProperty, value);
}
