using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Tfx;

internal static class ShellThumbnail
{
    private const int BiggerSizeOk = 0x1;
    private const int ThumbnailOnly = 0x8;
    private const int InCacheOnly = 0x10;

    /// <summary>
    /// Returns the Windows shell thumbnail for <paramref name="path"/>.
    /// </summary>
    /// <param name="cacheOnly">
    /// When <c>true</c> (default), only return a thumbnail that the OS already
    /// has cached and never trigger background generation. This guarantees the
    /// call is fast (returns either a bitmap or <c>null</c> within microseconds)
    /// — useful as an opportunistic fast path before a slower render. When
    /// <c>false</c>, the OS may block while generating the thumbnail.
    /// </param>
    public static BitmapSource? TryGetThumbnail(string path, int size, bool cacheOnly = true) =>
        TryGetThumbnail(path, size, cacheOnly, out _);

    /// <summary>
    /// Same as the simpler overload but reports why the call failed via
    /// <paramref name="error"/> so callers can surface a useful message in
    /// the preview pane (e.g. "0x80004005 from PDF shell extension").
    /// </summary>
    public static BitmapSource? TryGetThumbnail(string path, int size, bool cacheOnly, out string? error)
    {
        error = null;
        var itemId = typeof(IShellItemImageFactory).GUID;
        var hr = SHCreateItemFromParsingName(path, IntPtr.Zero, ref itemId, out var factory);
        if (hr != 0 || factory is null)
        {
            error = $"SHCreateItemFromParsingName failed (0x{hr:X8})";
            return null;
        }

        var hBitmap = IntPtr.Zero;
        try
        {
            var thumbnailSize = new SIZE(size, size);
            var flags = ThumbnailOnly | BiggerSizeOk;
            if (cacheOnly)
            {
                flags |= InCacheOnly;
            }
            factory.GetImage(thumbnailSize, flags, out hBitmap);
            if (hBitmap == IntPtr.Zero)
            {
                error = cacheOnly
                    ? "no cached thumbnail"
                    : "no thumbnail produced (no PDF shell handler returned an image)";
                return null;
            }

            var source = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (Exception ex)
        {
            error = $"{ex.GetType().Name}: {ex.Message}";
            return null;
        }
        finally
        {
            if (hBitmap != IntPtr.Zero)
            {
                DeleteObject(hBitmap);
            }

            Marshal.ReleaseComObject(factory);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        string pszPath,
        IntPtr pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory? ppv);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    private interface IShellItemImageFactory
    {
        void GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct SIZE(int cx, int cy)
    {
        public readonly int Cx = cx;
        public readonly int Cy = cy;
    }
}
