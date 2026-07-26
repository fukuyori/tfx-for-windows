using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Tfx;

/// <summary>
/// Shows the standard Windows properties sheet (the Explorer "Properties"
/// dialog) for a file, folder, or drive via ShellExecuteEx with the
/// "properties" verb. The sheet runs on a shell-owned thread inside this
/// process, so the call returns immediately and the dialog is modeless.
/// </summary>
internal static class ShellProperties
{
    private const uint SEE_MASK_INVOKEIDLIST = 0x0000000C;
    private const int SW_SHOWNORMAL = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellExecuteInfo
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpVerb;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpParameters;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr hProcess;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ShellExecuteEx(ref ShellExecuteInfo info);

    /// <summary>Opens the properties sheet for <paramref name="path"/>. Throws
    /// <see cref="Win32Exception"/> when the shell refuses.</summary>
    public static void Show(IntPtr ownerHandle, string path)
    {
        var info = new ShellExecuteInfo
        {
            cbSize = Marshal.SizeOf<ShellExecuteInfo>(),
            fMask = SEE_MASK_INVOKEIDLIST,
            hwnd = ownerHandle,
            lpVerb = "properties",
            lpFile = path,
            nShow = SW_SHOWNORMAL,
        };
        if (!ShellExecuteEx(ref info))
        {
            throw new Win32Exception();
        }
    }
}
