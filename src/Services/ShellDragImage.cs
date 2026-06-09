using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ComIDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace Tfx;

/// <summary>
/// Attaches an Explorer-style translucent drag image to a WPF
/// <see cref="DataObject"/> using the Windows shell <c>IDragSourceHelper</c>.
/// The shell then renders the bitmap following the cursor during
/// <see cref="System.Windows.DragDrop.DoDragDrop"/> — including over other apps —
/// exactly like a normal Explorer file drag.
/// </summary>
internal static class ShellDragImage
{
    /// <summary>
    /// Builds a small chip (icon glyph + label) and registers it as the drag
    /// image on <paramref name="data"/>. Best-effort: any failure leaves the drag
    /// working without a preview.
    /// </summary>
    public static void Attach(DataObject data, string glyph, string label, double dpiScale)
    {
        IntPtr hbmp = IntPtr.Zero;
        try
        {
            hbmp = RenderChip(glyph, label, dpiScale, out var width, out var height);
            if (hbmp == IntPtr.Zero)
            {
                return;
            }

            var image = new ShDragImage
            {
                sizeDragImage = new SIZE { cx = width, cy = height },
                // Where the cursor sits within the image (a little inside the
                // top-left corner, so the chip trails down-right of the pointer).
                ptOffset = new POINT { x = (int)(12 * dpiScale), y = (int)(10 * dpiScale) },
                hbmpDragImage = hbmp,
                crColorKey = 0
            };

            var helper = (IDragSourceHelper)new DragDropHelper();
            // The helper takes ownership of the HBITMAP on success and deletes it.
            var hr = helper.InitializeFromBitmap(ref image, (ComIDataObject)data);
            if (hr != 0)
            {
                DeleteObject(hbmp);
            }
        }
        catch
        {
            if (hbmp != IntPtr.Zero)
            {
                DeleteObject(hbmp);
            }
        }
    }

    /// <summary>Renders the chip visual to a top-down 32bpp DIB and returns its HBITMAP.</summary>
    private static IntPtr RenderChip(string glyph, string label, double dpiScale, out int pixelWidth, out int pixelHeight)
    {
        pixelWidth = 0;
        pixelHeight = 0;
        if (dpiScale <= 0)
        {
            dpiScale = 1.0;
        }

        var iconFont = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        var textFont = new FontFamily("Segoe UI, Yu Gothic UI");

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text = glyph,
            FontFamily = iconFont,
            FontSize = 16,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontFamily = textFont,
            FontSize = 13,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 260
        });

        var chip = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x2B, 0x2B, 0x2B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(9, 5, 11, 5),
            Child = panel
        };

        chip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        chip.Arrange(new Rect(chip.DesiredSize));
        chip.UpdateLayout();

        var w = chip.DesiredSize.Width;
        var h = chip.DesiredSize.Height;
        if (w <= 0 || h <= 0)
        {
            return IntPtr.Zero;
        }

        pixelWidth = Math.Max(1, (int)Math.Ceiling(w * dpiScale));
        pixelHeight = Math.Max(1, (int)Math.Ceiling(h * dpiScale));

        var rtb = new RenderTargetBitmap(pixelWidth, pixelHeight, 96 * dpiScale, 96 * dpiScale, PixelFormats.Pbgra32);
        rtb.Render(chip);

        var stride = pixelWidth * 4;
        var bits = new byte[stride * pixelHeight];
        rtb.CopyPixels(bits, stride, 0);

        var header = new BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = pixelWidth,
            biHeight = -pixelHeight,   // negative = top-down, matching CopyPixels order
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0          // BI_RGB
        };

        var hbmp = CreateDIBSection(IntPtr.Zero, ref header, 0, out var ppvBits, IntPtr.Zero, 0);
        if (hbmp == IntPtr.Zero || ppvBits == IntPtr.Zero)
        {
            if (hbmp != IntPtr.Zero)
            {
                DeleteObject(hbmp);
            }
            return IntPtr.Zero;
        }

        // RenderTargetBitmap Pbgra32 is premultiplied BGRA — exactly what the
        // shell drag-image helper expects for a per-pixel-alpha bitmap.
        Marshal.Copy(bits, 0, ppvBits, bits.Length);
        return hbmp;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ShDragImage
    {
        public SIZE sizeDragImage;
        public POINT ptOffset;
        public IntPtr hbmpDragImage;
        public int crColorKey;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [ComImport]
    [Guid("4657278A-411B-11d2-839A-00C04FD918D0")]
    private class DragDropHelper
    {
    }

    [ComImport]
    [Guid("DE5BF786-477A-11D2-839D-00C04FD918D0")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDragSourceHelper
    {
        [PreserveSig]
        int InitializeFromBitmap(ref ShDragImage pshdi, ComIDataObject pDataObject);

        [PreserveSig]
        int InitializeFromWindow(IntPtr hwnd, ref POINT ppt, ComIDataObject pDataObject);
    }

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateDIBSection(
        IntPtr hdc,
        ref BITMAPINFOHEADER pbmi,
        uint usage,
        out IntPtr ppvBits,
        IntPtr hSection,
        uint offset);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);
}
