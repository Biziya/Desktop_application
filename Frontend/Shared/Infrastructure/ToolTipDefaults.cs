using System.Windows;
using System.Windows.Controls;

namespace Frontend.Shared.Infrastructure;

/// <summary>
/// App-wide tooltip timing and behaviour: shown after 400 ms, kept 10 s, 100 ms between neighbouring tooltips; also
/// shown on disabled controls (where they say why) and on keyboard focus. A control can still set its own values.
/// The look is the implicit ToolTip style in Shared/Styles/ToolTips.xaml.
/// </summary>
public static class ToolTipDefaults
{
    public const int InitialShowDelay = 400;
    public const int ShowDuration = 10_000;
    public const int BetweenShowDelay = 100;

    private static bool applied;

    /// <summary>Call once before the first window is created; later calls do nothing.</summary>
    public static void Apply()
    {
        if (applied)
        {
            return;
        }

        applied = true;

        foreach (var type in new[] { typeof(FrameworkElement), typeof(FrameworkContentElement) })
        {
            ToolTipService.InitialShowDelayProperty.OverrideMetadata(type, new FrameworkPropertyMetadata(InitialShowDelay));
            ToolTipService.ShowDurationProperty.OverrideMetadata(type, new FrameworkPropertyMetadata(ShowDuration));
            ToolTipService.BetweenShowDelayProperty.OverrideMetadata(type, new FrameworkPropertyMetadata(BetweenShowDelay));
            ToolTipService.ShowOnDisabledProperty.OverrideMetadata(type, new FrameworkPropertyMetadata(true));
            ToolTipService.ShowsToolTipOnKeyboardFocusProperty.OverrideMetadata(type, new FrameworkPropertyMetadata((bool?)true));
        }
    }
}
