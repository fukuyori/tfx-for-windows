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
        if (item.IsParent)
        {
            var current = GetCurrentPath(grid);
            string selectName;
            if (ArchivePath.TryParse(current, out var archive, out var inner))
            {
                selectName = string.IsNullOrEmpty(inner)
                    ? Path.GetFileName(archive)
                    : (inner.TrimEnd('/').Split('/').LastOrDefault() ?? "");
            }
            else
            {
                selectName = Path.GetFileName(current.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }
            Navigate(grid, item.FullPath, true, selectName);
            return;
        }

        if (item.IsDirectory)
        {
            Navigate(grid, item.FullPath, true, "..");
            return;
        }

        if (ArchivePath.TryParse(item.FullPath, out var archiveFile, out var entryPath))
        {
            try
            {
                var realPath = ArchiveBrowser.ExtractEntryToTemp(archiveFile, entryPath, EnsureArchiveTempRoot(), CancellationToken.None);
                Process.Start(new ProcessStartInfo(realPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
            }
            return;
        }

        if (ArchivePath.IsZipFile(item.FullPath) && File.Exists(item.FullPath))
        {
            Navigate(grid, ArchivePath.Combine(item.FullPath, ""), true, "..");
            return;
        }

        if (TryOpenWithConfiguredApp(item.FullPath))
        {
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

    private bool TryOpenWithConfiguredApp(string path)
    {
        var extension = AppConfig.NormalizeExtension(Path.GetExtension(path));
        if (extension.Length == 0 || !_config.OpenWith.TryGetValue(extension, out var app) || string.IsNullOrWhiteSpace(app))
        {
            return false;
        }

        try
        {
            var expandedApp = Environment.ExpandEnvironmentVariables(AppConfig.ExpandUserPath(app));
            var safePath = "\"" + path.Replace("\"", "\"\"") + "\"";
            Process.Start(new ProcessStartInfo(expandedApp, safePath) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            SetStatus(Loc.F("Open with failed: {0}", ex.Message));
            return true;
        }
    }

    private string EnsureArchiveTempRoot()
    {
        if (!string.IsNullOrEmpty(_archiveTempRoot))
        {
            return _archiveTempRoot!;
        }
        // Before creating this session's folder, opportunistically sweep
        // leftovers from previous tfx runs that crashed before they could
        // delete their temp folders. Best-effort: anything currently held
        // open by another tfx process is silently skipped.
        try
        {
            var parent = Path.Combine(Path.GetTempPath(), "tfx");
            if (Directory.Exists(parent))
            {
                foreach (var stale in Directory.EnumerateDirectories(parent, "archive-*"))
                {
                    try { Directory.Delete(stale, recursive: true); } catch { }
                }
            }
        }
        catch
        {
        }
        var root = Path.Combine(Path.GetTempPath(), "tfx", "archive-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        _archiveTempRoot = root;
        return root;
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
        if (files.Length == 0)
        {
            return;
        }

        // Cut + paste = move; plain copy = copy. The clipboard set is uniformly one
        // or the other (set by Cut/Copy), so decide once for the whole batch.
        var move = files.All(f => _cutBuffer.Contains(f, StringComparer.OrdinalIgnoreCase));

        // Skip items already in the destination folder for a move (self-move).
        var sources = files
            .Where(f => !(move && FsHelpers.SamePath(Path.GetDirectoryName(f) ?? "", destination)))
            .ToArray();

        _cutBuffer = [];
        if (sources.Length == 0)
        {
            return;
        }

        // A copy whose sources all live in the destination folder is an in-place
        // copy → auto-rename to "name - Copy" (Explorer behavior) instead of the
        // shell's "source and destination are the same" skip/cancel error.
        var sameFolderCopy = !move &&
            sources.All(f => FsHelpers.SamePath(Path.GetDirectoryName(f) ?? "", destination));

        // Run through the same shell-operation path as drag-and-drop (a dedicated
        // STA thread). That reliably shows the Windows standard progress and
        // name-collision dialogs (replace / skip / keep both); doing it inline on
        // the UI thread did not surface the collision dialog. Reload + status are
        // handled by CopyOrMoveWithProgress.
        CopyOrMoveWithProgress(sources, destination, move, sameFolderCopy);
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

        var focusIndex = FirstSelectedListingIndex(items);

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

        RefreshActivePaneAfterMutation(focusIndex);
    }

    private void NewFolder()
    {
        try
        {
            var path = FsHelpers.NextAvailablePath(Path.Combine(GetCurrentPath(_activeGrid), Loc.T("New folder")));
            Directory.CreateDirectory(path);
            BeginInlineCreate(Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            SetStatus(Loc.F("New folder failed: {0}", ex.Message));
        }
    }

    private void NewFile()
    {
        try
        {
            var path = FsHelpers.NextAvailablePath(Path.Combine(GetCurrentPath(_activeGrid), Loc.T("New file") + ".txt"));
            File.WriteAllBytes(path, []);
            BeginInlineCreate(Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            SetStatus(Loc.F("New file failed: {0}", ex.Message));
        }
    }

    /// <summary>
    /// After creating a new item with a default name, reloads the active pane and
    /// drops the new row into inline rename (Explorer-style): the user types the
    /// final name and presses Enter (or Esc to keep the default name).
    /// </summary>
    private void BeginInlineCreate(string createdName)
    {
        // Pass the name through Reload's selectName parameter: Reload itself calls
        // SetPendingSelectionName, so setting it beforehand would be overwritten.
        // The rename flag is separate and survives the reload.
        SetPendingRename(ActivePane, true);
        Reload(_activeGrid, createdName);
    }

    private static bool Confirm(string message, string confirmText)
    {
        var dialog = new ConfirmDialog("tfx", message, confirmText);
        return dialog.ShowDialog() == true;
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

        // Restore selection on the renamed entry after the reload so the user
        // keeps their place (and so arrow keys keep navigating). The name must be
        // passed through Reload's selectName parameter — Reload itself calls
        // SetPendingSelectionName, so setting it beforehand would be overwritten.
        var renamedName = Path.GetFileName(target);
        var renamedPane = PaneOf(grid);

        Dispatcher.BeginInvoke(() =>
        {
            Reload(LeftGrid, renamedPane == Pane.Left ? renamedName : null);
            Reload(RightGrid, renamedPane == Pane.Right ? renamedName : null);
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

        var focusIndex = FirstSelectedListingIndex(items);

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

        RefreshActivePaneAfterMutation(focusIndex);
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

}
