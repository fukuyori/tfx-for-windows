using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Tfx;

internal static class WindowTheme
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    public static void Apply(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var caption = ResourceColor(window, "TfxChrome", Color.FromRgb(0x10, 0x13, 0x16));
        var border = ResourceColor(window, "TfxBorder", Color.FromRgb(0x2D, 0x35, 0x3C));
        var text = ResourceColor(window, "TfxForeground", Color.FromRgb(0xD6, 0xDE, 0xE6));
        var dark = IsDark(caption) ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));

        var captionColor = ColorRef(caption);
        var borderColor = ColorRef(border);
        var textColor = ColorRef(text);

        _ = DwmSetWindowAttribute(handle, DwmwaCaptionColor, ref captionColor, sizeof(int));
        _ = DwmSetWindowAttribute(handle, DwmwaBorderColor, ref borderColor, sizeof(int));
        _ = DwmSetWindowAttribute(handle, DwmwaTextColor, ref textColor, sizeof(int));
    }

    private static Color ResourceColor(Window window, string key, Color fallback)
    {
        return window.TryFindResource(key) is SolidColorBrush brush ? brush.Color : fallback;
    }

    private static bool IsDark(Color color)
    {
        var luminance = (0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B);
        return luminance <= 140;
    }

    private static int ColorRef(Color color) =>
        color.R | (color.G << 8) | (color.B << 16);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int attributeValue,
        int attributeSize);
}
