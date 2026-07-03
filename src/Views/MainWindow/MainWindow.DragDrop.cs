using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Path = System.IO.Path;

namespace Tfx;

public partial class MainWindow
{
    /// <summary>
    /// Custom DataObject format set when a drag is initiated inside tfx with the
    /// right mouse button. When <see cref="Grid_Drop"/> sees this marker it pops
    /// the Copy / Move / Shortcut / Cancel menu instead of executing the
    /// modifier-key resolved effect directly.
    /// </summary>
    private const string TfxRightDragFormat = "Tfx.RightDrag";

    private void Grid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        _pendingFileDragItem = null;
        _pendingFileDragPaths = [];

        if (sender is not DataGrid grid)
        {
            return;
        }

        // If an inline rename is active (grid temporarily editable) and the click
        // is outside the editor, commit the name first — Explorer-like
        // confirm-on-click, including clicks on the empty area of the list.
        if (!grid.IsReadOnly && FindVisualAncestor<TextBox>(e.OriginalSource as DependencyObject) is null)
        {
            grid.CommitEdit(DataGridEditingUnit.Row, true);
        }

        if (FindVisualAncestor<DataGridColumnHeader>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        UpdateActivePane(grid);

        var row = FindVisualAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row?.Item is not FileItem item)
        {
            BeginRubberBandSelection(grid, null, e);
            return;
        }

        if (item.IsParent)
        {
            return;
        }

        _pendingFileDragItem = item;
        var selectedItems = SelectedItems(grid).Where(i => !i.IsParent).ToArray();
        var itemAlreadySelected = selectedItems.Contains(item);
        _pendingFileDragPaths = itemAlreadySelected
            ? selectedItems.Select(i => i.FullPath).ToArray()
            : [item.FullPath];

        if (itemAlreadySelected && selectedItems.Length > 1 && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
        }
    }

    private void Grid_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not DataGrid grid)
        {
            return;
        }

        var leftPressed = e.LeftButton == MouseButtonState.Pressed;
        var rightPressed = e.RightButton == MouseButtonState.Pressed;

        if (!leftPressed && !rightPressed)
        {
            _dragStart = e.GetPosition(this);
            _pendingFileDragItem = null;
            _pendingFileDragPaths = [];
            return;
        }

        if (leftPressed && _isRubberBandSelecting)
        {
            UpdateRubberBandSelection(e);
            return;
        }

        if (_pendingFileDragItem is null)
        {
            return;
        }

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        StartFileDrag(grid, isRightDrag: rightPressed && !leftPressed);
    }

    /// <summary>
    /// Builds the DataObject and invokes <see cref="DragDrop.DoDragDrop"/>. Used
    /// by both left- and right-button drag starts. Right-button drags attach a
    /// custom marker (<see cref="TfxRightDragFormat"/>) so that drops landing
    /// back inside tfx can show the Copy / Move / Shortcut / Cancel menu.
    /// </summary>
    private void StartFileDrag(DependencyObject source, bool isRightDrag)
    {
        var paths = _pendingFileDragPaths;
        if (paths.Length == 0)
        {
            return;
        }

        var hasArchive = paths.Any(ArchivePath.Contains);
        var realPaths = ResolveDragPaths(paths);
        if (realPaths.Length == 0)
        {
            ClearPendingFileDrag();
            return;
        }

        if (isRightDrag && TryStartNativeRightDrag(realPaths, hasArchive, out var nativeEffect))
        {
            CompleteFileDrag(nativeEffect, suppressNextContextMenu: true);
            return;
        }

        var data = BuildFileDropData(realPaths, isRightDrag);
        AttachDragImage(data, paths.Length);
        var allowedEffects = hasArchive
            ? DragDropEffects.Copy
            : DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link;

        // Show the terminal drop overlay (if the pane is open) so files can be
        // dropped onto the shell. WebView2 is a native child window that swallows
        // WPF drops, so a WPF overlay must sit on top of it during the drag.
        ShowTerminalDropOverlay(true);
        DragDropEffects effect;
        try
        {
            effect = DragDrop.DoDragDrop(source, data, allowedEffects);
        }
        finally
        {
            ShowTerminalDropOverlay(false);
        }

        CompleteFileDrag(effect, suppressNextContextMenu: isRightDrag);
    }

    private bool TryStartNativeRightDrag(string[] realPaths, bool hasArchive, out DragDropEffects effect)
    {
        effect = DragDropEffects.None;
        if (hasArchive)
        {
            return false;
        }

        _nativeRightDragInProgress = true;
        try
        {
            return ShellFileDrag.TryStartRightButtonDrag(realPaths, out effect);
        }
        finally
        {
            _nativeRightDragInProgress = false;
        }
    }

    /// <summary>
    /// Attaches an Explorer-style translucent drag preview (icon glyph + name, or
    /// an item count for a multi-selection) to the drag's <see cref="DataObject"/>.
    /// </summary>
    private void AttachDragImage(DataObject data, int count)
    {
        var primary = _pendingFileDragItem;
        if (primary is null)
        {
            return;
        }

        var label = count > 1 ? Loc.F("{0} items", count) : primary.Name;
        var scale = System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX;
        ShellDragImage.Attach(data, primary.IconGlyph, label, scale);
    }

    private static DataObject BuildFileDropData(string[] paths, bool isRightDrag)
    {
        var data = new DataObject();
        var collection = new StringCollection();
        collection.AddRange(paths);
        data.SetFileDropList(collection);

        if (isRightDrag)
        {
            data.SetData(TfxRightDragFormat, true);
        }

        return data;
    }

    private void CompleteFileDrag(DragDropEffects effect, bool suppressNextContextMenu)
    {
        SetDropHighlight(null);

        if (suppressNextContextMenu)
        {
            _suppressNextContextMenu = true;
        }

        if (effect != DragDropEffects.None)
        {
            Reload(LeftGrid);
            Reload(RightGrid);
        }

        ClearPendingFileDrag();
    }

    private void Grid_Drop(object sender, DragEventArgs e)
    {
        SetDropHighlight(null);

        if (sender is not DependencyObject view || !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
        var kind = ClassifyDropTarget(e.OriginalSource as DependencyObject, out var targetItem);

        // Drop onto an executable's name → run it with the dropped files as args.
        if (kind == DropTargetKind.RunExecutable && targetItem is not null)
        {
            e.Handled = true;
            e.Effects = DragDropEffects.Link;
            LaunchWithArguments(targetItem.FullPath, paths);
            return;
        }

        var destination = DropDestinationFor(kind, targetItem, view);
        if (ArchivePath.Contains(destination))
        {
            e.Handled = true;
            return;
        }

        // Right-button drag started from a tfx pane: pop the Copy / Move /
        // Shortcut / Cancel menu and act on the user's choice.
        if (_nativeRightDragInProgress || e.Data.GetDataPresent(TfxRightDragFormat))
        {
            e.Handled = true;
            var allowMoveLink = !paths.Any(ArchivePath.Contains);
            var chosen = ShowRightDragMenu(view as UIElement, allowMoveLink);
            if (chosen is null)
            {
                e.Effects = DragDropEffects.None;
                return;
            }
            ExecuteDrop(paths, destination, chosen.Value);
            e.Effects = chosen.Value;
            return;
        }

        var effect = ResolveDropEffect(e, destination);
        ExecuteDrop(paths, destination, effect);
        e.Effects = effect;
    }

    /// <summary>
    /// Shared drop-execution core. Performs the Copy / Move / Link operation
    /// for each source path, verifies the move actually removed the source
    /// (catches cross-volume copy + delete failures), surfaces left-behind /
    /// failed items via the status bar, and reloads both panes.
    /// </summary>
    private void ExecuteDrop(string[] paths, string destination, DragDropEffects effect)
    {
        if (effect == DragDropEffects.Link)
        {
            ExecuteCreateShortcuts(paths, destination);
            return;
        }

        var move = effect == DragDropEffects.Move;

        // Skip items that are already in the destination folder (dragging within
        // the same folder), which would otherwise be a no-op move or a self-copy.
        var sources = paths
            .Where(p => !(move && FsHelpers.SamePath(Path.GetDirectoryName(p) ?? "", destination)))
            .ToArray();
        if (sources.Length == 0)
        {
            return;
        }

        // A copy whose sources all live in the destination folder is an in-place
        // copy → auto-rename to "name - Copy" (Explorer behavior) instead of the
        // shell's "source and destination are the same" skip/cancel error.
        var sameFolderCopy = !move &&
            sources.All(p => FsHelpers.SamePath(Path.GetDirectoryName(p) ?? "", destination));

        CopyOrMoveWithProgress(sources, destination, move, sameFolderCopy);
    }

    private void ExecuteCreateShortcuts(string[] paths, string destination)
    {
        var failed = new List<string>();
        foreach (var source in paths)
        {
            try
            {
                var lnkName = Loc.F("{0} - Shortcut.lnk", Path.GetFileName(source));
                var lnkPath = FsHelpers.NextAvailablePath(Path.Combine(destination, lnkName));
                FsHelpers.CreateShortcut(source, lnkPath);
            }
            catch (Exception ex)
            {
                failed.Add($"{Path.GetFileName(source)} ({ex.Message})");
            }
        }

        Reload(LeftGrid);
        Reload(RightGrid);
        if (failed.Count > 0)
        {
            SetStatus(Loc.F("Failed: {0}", string.Join(", ", failed)));
        }
    }

    /// <summary>
    /// Copies or moves the sources into <paramref name="destination"/> through the
    /// Windows shell, which shows its standard progress dialog for long operations
    /// and handles name collisions natively. Both panes reload afterwards.
    /// </summary>
    /// <remarks>
    /// Runs on a dedicated STA thread. <c>IFileOperation.PerformOperations</c> pumps
    /// its own modal message loop / shows a dialog; doing that on the UI thread from
    /// inside the drop handler — which for an intra-app drag is nested inside the
    /// source's <c>DoDragDrop</c> modal loop (and that loop also pumps the WPF
    /// dispatcher, so a mere <c>BeginInvoke</c> can still run nested) — is an
    /// input-synchronous re-entrant COM call that crashes the process. A separate
    /// STA thread is fully decoupled from the drag loop.
    /// </remarks>
    private void CopyOrMoveWithProgress(IReadOnlyList<string> sources, string destination, bool move, bool renameOnCollision = false)
    {
        var sourcesCopy = sources.ToArray();
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;

        var thread = new System.Threading.Thread(() =>
        {
            var aborted = false;
            string? error = null;
            try
            {
                ShellFileOperation.CopyOrMove(hwnd, sourcesCopy, destination, move, renameOnCollision, out aborted);
            }
            catch (Exception ex)
            {
                // E.g. the destination folder vanished between the paste and the
                // shell resolving it — without a status the paste fails silently
                // and the user believes the copy happened.
                error = ex.Message;
            }

            Dispatcher.BeginInvoke(() =>
            {
                // Use the in-place diff refresh (not a full Reload) so the new
                // items appear without clearing the lists — this preserves the
                // current selection and keyboard focus and avoids the flicker of a
                // clear-and-repopulate.
                _ = ReloadDiffAsync(LeftGrid);
                _ = ReloadDiffAsync(RightGrid);
                if (error is not null)
                {
                    SetStatus(Loc.F("Operation failed: {0}", error));
                }
                else if (aborted)
                {
                    SetStatus(Loc.T("Operation cancelled or incomplete"));
                }
            });
        })
        {
            IsBackground = true,
            Name = "tfx-file-op"
        };
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
    }

    /// <summary>
    /// Pops a ContextMenu at the current cursor with the four standard
    /// right-drag options. Returns the chosen <see cref="DragDropEffects"/>,
    /// or null if the user cancels / dismisses the menu.
    /// </summary>
    private DragDropEffects? ShowRightDragMenu(UIElement? placement, bool allowMoveAndLink)
    {
        DragDropEffects? chosen = null;
        var menu = new ContextMenu { PlacementTarget = placement, Placement = PlacementMode.MousePoint };

        var copyItem = new MenuItem { Header = Loc.T("Copy here") };
        copyItem.Click += (_, _) => chosen = DragDropEffects.Copy;
        menu.Items.Add(copyItem);

        var moveItem = new MenuItem { Header = Loc.T("Move here"), IsEnabled = allowMoveAndLink };
        moveItem.Click += (_, _) => chosen = DragDropEffects.Move;
        menu.Items.Add(moveItem);

        var linkItem = new MenuItem { Header = Loc.T("Create shortcut here"), IsEnabled = allowMoveAndLink };
        linkItem.Click += (_, _) => chosen = DragDropEffects.Link;
        menu.Items.Add(linkItem);

        menu.Items.Add(new Separator());

        var cancelItem = new MenuItem { Header = Loc.T("Cancel") };
        cancelItem.Click += (_, _) => chosen = null;
        menu.Items.Add(cancelItem);

        // Open synchronously: pump the dispatcher until the menu closes so the
        // Drop handler can act on the user's choice before returning.
        menu.IsOpen = true;
        var frame = new DispatcherFrame();
        menu.Closed += (_, _) => frame.Continue = false;
        Dispatcher.PushFrame(frame);

        return chosen;
    }

    private void Grid_DragOver(object sender, DragEventArgs e)
    {
        if (sender is not DependencyObject view)
        {
            SetDropHighlight(null);
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var kind = ClassifyDropTarget(e.OriginalSource as DependencyObject, out var targetItem);

        if (kind == DropTargetKind.RunExecutable)
        {
            // "Open with": the dropped files become the program's arguments.
            SetDropHighlight(targetItem);
            e.Effects = DragDropEffects.Link;
            e.Handled = true;
            return;
        }

        var destination = DropDestinationFor(kind, targetItem, view);
        if (ArchivePath.Contains(destination))
        {
            SetDropHighlight(null);
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = ResolveDropEffect(e, destination);
        // Highlight the target folder's name only when the drop will go into it.
        SetDropHighlight(kind == DropTargetKind.IntoFolder && e.Effects != DragDropEffects.None ? targetItem : null);
        e.Handled = true;
    }

    private void Grid_DragLeave(object sender, DragEventArgs e)
    {
        // DragLeave also fires when moving between child elements; only clear the
        // highlight when the cursor has actually left the listing bounds.
        if (sender is FrameworkElement fe)
        {
            var p = e.GetPosition(fe);
            if (p.X < 0 || p.Y < 0 || p.X > fe.ActualWidth || p.Y > fe.ActualHeight)
            {
                SetDropHighlight(null);
            }
        }
    }

    private enum DropTargetKind { CurrentFolder, IntoFolder, RunExecutable }

    /// <summary>
    /// Classifies a drop over <paramref name="source"/>: into the hovered folder
    /// (over its name), run the hovered executable with the dropped files as
    /// arguments (over its name), or — for anything else (other columns, regular
    /// files, empty space) — the current folder.
    /// </summary>
    private DropTargetKind ClassifyDropTarget(DependencyObject? source, out FileItem? targetItem)
    {
        targetItem = null;
        if (!TryGetNameTargetItem(source, out var item) || item is null)
        {
            return DropTargetKind.CurrentFolder;
        }
        if (item.IsDirectory || item.IsParent)
        {
            targetItem = item;
            return DropTargetKind.IntoFolder;
        }
        if (IsExecutableTarget(item))
        {
            targetItem = item;
            return DropTargetKind.RunExecutable;
        }
        return DropTargetKind.CurrentFolder;
    }

    private string DropDestinationFor(DropTargetKind kind, FileItem? targetItem, DependencyObject view) =>
        kind == DropTargetKind.IntoFolder && targetItem is not null
            ? targetItem.FullPath
            : GetCurrentPath(SideOf(view));

    /// <summary>
    /// The FileItem whose name area is under the cursor: in Details view the
    /// tagged Border around the icon + name, in Icons view the whole tile. Returns
    /// any item (folder or file) — the caller decides what to do with it.
    /// </summary>
    private static bool TryGetNameTargetItem(DependencyObject? source, out FileItem? item)
    {
        item = null;

        for (var node = source; node is not null; node = GetParentSafe(node))
        {
            if (node is DataGrid || node is ListBox)
            {
                break;
            }
            if (node is Border { Tag: "dropname" } border && border.DataContext is FileItem fi)
            {
                item = fi;
                return true;
            }
        }

        var listBoxItem = FindVisualAncestor<ListBoxItem>(source);
        if (listBoxItem?.Content is FileItem iconItem)
        {
            item = iconItem;
            return true;
        }

        return false;
    }

    private static readonly HashSet<string> ExecutableDropExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".exe", ".com", ".bat", ".cmd", ".lnk" };

    private static bool IsExecutableTarget(FileItem item) =>
        !item.IsDirectory
        && !item.IsParent
        && !ArchivePath.Contains(item.FullPath)
        && ExecutableDropExtensions.Contains(Path.GetExtension(item.FullPath));

    /// <summary>Launches a program with the dropped files as its arguments ("Open with").</summary>
    private void LaunchWithArguments(string exePath, string[] arguments)
    {
        var args = arguments.ToArray();
        // Deferred so it runs outside the drop / drag-drop modal loop.
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(exePath) ?? ""
                };
                foreach (var arg in args)
                {
                    psi.ArgumentList.Add(arg);
                }
                System.Diagnostics.Process.Start(psi);
                SetStatus(Loc.F("Opened {0} with {1} item(s)", Path.GetFileName(exePath), args.Length));
            }
            catch (Exception ex)
            {
                SetStatus(Loc.F("Failed to open: {0}", ex.Message));
            }
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private static DependencyObject? GetParentSafe(DependencyObject node) =>
        node is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(node)
            : LogicalTreeHelper.GetParent(node);

    private FileItem? _dropHighlightItem;

    /// <summary>Highlights (only) the given folder's name as the current drop target.</summary>
    private void SetDropHighlight(FileItem? item)
    {
        if (ReferenceEquals(_dropHighlightItem, item))
        {
            return;
        }
        if (_dropHighlightItem is not null)
        {
            _dropHighlightItem.IsDropTarget = false;
        }
        _dropHighlightItem = item;
        if (_dropHighlightItem is not null)
        {
            _dropHighlightItem.IsDropTarget = true;
        }
    }

    private static DragDropEffects ResolveDropEffect(DragEventArgs e, string destinationPath)
    {
        if (e.KeyStates.HasFlag(DragDropKeyStates.AltKey))
        {
            return DragDropEffects.Link;
        }
        if (e.KeyStates.HasFlag(DragDropKeyStates.ShiftKey))
        {
            return DragDropEffects.Move;
        }
        if (e.KeyStates.HasFlag(DragDropKeyStates.ControlKey))
        {
            return DragDropEffects.Copy;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
        {
            var sourceRoot = Path.GetPathRoot(paths[0]);
            var destRoot = Path.GetPathRoot(destinationPath);
            if (!string.IsNullOrEmpty(sourceRoot) && !string.IsNullOrEmpty(destRoot) &&
                string.Equals(sourceRoot, destRoot, StringComparison.OrdinalIgnoreCase))
            {
                return DragDropEffects.Move;
            }
        }

        return DragDropEffects.Copy;
    }

    /// <remarks>
    /// Stays synchronous: it runs inside the mouse-move handler that must call
    /// DoDragDrop immediately, so a large archive entry blocks until extracted.
    /// The Enter-to-open and Ctrl+C paths use the async wrappers instead.
    /// </remarks>
    private string[] ResolveDragPaths(string[] paths)
    {
        if (!paths.Any(ArchivePath.Contains))
        {
            return paths;
        }

        var (extracted, errors) = ExtractArchiveEntries(paths, EnsureArchiveTempRoot());
        foreach (var error in errors)
        {
            SetStatus(error);
        }
        return extracted.ToArray();
    }
}
