using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Frontend.Shared.Infrastructure;

/// <summary>
/// Colours the standard Windows title bar like the sidebar (SidebarBackground, white title text). The system
/// minimize / maximize / close buttons, snapping and dragging stay as they are. Windows 10 has no caption colour, so
/// there the call fails quietly and the default title bar stays.
/// </summary>
public static class WindowCaption
{
    private const int BorderColor = 34;   // DWMWA_BORDER_COLOR
    private const int CaptionColor = 35;  // DWMWA_CAPTION_COLOR
    private const int TextColor = 36;     // DWMWA_TEXT_COLOR

    public static void Apply(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            var background = ColorOf(window, "SidebarBackground", Color.FromRgb(0x14, 0x21, 0x3D));
            var text = ColorOf(window, "SidebarTextStrong", Colors.White);

            Set(handle, CaptionColor, background);
            Set(handle, BorderColor, background);
            Set(handle, TextColor, text);
        };
    }

    private static Color ColorOf(Window window, string key, Color fallback) =>
        window.TryFindResource(key) is SolidColorBrush brush ? brush.Color : fallback;

    // COLORREF is 0x00BBGGRR. The HRESULT is ignored on purpose: an older Windows simply keeps its own colours.
    private static void Set(IntPtr handle, int attribute, Color color)
    {
        var value = color.R | (color.G << 8) | (color.B << 16);
        _ = DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
