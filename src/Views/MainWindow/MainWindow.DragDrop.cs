using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.VisualBasic.FileIO;
using VbFileSystem = Microsoft.VisualBasic.FileIO.FileSystem;
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
        if (sender is not DependencyObject view || !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        var destination = ResolveDropDestination(view, e);
        if (ArchivePath.Contains(destination))
        {
            e.Handled = true;
            return;
        }
        var paths = (string[])e.Data.GetData(DataFormats.FileDrop);

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
        var leftBehind = new List<string>();
        var failed = new List<string>();

        foreach (var source in paths)
        {
            try
            {
                if (effect == DragDropEffects.Link)
                {
                    var lnkName = Loc.F("{0} - Shortcut.lnk", Path.GetFileName(source));
                    var lnkPath = FsHelpers.NextAvailablePath(Path.Combine(destination, lnkName));
                    FsHelpers.CreateShortcut(source, lnkPath);
                }
                else
                {
                    var requestedTarget = Path.Combine(destination, Path.GetFileName(source));
                    if (effect == DragDropEffects.Move && FsHelpers.SamePath(source, requestedTarget))
                    {
                        continue;
                    }

                    var target = FsHelpers.NextAvailablePath(requestedTarget);
                    if (Directory.Exists(source))
                    {
                        if (effect == DragDropEffects.Move)
                        {
                            VbFileSystem.MoveDirectory(source, target);
                        }
                        else
                        {
                            VbFileSystem.CopyDirectory(source, target);
                        }
                    }
                    else if (File.Exists(source))
                    {
                        if (effect == DragDropEffects.Move)
                        {
                            File.Move(source, target);
                        }
                        else
                        {
                            File.Copy(source, target);
                        }
                    }

                    if (effect == DragDropEffects.Move && (File.Exists(source) || Directory.Exists(source)))
                    {
                        leftBehind.Add(Path.GetFileName(source));
                    }
                }
            }
            catch (Exception ex)
            {
                failed.Add($"{Path.GetFileName(source)} ({ex.Message})");
            }
        }

        Reload(LeftGrid);
        Reload(RightGrid);

        if (leftBehind.Count > 0)
        {
            SetStatus(Loc.F("Source remained for: {0}", string.Join(", ", leftBehind)));
        }
        else if (failed.Count > 0)
        {
            SetStatus(Loc.F("Failed: {0}", string.Join(", ", failed)));
        }
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
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var destination = ResolveDropDestination(view, e);
        if (ArchivePath.Contains(destination))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = ResolveDropEffect(e, destination);
        e.Handled = true;
    }

    private string ResolveDropDestination(DependencyObject view, DragEventArgs e)
    {
        if (TryGetDropFolder(e.OriginalSource as DependencyObject, out var folderPath))
        {
            return folderPath;
        }

        return GetCurrentPath(SideOf(view));
    }

    private static bool TryGetDropFolder(DependencyObject? source, out string folderPath)
    {
        var row = FindVisualAncestor<DataGridRow>(source);
        if (row?.Item is FileItem rowItem && (rowItem.IsDirectory || rowItem.IsParent))
        {
            folderPath = rowItem.FullPath;
            return true;
        }

        var listBoxItem = FindVisualAncestor<ListBoxItem>(source);
        if (listBoxItem?.Content is FileItem iconItem && (iconItem.IsDirectory || iconItem.IsParent))
        {
            folderPath = iconItem.FullPath;
            return true;
        }

        folderPath = "";
        return false;
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

    private string[] ResolveDragPaths(string[] paths)
    {
        if (!paths.Any(ArchivePath.Contains))
        {
            return paths;
        }

        var result = new List<string>();
        var groups = paths
            .Where(ArchivePath.Contains)
            .Select(p =>
            {
                ArchivePath.TryParse(p, out var a, out var i);
                return (Archive: a, Inner: i);
            })
            .GroupBy(t => t.Archive, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            try
            {
                var extracted = ArchiveBrowser.ExtractEntriesToTemp(
                    group.Key,
                    group.Select(t => t.Inner),
                    EnsureArchiveTempRoot(),
                    CancellationToken.None);
                result.AddRange(extracted);
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
            }
        }

        result.AddRange(paths.Where(p => !ArchivePath.Contains(p)));
        return result.ToArray();
    }
}
