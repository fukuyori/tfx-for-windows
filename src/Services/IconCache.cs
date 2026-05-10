using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Tfx;

internal static class IconCache
{
    private static readonly Dictionary<string, ImageSource?> _extensionCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ImageSource?> _extensionCacheLarge = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _cacheLock = new();
    private static ImageSource? _folderIcon;
    private static ImageSource? _folderIconLarge;

    public static ImageSource? GetFolderIcon()
    {
        lock (_cacheLock)
        {
            return _folderIcon ??= LoadShellIcon("folder", isDirectory: true, large: false);
        }
    }

    public static ImageSource? GetFolderIconLarge()
    {
        lock (_cacheLock)
        {
            return _folderIconLarge ??= LoadShellIcon("folder", isDirectory: true, large: true);
        }
    }

    public static ImageSource? GetFileIcon(string path) => GetFileIconCached(path, _extensionCache, large: false);

    public static ImageSource? GetFileIconLarge(string path) => GetFileIconCached(path, _extensionCacheLarge, large: true);

    private static ImageSource? GetFileIconCached(string path, Dictionary<string, ImageSource?> cache, bool large)
    {
        var ext = Path.GetExtension(path);
        var key = string.IsNullOrEmpty(ext) ? "__default__" : ext.ToLowerInvariant();

        lock (_cacheLock)
        {
            if (cache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var hint = key == "__default__" ? "file" : "file" + key;
            var icon = LoadShellIcon(hint, isDirectory: false, large: large);
            cache[key] = icon;
            return icon;
        }
    }

    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_LARGEICON = 0x0;
    private const uint SHGFI_SMALLICON = 0x1;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x10;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static ImageSource? LoadShellIcon(string path, bool isDirectory, bool large)
    {
        var info = new SHFILEINFO();
        var attr = isDirectory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
        var flags = SHGFI_ICON | SHGFI_USEFILEATTRIBUTES | (large ? SHGFI_LARGEICON : SHGFI_SMALLICON);

        var result = SHGetFileInfo(path, attr, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }
}
