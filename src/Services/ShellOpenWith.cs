using System.Runtime.InteropServices;

namespace Tfx;

internal static class ShellOpenWith
{
    private const int ERROR_CANCELLED = unchecked((int)0x800704C7);

    [Flags]
    private enum OpenAsInfoFlags : uint
    {
        AllowRegistration = 0x00000001,
        RegisterExt = 0x00000002,
        Exec = 0x00000004,
        ForceRegistration = 0x00000008,
        HideRegistration = 0x00000020,
        UrlProtocol = 0x00000040,
        FileIsUri = 0x00000080,
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenAsInfo
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string FileName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? FileClass;
        public OpenAsInfoFlags Flags;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, PreserveSig = false)]
    private static extern void SHOpenWithDialog(IntPtr hwndParent, ref OpenAsInfo info);

    public static bool Show(IntPtr ownerHandle, string path)
    {
        var info = new OpenAsInfo
        {
            FileName = path,
            FileClass = null,
            Flags = OpenAsInfoFlags.Exec | OpenAsInfoFlags.AllowRegistration | OpenAsInfoFlags.HideRegistration,
        };
        try
        {
            SHOpenWithDialog(ownerHandle, ref info);
            return true;
        }
        catch (COMException ex) when (ex.HResult == ERROR_CANCELLED)
        {
            return false;
        }
    }
}
