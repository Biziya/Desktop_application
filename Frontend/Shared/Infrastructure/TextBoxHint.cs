using System.Windows;

namespace Frontend.Shared.Infrastructure;

/// <summary>
/// Placeholder text for a TextBox, shown while it is empty. The TextBox template draws it at the same origin as the
/// typed text, so the caret, the placeholder's first letter and the first typed character all start at one point.
/// </summary>
public static class TextBoxHint
{
    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.RegisterAttached(
        "Placeholder", typeof(string), typeof(TextBoxHint), new FrameworkPropertyMetadata(null));

    public static string? GetPlaceholder(DependencyObject element) => (string?)element.GetValue(PlaceholderProperty);

    public static void SetPlaceholder(DependencyObject element, string? value) => element.SetValue(PlaceholderProperty, value);
}
