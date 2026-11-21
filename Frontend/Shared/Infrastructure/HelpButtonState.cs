using System.Windows;

namespace Frontend.Shared.Infrastructure;

/// <summary>
/// Whether the Help drawer is open, inherited from MainWindow by every element, so the ? button of each page header
/// can show its active look with a plain style trigger (no binding to the window).
/// </summary>
public static class HelpButtonState
{
    public static readonly DependencyProperty IsHelpOpenProperty = DependencyProperty.RegisterAttached(
        "IsHelpOpen", typeof(bool), typeof(HelpButtonState),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static bool GetIsHelpOpen(DependencyObject element) => (bool)element.GetValue(IsHelpOpenProperty);

    public static void SetIsHelpOpen(DependencyObject element, bool value) => element.SetValue(IsHelpOpenProperty, value);
}
