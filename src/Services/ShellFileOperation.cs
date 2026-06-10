using System.Runtime.InteropServices;

namespace Tfx;

/// <summary>
/// Copies or moves files/folders through the Windows shell <c>IFileOperation</c>
/// (Vista+). The shell shows its standard progress dialog (time remaining, speed,
/// cancel) for longer operations, handles name collisions with the native
/// replace / skip / keep-both prompts, batches all items into a single dialog, and
/// records an undo entry. The progress dialog only appears once an operation runs
/// long enough — quick copies finish without any flicker.
/// </summary>
internal static class ShellFileOperation
{
    // FOF_* / FOFX_* operation flags.
    private const uint FOF_RENAMEONCOLLISION = 0x0008;
    private const uint FOF_NOCONFIRMMKDIR = 0x0200;
    private const uint FOFX_ADDUNDORECORD = 0x20000000;
    private const uint FOFX_SHOWELEVATIONPROMPT = 0x00040000;

    /// <summary>
    /// Copies (or moves, when <paramref name="move"/> is true) every source into
    /// <paramref name="destinationFolder"/>. Returns false if the operation could
    /// not be started; <paramref name="aborted"/> is true if the user cancelled or
    /// any item was skipped/failed.
    /// </summary>
    public static bool CopyOrMove(
        IntPtr ownerHwnd,
        IReadOnlyList<string> sources,
        string destinationFolder,
        bool move,
        bool renameOnCollision,
        out bool aborted)
    {
        aborted = false;
        if (sources.Count == 0)
        {
            return true;
        }

        IFileOperation? op = null;
        object? destObj = null;
        var iidShellItem = typeof(IShellItem).GUID;
        try
        {
            op = (IFileOperation)new FileOperation();
            var flags = FOF_NOCONFIRMMKDIR | FOFX_ADDUNDORECORD | FOFX_SHOWELEVATIONPROMPT;
            if (renameOnCollision)
            {
                // Auto-rename to "name - Copy" instead of erroring/prompting —
                // matches Explorer's same-folder paste behavior.
                flags |= FOF_RENAMEONCOLLISION;
            }
            op.SetOperationFlags(flags);
            if (ownerHwnd != IntPtr.Zero)
            {
                op.SetOwnerWindow(ownerHwnd);
            }

            SHCreateItemFromParsingName(destinationFolder, IntPtr.Zero, ref iidShellItem, out destObj);
            var destItem = (IShellItem)destObj;

            var items = new List<object>();
            try
            {
                foreach (var source in sources)
                {
                    SHCreateItemFromParsingName(source, IntPtr.Zero, ref iidShellItem, out var srcObj);
                    items.Add(srcObj);
                    var srcItem = (IShellItem)srcObj;
                    if (move)
                    {
                        op.MoveItem(srcItem, destItem, null, IntPtr.Zero);
                    }
                    else
                    {
                        op.CopyItem(srcItem, destItem, null, IntPtr.Zero);
                    }
                }

                op.PerformOperations();
                op.GetAnyOperationsAborted(out aborted);
                return true;
            }
            finally
            {
                foreach (var item in items)
                {
                    if (item is not null && Marshal.IsComObject(item))
                    {
                        Marshal.ReleaseComObject(item);
                    }
                }
            }
        }
        finally
        {
            if (destObj is not null && Marshal.IsComObject(destObj))
            {
                Marshal.ReleaseComObject(destObj);
            }
            if (op is not null && Marshal.IsComObject(op))
            {
                Marshal.FinalReleaseComObject(op);
            }
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.IUnknown)] out object ppv);

    [ComImport]
    [Guid("3ad05575-8857-4850-9277-11b85bdb8e09")]
    private class FileOperation
    {
    }

    [ComImport]
    [Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        // The vtable order must match the native interface exactly. Methods we
        // don't call are still declared so later slots line up; they use
        // PreserveSig int so an unexpected call can't throw on marshalling.
        [PreserveSig] int Advise(object pfops, out uint pdwCookie);
        [PreserveSig] int Unadvise(uint dwCookie);
        void SetOperationFlags(uint dwOperationFlags);
        [PreserveSig] int SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string pszMessage);
        [PreserveSig] int SetProgressDialog(object popd);
        [PreserveSig] int SetProperties(object pproparray);
        void SetOwnerWindow(IntPtr hwndOwner);
        [PreserveSig] int ApplyPropertiesToItem(IShellItem psiItem);
        [PreserveSig] int ApplyPropertiesToItems(object punkItems);
        [PreserveSig] int RenameItem(IShellItem psiItem, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, object pfopsItem);
        [PreserveSig] int RenameItems(object pUnkItems, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);
        // pfopsItem is an IFileOperationProgressSink*. It MUST be marshaled as a
        // raw pointer (IntPtr) — declaring it as `object` makes COM interop marshal
        // it as a VARIANT, corrupting the call's ABI and crashing with an access
        // violation. We never use a sink, so callers pass IntPtr.Zero.
        void MoveItem(IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName, IntPtr pfopsItem);
        [PreserveSig] int MoveItems(object punkItems, IShellItem psiDestinationFolder);
        void CopyItem(IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszCopyName, IntPtr pfopsItem);
        [PreserveSig] int CopyItems(object punkItems, IShellItem psiDestinationFolder);
        [PreserveSig] int DeleteItem(IShellItem psiItem, object pfopsItem);
        [PreserveSig] int DeleteItems(object punkItems);
        [PreserveSig] int NewItem(IShellItem psiDestinationFolder, uint dwFileAttributes, [MarshalAs(UnmanagedType.LPWStr)] string pszName, [MarshalAs(UnmanagedType.LPWStr)] string pszTemplateName, object pfopsItem);
        void PerformOperations();
        void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool pfAnyOperationsAborted);
    }

    // Empty pass-through wrapper: we never call IShellItem members, we only hand
    // the pointer from SHCreateItemFromParsingName back into IFileOperation.
    [ComImport]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
    }
}
