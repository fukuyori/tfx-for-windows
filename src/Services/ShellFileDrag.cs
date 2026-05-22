using System.Runtime.InteropServices;
using System.Windows;
using ComIDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;
using Path = System.IO.Path;

namespace Tfx;

/// <summary>
/// Starts a shell-native file drag so Explorer can show its standard right-drag menu.
/// </summary>
internal static class ShellFileDrag
{
    private const uint DROPEFFECT_COPY = 1;
    private const uint DROPEFFECT_MOVE = 2;
    private const uint DROPEFFECT_LINK = 4;
    private const int S_OK = 0;
    private const int DRAGDROP_S_DROP = 0x00040100;
    private const int DRAGDROP_S_CANCEL = 0x00040101;
    private const int DRAGDROP_S_USEDEFAULTCURSORS = 0x00040102;
    private const uint MK_LBUTTON = 0x0001;
    private const uint MK_RBUTTON = 0x0002;
    private const uint FileDropEffects = DROPEFFECT_COPY | DROPEFFECT_MOVE | DROPEFFECT_LINK;

    public static bool TryStartRightButtonDrag(IReadOnlyList<string> paths, out DragDropEffects effect)
    {
        effect = DragDropEffects.None;
        if (!TryGetCommonParent(paths, out var parent))
        {
            return false;
        }

        var parentPidl = IntPtr.Zero;
        var absolutePidls = new List<IntPtr>();
        var childPidls = new IntPtr[paths.Count];
        var childPidlArray = IntPtr.Zero;
        ComIDataObject? dataObject = null;

        try
        {
            if (!TryParseDisplayName(parent, out parentPidl))
            {
                return false;
            }

            if (!TryBuildChildPidlArray(paths, absolutePidls, childPidls))
            {
                return false;
            }

            childPidlArray = Marshal.AllocCoTaskMem(IntPtr.Size * childPidls.Length);
            Marshal.Copy(childPidls, 0, childPidlArray, childPidls.Length);

            var iidDataObject = typeof(ComIDataObject).GUID;
            var hr = SHCreateDataObject(parentPidl, (uint)childPidls.Length, childPidlArray, null, ref iidDataObject, out dataObject);
            if (hr != S_OK || dataObject is null)
            {
                return false;
            }

            hr = DoDragDrop(dataObject, new RightButtonDropSource(), FileDropEffects, out var nativeEffect);
            effect = (DragDropEffects)nativeEffect;
            return hr == S_OK || hr == DRAGDROP_S_DROP || hr == DRAGDROP_S_CANCEL;
        }
        catch
        {
            effect = DragDropEffects.None;
            return false;
        }
        finally
        {
            if (dataObject is not null && Marshal.IsComObject(dataObject))
            {
                Marshal.FinalReleaseComObject(dataObject);
            }

            if (childPidlArray != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(childPidlArray);
            }

            if (parentPidl != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(parentPidl);
            }

            foreach (var pidl in absolutePidls)
            {
                if (pidl != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(pidl);
                }
            }
        }
    }

    private static bool TryGetCommonParent(IReadOnlyList<string> paths, out string parent)
    {
        parent = "";
        if (paths.Count == 0)
        {
            return false;
        }

        var commonParent = Path.GetDirectoryName(paths[0]) ?? "";
        if (commonParent.Length == 0 ||
            paths.Any(p => !string.Equals(Path.GetDirectoryName(p), commonParent, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        parent = commonParent;
        return true;
    }

    private static bool TryBuildChildPidlArray(
        IReadOnlyList<string> paths,
        List<IntPtr> absolutePidls,
        IntPtr[] childPidls)
    {
        for (var i = 0; i < paths.Count; i++)
        {
            if (!TryParseDisplayName(paths[i], out var absolutePidl))
            {
                return false;
            }

            absolutePidls.Add(absolutePidl);
            childPidls[i] = ILFindLastID(absolutePidl);
            if (childPidls[i] == IntPtr.Zero)
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryParseDisplayName(string path, out IntPtr pidl)
    {
        var hr = SHParseDisplayName(path, IntPtr.Zero, out pidl, 0, out _);
        return hr == S_OK && pidl != IntPtr.Zero;
    }

    private sealed class RightButtonDropSource : IDropSource
    {
        public int QueryContinueDrag(bool fEscapePressed, uint grfKeyState)
        {
            if (fEscapePressed)
            {
                return DRAGDROP_S_CANCEL;
            }

            return (grfKeyState & (MK_LBUTTON | MK_RBUTTON)) == 0
                ? DRAGDROP_S_DROP
                : S_OK;
        }

        public int GiveFeedback(uint dwEffect) => DRAGDROP_S_USEDEFAULTCURSORS;
    }

    [ComImport]
    [Guid("00000121-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDropSource
    {
        [PreserveSig]
        int QueryContinueDrag([MarshalAs(UnmanagedType.Bool)] bool fEscapePressed, uint grfKeyState);

        [PreserveSig]
        int GiveFeedback(uint dwEffect);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHParseDisplayName(
        string pszName,
        IntPtr pbc,
        out IntPtr ppidl,
        uint sfgaoIn,
        out uint psfgaoOut);

    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern IntPtr ILFindLastID(IntPtr pidl);

    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHCreateDataObject(
        IntPtr pidlFolder,
        uint cidl,
        IntPtr apidl,
        ComIDataObject? pdtInner,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out ComIDataObject? ppv);

    [DllImport("ole32.dll", PreserveSig = true)]
    private static extern int DoDragDrop(
        ComIDataObject pDataObj,
        IDropSource pDropSource,
        uint dwOKEffects,
        out uint pdwEffect);
}
