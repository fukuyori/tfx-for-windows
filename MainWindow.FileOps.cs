using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
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
            Navigate(grid, item.FullPath, true);
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
        var paths = SelectedItems(_activeGrid).Where(i => !i.IsParent).Select(i => i.FullPath).ToArray();
        if (paths.Length == 0)
        {
            return;
        }

        var collection = new StringCollection();
        collection.AddRange(paths);
        Clipboard.SetFileDropList(collection);
        _cutBuffer = cut ? paths : [];
        SetStatus(cut ? $"Cut {paths.Length} item(s)" : $"Copied {paths.Length} item(s)");
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
            var target = FsHelpers.NextAvailablePath(Path.Combine(destination, Path.GetFileName(source)));
            if (Directory.Exists(source))
            {
                if (_cutBuffer.Contains(source, StringComparer.OrdinalIgnoreCase))
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
                if (_cutBuffer.Contains(source, StringComparer.OrdinalIgnoreCase))
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
        SetStatus($"Pasted {files.Length} item(s)");
    }

    private void MoveSelectionToTrash()
    {
        var items = SelectedItems(_activeGrid).Where(i => !i.IsParent).ToArray();
        if (items.Length == 0)
        {
            return;
        }

        if (MessageBox.Show($"Move {items.Length} item(s) to Recycle Bin?", "tfx", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
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
        var name = Microsoft.VisualBasic.Interaction.InputBox("Folder name", "tfx", "New Folder");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var path = FsHelpers.NextAvailablePath(Path.Combine(GetCurrentPath(_activeGrid), name.Trim()));
        Directory.CreateDirectory(path);
        Reload(_activeGrid);
        SetStatus($"Created {path}");
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
            SetStatus($"Rename failed: {ex.Message}");
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
        var items = SelectedItems(_activeGrid).Where(i => !i.IsParent).ToArray();
        if (items.Length == 0)
        {
            return;
        }

        var msg = items.Length == 1
            ? $"Permanently delete \"{items[0].Name}\"? This cannot be undone."
            : $"Permanently delete {items.Length} item(s)? This cannot be undone.";

        if (MessageBox.Show(msg, "tfx", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
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

    private void Grid_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || sender is not DataGrid grid)
        {
            _dragStart = e.GetPosition(this);
            return;
        }

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var paths = SelectedItems(grid).Where(i => !i.IsParent).Select(i => i.FullPath).ToArray();
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
    }

    private void Grid_Drop(object sender, DragEventArgs e)
    {
        if (sender is not DependencyObject view || !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        var grid = SideOf(view);
        var destination = GetCurrentPath(grid);
        var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
        var effect = ResolveDropEffect(e, destination);

        foreach (var source in paths)
        {
            try
            {
                if (effect == DragDropEffects.Link)
                {
                    var lnkName = $"{Path.GetFileName(source)} - Shortcut.lnk";
                    var lnkPath = FsHelpers.NextAvailablePath(Path.Combine(destination, lnkName));
                    FsHelpers.CreateShortcut(source, lnkPath);
                }
                else
                {
                    var target = FsHelpers.NextAvailablePath(Path.Combine(destination, Path.GetFileName(source)));
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

        e.Effects = ResolveDropEffect(e, GetCurrentPath(SideOf(view)));
        e.Handled = true;
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

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (_activeGrid.SelectedItem is FileItem item && !item.IsParent)
        {
            StartRename(_activeGrid, item);
        }
    }

    private void Trash_Click(object sender, RoutedEventArgs e) => MoveSelectionToTrash();
}
