using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Tfx;

/// <summary>
/// Wraps the WPF <see cref="Clipboard"/> API with retries and exception
/// swallowing. The Win32 clipboard is a single shared resource: whenever
/// another process holds it open (clipboard history tools, RDP, Office),
/// every WPF call throws COMException (CLIPBRD_E_CANT_OPEN) — on the UI
/// thread that would take the whole app down. Getters fall back to an
/// empty/false result; setters report failure so callers can show a status.
/// </summary>
internal static class SafeClipboard
{
    private const int RetryCount = 3;
    private const int RetryDelayMs = 50;

    // CFSTR_PREFERREDDROPEFFECT — the standard cut-vs-copy marker Explorer and
    // other file managers put next to a CF_HDROP. DWORD: DROPEFFECT_COPY = 1,
    // DROPEFFECT_MOVE = 2.
    private const string PreferredDropEffectFormat = "Preferred DropEffect";
    private const int DropEffectCopy = 1;
    private const int DropEffectMove = 2;

    /// <summary>
    /// Puts the paths on the clipboard together with the standard
    /// CFSTR_PREFERREDDROPEFFECT marker, so other applications (and this one)
    /// can tell a cut from a copy the same way Explorer does.
    /// </summary>
    public static bool SetFileDropList(StringCollection paths, bool cut)
    {
        var data = new DataObject();
        data.SetFileDropList(paths);
        data.SetData(PreferredDropEffectFormat,
            new MemoryStream(BitConverter.GetBytes(cut ? DropEffectMove : DropEffectCopy)));
        return TrySet(() => Clipboard.SetDataObject(data, copy: true));
    }

    /// <summary>
    /// Reads CFSTR_PREFERREDDROPEFFECT: true = the source cut (paste should
    /// move), false = copied, null = the source app didn't say.
    /// </summary>
    public static bool? GetPreferredDropEffectIsMove()
    {
        if (Get(() => Clipboard.GetData(PreferredDropEffectFormat), null) is not MemoryStream stream)
        {
            return null;
        }

        try
        {
            var bytes = new byte[4];
            stream.Position = 0;
            if (stream.Read(bytes, 0, 4) == 4)
            {
                var effect = BitConverter.ToInt32(bytes, 0);
                if ((effect & DropEffectMove) != 0)
                {
                    return true;
                }
                if ((effect & DropEffectCopy) != 0)
                {
                    return false;
                }
            }
        }
        catch
        {
        }
        return null;
    }

    public static bool SetText(string text) =>
        TrySet(() => Clipboard.SetText(text));

    public static bool ContainsFileDropList() => Get(Clipboard.ContainsFileDropList, false);

    public static StringCollection GetFileDropList() => Get(Clipboard.GetFileDropList, []);

    public static bool ContainsText() => Get(Clipboard.ContainsText, false);

    public static string GetText() => Get(Clipboard.GetText, string.Empty);

    public static bool ContainsData(string format) => Get(() => Clipboard.ContainsData(format), false);

    public static object? GetData(string format) => Get(() => Clipboard.GetData(format), null);

    public static bool ContainsImage() => Get(Clipboard.ContainsImage, false);

    public static BitmapSource? GetImage() => Get(Clipboard.GetImage, null);

    public static IDataObject? GetDataObject() => Get(Clipboard.GetDataObject, null);

    private static bool TrySet(Action action)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception ex) when (IsClipboardBusy(ex))
            {
                if (attempt >= RetryCount)
                {
                    return false;
                }
                Thread.Sleep(RetryDelayMs);
            }
        }
    }

    private static T Get<T>(Func<T> getter, T fallback)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return getter();
            }
            catch (Exception ex) when (IsClipboardBusy(ex))
            {
                if (attempt >= RetryCount)
                {
                    return fallback;
                }
                Thread.Sleep(RetryDelayMs);
            }
        }
    }

    // ExternalException covers COMException's CLIPBRD_E_CANT_OPEN and the other
    // HRESULT failures the clipboard surfaces under contention.
    private static bool IsClipboardBusy(Exception ex) => ex is ExternalException;
}
