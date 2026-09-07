using System.IO;
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
    private const uint FOF_NOCONFIRMATION = 0x0010;
    private const uint FOF_NOCONFIRMMKDIR = 0x0200;
    private const uint FOFX_RECYCLEONDELETE = 0x00080000;
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

    /// <summary>
    /// Moves items out of the Recycle Bin into <paramref name="destinationFolder"/>,
    /// restoring their original names. <paramref name="sources"/> are the
    /// physical paths the shell puts in CF_HDROP for Recycle Bin items
    /// (<c>X:\$RECYCLE.BIN\&lt;SID&gt;\$Rxxxxxx.ext</c>); handing those to
    /// <see cref="CopyOrMove"/> would produce a file named <c>$Rxxxxxx.ext</c>
    /// and orphan the <c>$I</c> metadata. Instead the Recycle Bin shell folder is
    /// enumerated, each source is matched to its shell item by file-system path,
    /// and that item is moved through <c>IFileOperation</c> — the same thing
    /// Explorer does, so the original name comes back. Sources that are not in
    /// the Recycle Bin are moved as plain paths. Throws when a Recycle Bin
    /// source cannot be found (e.g. the bin was emptied meanwhile).
    /// </summary>
    public static bool MoveFromRecycleBin(
        IntPtr ownerHwnd,
        IReadOnlyList<string> sources,
        string destinationFolder,
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
        var items = new List<object>();
        try
        {
            op = (IFileOperation)new FileOperation();
            op.SetOperationFlags(FOF_NOCONFIRMMKDIR | FOFX_ADDUNDORECORD | FOFX_SHOWELEVATIONPROMPT);
            if (ownerHwnd != IntPtr.Zero)
            {
                op.SetOwnerWindow(ownerHwnd);
            }

            SHCreateItemFromParsingName(destinationFolder, IntPtr.Zero, ref iidShellItem, out destObj);
            var destItem = (IShellItem)destObj;

            var binItems = EnumerateRecycleBinItems(items);
            var missing = new List<string>();
            foreach (var source in sources)
            {
                IShellItem srcItem;
                if (FsHelpers.IsRecycleBinPath(source))
                {
                    if (!binItems.TryGetValue(source, out var found))
                    {
                        missing.Add(Path.GetFileName(source));
                        continue;
                    }
                    srcItem = found;
                }
                else
                {
                    SHCreateItemFromParsingName(source, IntPtr.Zero, ref iidShellItem, out var srcObj);
                    items.Add(srcObj);
                    srcItem = (IShellItem)srcObj;
                }
                op.MoveItem(srcItem, destItem, null, IntPtr.Zero);
            }

            if (missing.Count > 0)
            {
                throw new FileNotFoundException(
                    Loc.F("Not found in the Recycle Bin: {0}", string.Join(", ", missing)));
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

    /// <summary>
    /// Enumerates the Recycle Bin virtual folder and maps each item's physical
    /// path (<c>SIGDN_FILESYSPATH</c>) to its shell item. Every COM object
    /// created is appended to <paramref name="owned"/> so the caller releases
    /// them after the operation.
    /// </summary>
    private static Dictionary<string, IShellItem> EnumerateRecycleBinItems(List<object> owned)
    {
        var map = new Dictionary<string, IShellItem>(StringComparer.OrdinalIgnoreCase);
        var iidShellItem = typeof(IShellItem).GUID;
        SHCreateItemFromParsingName(RecycleBinParsingName, IntPtr.Zero, ref iidShellItem, out var binObj);
        owned.Add(binObj);

        var bhidEnumItems = BHID_EnumItems;
        var iidEnum = typeof(IEnumShellItems).GUID;
        ((IShellItem)binObj).BindToHandler(IntPtr.Zero, ref bhidEnumItems, ref iidEnum, out var enumObj);
        owned.Add(enumObj);
        var enumerator = (IEnumShellItems)enumObj;

        while (enumerator.Next(1, out var item, out var fetched) == 0 && fetched == 1)
        {
            owned.Add(item);
            string? fsPath = null;
            try
            {
                item.GetDisplayName(SIGDN_FILESYSPATH, out fsPath);
            }
            catch (COMException)
            {
                // Not a file-system item; skip.
            }
            if (!string.IsNullOrEmpty(fsPath))
            {
                map[fsPath] = item;
            }
        }
        return map;
    }

    /// <summary>
    /// Deletes every path, to the Recycle Bin when <paramref name="toRecycleBin"/>
    /// is true, otherwise permanently. The shell shows progress + cancel for long
    /// deletes and handles read-only/locked items with its native prompts. For a
    /// recycle delete on a volume with no Recycle Bin the shell asks the user
    /// before falling back to a permanent delete (no confirmation is suppressed);
    /// a permanent delete suppresses the shell confirmation because the app has
    /// already asked. Returns false if the operation could not be started;
    /// <paramref name="aborted"/> is true if the user cancelled or any item was
    /// skipped/failed.
    /// </summary>
    public static bool Delete(
        IntPtr ownerHwnd,
        IReadOnlyList<string> paths,
        bool toRecycleBin,
        out bool aborted)
    {
        aborted = false;
        if (paths.Count == 0)
        {
            return true;
        }

        IFileOperation? op = null;
        var iidShellItem = typeof(IShellItem).GUID;
        try
        {
            op = (IFileOperation)new FileOperation();
            var flags = toRecycleBin
                ? FOFX_RECYCLEONDELETE | FOFX_ADDUNDORECORD | FOFX_SHOWELEVATIONPROMPT
                : FOF_NOCONFIRMATION | FOFX_SHOWELEVATIONPROMPT;
            op.SetOperationFlags(flags);
            if (ownerHwnd != IntPtr.Zero)
            {
                op.SetOwnerWindow(ownerHwnd);
            }

            var items = new List<object>();
            try
            {
                foreach (var path in paths)
                {
                    SHCreateItemFromParsingName(path, IntPtr.Zero, ref iidShellItem, out var srcObj);
                    items.Add(srcObj);
                    op.DeleteItem((IShellItem)srcObj, IntPtr.Zero);
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
        // Same marshaling note as MoveItem/CopyItem: pfopsItem is an
        // IFileOperationProgressSink* and must be a raw pointer, never `object`.
        void DeleteItem(IShellItem psiItem, IntPtr pfopsItem);
        [PreserveSig] int DeleteItems(object punkItems);
        [PreserveSig] int NewItem(IShellItem psiDestinationFolder, uint dwFileAttributes, [MarshalAs(UnmanagedType.LPWStr)] string pszName, [MarshalAs(UnmanagedType.LPWStr)] string pszTemplateName, object pfopsItem);
        void PerformOperations();
        void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool pfAnyOperationsAborted);
    }

    // Parsing name of the Recycle Bin virtual folder (CLSID_RecycleBin).
    private const string RecycleBinParsingName = "::{645FF040-5081-101B-9F08-00AA002F954E}";
    private static readonly Guid BHID_EnumItems = new("94f60519-2850-4924-aa5a-d15e84868039");
    private const uint SIGDN_FILESYSPATH = 0x80058000;

    // Vtable order matches the native IShellItem. Only GetDisplayName and
    // BindToHandler are called (Recycle Bin enumeration); the rest are declared
    // so the slots line up.
    [ComImport]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
        [PreserveSig] int GetParent(out IShellItem ppsi);
        void GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
        [PreserveSig] int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        [PreserveSig] int Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport]
    [Guid("70629033-e363-4a28-a567-0db78006e6d7")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumShellItems
    {
        [PreserveSig] int Next(uint celt, out IShellItem rgelt, out uint pceltFetched);
        [PreserveSig] int Skip(uint celt);
        [PreserveSig] int Reset();
        [PreserveSig] int Clone(out IEnumShellItems ppenum);
    }
}
