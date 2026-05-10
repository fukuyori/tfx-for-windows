using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Path = System.IO.Path;

namespace Tfx;

public partial class MainWindow
{
    private void LoadDrives()
    {
        FolderTree.Items.Clear();
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
        {
            var item = CreateFolderNode(drive.RootDirectory.FullName);
            FolderTree.Items.Add(item);
        }
    }

    private TreeViewItem CreateFolderNode(string path)
    {
        var item = new TreeViewItem
        {
            Header = FormatTreeHeader(path),
            Tag = path
        };

        if (HasVisibleSubdirectories(path))
        {
            item.Items.Add(null);
        }

        return item;
    }

    private static string FormatTreeHeader(string path)
    {
        var root = Path.GetPathRoot(path);
        if (string.Equals(root, path, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        return Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }

    private bool HasVisibleSubdirectories(string path)
    {
        try
        {
            return VisibleDirectories(path).Any();
        }
        catch
        {
            return false;
        }
    }

    private void FolderTree_Expanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not TreeViewItem item || item.Tag is not string path)
        {
            return;
        }

        EnsureFolderNodeChildren(item);
    }

    private void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_syncingFolderTree)
        {
            return;
        }

        if (FolderTree.SelectedItem is TreeViewItem item && item.Tag is string path && Directory.Exists(path))
        {
            Navigate(_activeGrid, path, true);
        }
    }

    private void FolderTree_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var node = e.OriginalSource as DependencyObject;
        while (node != null && node is not TreeView)
        {
            if (node is System.Windows.Controls.Primitives.ToggleButton)
            {
                return;
            }

            if (node is TreeViewItem item && item.Tag is string path && Directory.Exists(path))
            {
                if (!string.Equals(GetCurrentPath(_activeGrid), path, StringComparison.OrdinalIgnoreCase))
                {
                    Navigate(_activeGrid, path, true);
                }
                return;
            }

            node = VisualTreeHelper.GetParent(node);
        }
    }

    private void SyncFolderTreeToActivePane()
    {
        SyncFolderTreeToPath(GetCurrentPath(_activeGrid));
    }

    private void QueueFolderTreeSyncToActivePane()
    {
        Dispatcher.BeginInvoke(() => SyncFolderTreeToActivePane(), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void SyncFolderTreeToPath(string path)
    {
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root))
        {
            return;
        }

        var current = FolderTree.Items
            .OfType<TreeViewItem>()
            .FirstOrDefault(item => item.Tag is string itemPath &&
                                    string.Equals(itemPath, root, StringComparison.OrdinalIgnoreCase));
        if (current is null)
        {
            return;
        }

        _syncingFolderTree = true;
        try
        {
            current.IsExpanded = true;
            var accumulated = root;
            var rest = path.Length > root.Length ? path[root.Length..] : "";
            var parts = rest.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);

            foreach (var part in parts)
            {
                EnsureFolderNodeChildren(current);
                accumulated = Path.Combine(accumulated, part);
                var next = current.Items
                    .OfType<TreeViewItem>()
                    .FirstOrDefault(item => item.Tag is string itemPath &&
                                            string.Equals(itemPath, accumulated, StringComparison.OrdinalIgnoreCase));
                if (next is null)
                {
                    break;
                }

                current = next;
                current.IsExpanded = true;
            }

            current.IsSelected = true;
            current.BringIntoView();
        }
        finally
        {
            _syncingFolderTree = false;
        }
    }

    private void EnsureFolderNodeChildren(TreeViewItem item)
    {
        if (item.Tag is not string path)
        {
            return;
        }

        if (item.Items.Count > 0 && (item.Items.Count != 1 || item.Items[0] is not null))
        {
            return;
        }

        item.Items.Clear();
        foreach (var directory in VisibleDirectories(path).OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase))
        {
            item.Items.Add(CreateFolderNode(directory));
        }
    }

    private IEnumerable<string> VisibleDirectories(string path) =>
        FsHelpers.SafeEnumerateDirectories(path)
            .Where(directory => ShowHidden || !FsHelpers.IsHidden(directory));
}
