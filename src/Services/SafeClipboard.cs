using System.Collections.Specialized;
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

    public static bool SetFileDropList(StringCollection paths) =>
        TrySet(() => Clipboard.SetFileDropList(paths));

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
