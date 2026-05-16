using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.VisualBasic.FileIO;
using VbFileSystem = Microsoft.VisualBasic.FileIO.FileSystem;
using Path = System.IO.Path;

namespace Tfx;

public partial class MainWindow
{
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
        if (e.LeftButton != MouseButtonState.Pressed || sender is not DataGrid grid)
        {
            _dragStart = e.GetPosition(this);
            _pendingFileDragItem = null;
            _pendingFileDragPaths = [];
            return;
        }

        if (_isRubberBandSelecting)
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

        var paths = _pendingFileDragPaths;
        if (paths.Length == 0)
        {
            return;
        }

        var hasArchive = paths.Any(ArchivePath.Contains);
        var realPaths = ResolveDragPaths(paths);
        if (realPaths.Length == 0)
        {
            _pendingFileDragItem = null;
            _pendingFileDragPaths = [];
            return;
        }

        var data = new DataObject();
        var collection = new StringCollection();
        collection.AddRange(realPaths);
        data.SetFileDropList(collection);
        var allowedEffects = hasArchive
            ? DragDropEffects.Copy
            : DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link;
        var effect = DragDrop.DoDragDrop(grid, data, allowedEffects);

        if (effect != DragDropEffects.None)
        {
            Reload(LeftGrid);
            Reload(RightGrid);
        }

        _pendingFileDragItem = null;
        _pendingFileDragPaths = [];
    }

    private void Grid_Drop(object sender, DragEventArgs e)
    {
        if (sender is not DependencyObject view || !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        var grid = SideOf(view);
        var destination = ResolveDropDestination(view, e);
        if (ArchivePath.Contains(destination))
        {
            e.Handled = true;
            return;
        }
        var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
        var effect = ResolveDropEffect(e, destination);

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
                }
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
            }
        }

        e.Effects = effect;
        Reload(LeftGrid);
        Reload(RightGrid);
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
