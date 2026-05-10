using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualBasic.FileIO;
using VbFileSystem = Microsoft.VisualBasic.FileIO.FileSystem;
using Path = System.IO.Path;

namespace Tfx;

public partial class MainWindow
{
    private void OpenItem(DataGrid grid, FileItem item)
    {
        if (item.IsDirectory || item.IsParent)
        {
            var selectName = item.IsParent
                ? Path.GetFileName(GetCurrentPath(grid).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                : "..";
            Navigate(grid, item.FullPath, true, selectName);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(item.FullPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private void CopySelection(bool cut)
    {
        var paths = ActiveSelectedItems().Where(i => !i.IsParent).Select(i => i.FullPath).ToArray();
        if (paths.Length == 0)
        {
            return;
        }

        var collection = new StringCollection();
        collection.AddRange(paths);
        Clipboard.SetFileDropList(collection);
        _cutBuffer = cut ? paths : [];
        SetStatus(cut ? Loc.F("Cut {0} item(s)", paths.Length) : Loc.F("Copied {0} item(s)", paths.Length));
    }

    private void PasteIntoActivePane()
    {
        if (!Clipboard.ContainsFileDropList())
        {
            return;
        }

        var destination = GetCurrentPath(_activeGrid);
        var files = Clipboard.GetFileDropList().Cast<string>().ToArray();
        foreach (var source in files)
        {
            var requestedTarget = Path.Combine(destination, Path.GetFileName(source));
            var isMove = _cutBuffer.Contains(source, StringComparer.OrdinalIgnoreCase);
            if (isMove && FsHelpers.SamePath(source, requestedTarget))
            {
                continue;
            }

            var target = FsHelpers.NextAvailablePath(requestedTarget);
            if (Directory.Exists(source))
            {
                if (isMove)
                {
                    Directory.Move(source, target);
                }
                else
                {
                    VbFileSystem.CopyDirectory(source, target);
                }
            }
            else if (File.Exists(source))
            {
                if (isMove)
                {
                    File.Move(source, target);
                }
                else
                {
                    File.Copy(source, target);
                }
            }
        }

        _cutBuffer = [];
        Reload(LeftGrid);
        Reload(RightGrid);
        SetStatus(Loc.F("Pasted {0} item(s)", files.Length));
    }

    private void MoveSelectionToTrash()
    {
        var items = ActiveSelectedItems().Where(i => !i.IsParent).ToArray();
        if (items.Length == 0)
        {
            return;
        }

        if (!Confirm(Loc.F("Move {0} item(s) to Recycle Bin?", items.Length), Loc.T("Move to Recycle Bin")))
        {
            return;
        }

        foreach (var item in items)
        {
            try
            {
                if (item.IsDirectory)
                {
                    VbFileSystem.DeleteDirectory(item.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                }
                else
                {
                    VbFileSystem.DeleteFile(item.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                }
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
            }
        }

        Reload(LeftGrid);
        Reload(RightGrid);
    }

    private void NewFolder()
    {
        var name = PromptForName(Loc.T("New Folder"), Loc.T("Folder name"), Loc.T("New Folder"));
        if (!TryNormalizeNewItemName(name, out var itemName))
        {
            return;
        }

        try
        {
            var path = FsHelpers.NextAvailablePath(Path.Combine(GetCurrentPath(_activeGrid), itemName));
            Directory.CreateDirectory(path);
            Reload(_activeGrid);
            SetStatus(Loc.F("Created {0}", path));
        }
        catch (Exception ex)
        {
            SetStatus(Loc.F("New folder failed: {0}", ex.Message));
        }
    }

    private void NewFile()
    {
        var name = PromptForName(Loc.T("New File"), Loc.T("File name"), Loc.T("New File.txt"));
        if (!TryNormalizeNewItemName(name, out var itemName))
        {
            return;
        }

        try
        {
            var path = FsHelpers.NextAvailablePath(Path.Combine(GetCurrentPath(_activeGrid), itemName));
            File.WriteAllBytes(path, []);
            Reload(_activeGrid);
            SetStatus(Loc.F("Created {0}", path));
        }
        catch (Exception ex)
        {
            SetStatus(Loc.F("New file failed: {0}", ex.Message));
        }
    }

    private string? PromptForName(string title, string label, string defaultValue)
    {
        var dialog = new NamePromptDialog(title, label, defaultValue);
        return dialog.ShowDialog() == true ? dialog.EnteredText : null;
    }

    private static bool Confirm(string message, string confirmText)
    {
        var dialog = new ConfirmDialog("tfx", message, confirmText);
        return dialog.ShowDialog() == true;
    }

    private bool TryNormalizeNewItemName(string? rawName, out string name)
    {
        name = (rawName ?? "").Trim();
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            name.Contains(Path.DirectorySeparatorChar) ||
            name.Contains(Path.AltDirectorySeparatorChar))
        {
            SetStatus(Loc.F("Invalid name: {0}", name));
            return false;
        }

        return true;
    }

    private void StartRename(DataGrid grid, FileItem item)
    {
        var nameColumn = grid == LeftGrid ? LeftNameColumn : RightNameColumn;
        grid.IsReadOnly = false;
        grid.CurrentCell = new DataGridCellInfo(item, nameColumn);
        grid.BeginEdit();
    }

    private void Grid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (sender is not DataGrid grid)
        {
            return;
        }

        Dispatcher.BeginInvoke(() => grid.IsReadOnly = true, DispatcherPriority.Background);

        if (e.EditAction != DataGridEditAction.Commit)
        {
            return;
        }

        if (e.Row.Item is not FileItem item || item.IsParent)
        {
            return;
        }

        var nameColumn = grid == LeftGrid ? LeftNameColumn : RightNameColumn;
        if (e.Column != nameColumn)
        {
            return;
        }

        var tb = e.EditingElement as TextBox ?? FindVisualChild<TextBox>(e.EditingElement);
        if (tb is null)
        {
            return;
        }

        var newName = (tb.Text ?? "").Trim();
        if (string.IsNullOrEmpty(newName) || newName == item.Name)
        {
            return;
        }

        var directory = Path.GetDirectoryName(item.FullPath) ?? GetCurrentPath(grid);
        var target = FsHelpers.NextAvailablePath(Path.Combine(directory, newName));

        try
        {
            if (item.IsDirectory)
            {
                Directory.Move(item.FullPath, target);
            }
            else
            {
                File.Move(item.FullPath, target);
            }
        }
        catch (Exception ex)
        {
            SetStatus(Loc.F("Rename failed: {0}", ex.Message));
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            Reload(LeftGrid);
            Reload(RightGrid);
        }, DispatcherPriority.Background);
    }

    private void RenameTextBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox tb)
        {
            return;
        }

        tb.Focus();
        var text = tb.Text ?? "";

        if (tb.DataContext is FileItem item && !item.IsDirectory)
        {
            var dot = text.LastIndexOf('.');
            if (dot > 0)
            {
                tb.Select(0, dot);
                return;
            }
        }

        tb.SelectAll();
    }

    private void DeletePermanently()
    {
        var items = ActiveSelectedItems().Where(i => !i.IsParent).ToArray();
        if (items.Length == 0)
        {
            return;
        }

        var msg = items.Length == 1
            ? Loc.F("Permanently delete \"{0}\"? This cannot be undone.", items[0].Name)
            : Loc.F("Permanently delete {0} item(s)? This cannot be undone.", items.Length);

        if (!Confirm(msg, Loc.T("Delete permanently")))
        {
            return;
        }

        foreach (var item in items)
        {
            try
            {
                if (item.IsDirectory)
                {
                    Directory.Delete(item.FullPath, recursive: true);
                }
                else
                {
                    File.Delete(item.FullPath);
                }
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
            }
        }

        Reload(LeftGrid);
        Reload(RightGrid);
    }

    private void CompressSelection()
    {
        var items = ActiveSelectedItems().Where(i => !i.IsParent).ToArray();
        if (items.Length == 0)
        {
            return;
        }

        var directory = GetCurrentPath(_activeGrid);
        var baseName = items.Length == 1
            ? Path.GetFileNameWithoutExtension(items[0].Name)
            : Loc.T("Archive");
        var zipPath = FsHelpers.NextAvailablePath(Path.Combine(directory, $"{baseName}.zip"));

        try
        {
            using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            foreach (var item in items)
            {
                if (item.IsDirectory)
                {
                    AddDirectoryToArchive(archive, item.FullPath, item.Name);
                }
                else
                {
                    archive.CreateEntryFromFile(item.FullPath, item.Name, CompressionLevel.Optimal);
                }
            }

            Reload(_activeGrid);
            SetStatus(Loc.F("Created {0}", zipPath));
        }
        catch (Exception ex)
        {
            SetStatus(Loc.F("Compress failed: {0}", ex.Message));
            try
            {
                if (File.Exists(zipPath))
                {
                    File.Delete(zipPath);
                }
            }
            catch
            {
            }
        }
    }

    private void ExtractSelectedArchives()
    {
        var archives = ActiveSelectedItems()
            .Where(i => !i.IsParent && !i.IsDirectory && Path.GetExtension(i.FullPath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (archives.Length == 0)
        {
            SetStatus(Loc.T("Select one or more .zip files to extract"));
            return;
        }

        foreach (var archiveItem in archives)
        {
            var destination = FsHelpers.NextAvailablePath(Path.Combine(
                GetCurrentPath(_activeGrid),
                Path.GetFileNameWithoutExtension(archiveItem.Name)));

            try
            {
                Directory.CreateDirectory(destination);
                ZipFile.ExtractToDirectory(archiveItem.FullPath, destination);
            }
            catch (Exception ex)
            {
                SetStatus(Loc.F("Extract failed: {0}", ex.Message));
                return;
            }
        }

        Reload(_activeGrid);
        SetStatus(Loc.F("Extracted {0} archive(s)", archives.Length));
    }

    private static void AddDirectoryToArchive(ZipArchive archive, string sourceDirectory, string entryRoot)
    {
        var files = Directory.EnumerateFiles(sourceDirectory, "*", System.IO.SearchOption.AllDirectories);
        var wroteAnyFile = false;

        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(sourceDirectory, file);
            var entryName = Path.Combine(entryRoot, relative).Replace(Path.DirectorySeparatorChar, '/');
            archive.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
            wroteAnyFile = true;
        }

        if (!wroteAnyFile)
        {
            archive.CreateEntry(entryRoot.TrimEnd('/', '\\') + "/");
        }
    }

    private static T? FindVisualChild<T>(DependencyObject? parent) where T : DependencyObject
    {
        if (parent is null)
        {
            return null;
        }
        if (parent is T match)
        {
            return match;
        }

        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var found = FindVisualChild<T>(VisualTreeHelper.GetChild(parent, i));
            if (found is not null)
            {
                return found;
            }
        }
        return null;
    }

    private static T? FindVisualAncestor<T>(DependencyObject? child) where T : DependencyObject
    {
        var node = child;
        while (node is not null)
        {
            if (node is T match)
            {
                return match;
            }

            node = VisualTreeHelper.GetParent(node);
        }

        return null;
    }

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

        var data = new DataObject();
        var collection = new StringCollection();
        collection.AddRange(paths);
        data.SetFileDropList(collection);
        var effect = DragDrop.DoDragDrop(grid, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);

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

        e.Effects = ResolveDropEffect(e, ResolveDropDestination(view, e));
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

    private void NewFolder_Click(object sender, RoutedEventArgs e) => NewFolder();

    private void NewFile_Click(object sender, RoutedEventArgs e) => NewFile();

    private void Copy_Click(object sender, RoutedEventArgs e) => CopySelection(false);

    private void Cut_Click(object sender, RoutedEventArgs e) => CopySelection(true);

    private void Paste_Click(object sender, RoutedEventArgs e) => PasteIntoActivePane();

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (_activeGrid.SelectedItem is FileItem item && !item.IsParent)
        {
            StartRename(_activeGrid, item);
        }
    }

    private void Trash_Click(object sender, RoutedEventArgs e) => MoveSelectionToTrash();

    private void Compress_Click(object sender, RoutedEventArgs e) => CompressSelection();

    private void Extract_Click(object sender, RoutedEventArgs e) => ExtractSelectedArchives();
}
