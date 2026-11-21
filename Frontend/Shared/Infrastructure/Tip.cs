using System.Windows;
using System.Windows.Automation;

namespace Frontend.Shared.Infrastructure;

/// <summary>
/// <c>infra:Tip.Text="…"</c> sets the control's tooltip and its screen-reader help text
/// (<see cref="AutomationProperties.HelpTextProperty"/>) to the same short sentence. Works in style setters and
/// triggers, so a disabled control can explain why it is disabled. The texts live in Shared/Styles/ToolTips.xaml.
/// </summary>
public static class Tip
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(Tip), new FrameworkPropertyMetadata(null, TextChanged));

    public static string? GetText(DependencyObject element) => (string?)element.GetValue(TextProperty);

    public static void SetText(DependencyObject element, string? value) => element.SetValue(TextProperty, value);

    private static void TextChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        var text = e.NewValue as string;
        var empty = string.IsNullOrWhiteSpace(text);

        switch (element)
        {
            case FrameworkElement control when empty:
                control.ClearValue(FrameworkElement.ToolTipProperty);
                break;
            case FrameworkElement control:
                control.ToolTip = text;
                break;
            case FrameworkContentElement content when empty:
                content.ClearValue(FrameworkContentElement.ToolTipProperty);
                break;
            case FrameworkContentElement content:
                content.ToolTip = text;
                break;
        }

        if (empty)
        {
            element.ClearValue(AutomationProperties.HelpTextProperty);
        }
        else
        {
            AutomationProperties.SetHelpText(element, text);
        }
    }
}
