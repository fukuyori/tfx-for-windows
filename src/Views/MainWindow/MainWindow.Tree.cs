using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Path = System.IO.Path;

namespace Tfx;

public partial class MainWindow
{
    private async void LoadDrives()
    {
        FolderTree.Items.Clear();

        List<(string Path, bool HasChildren)> entries;
        try
        {
            var showHidden = ShowHidden;
            entries = await Task.Run(() =>
                DriveInfo.GetDrives()
                    .Where(d => d.IsReady)
                    .AsParallel()
                    .Select(d =>
                    {
                        var path = d.RootDirectory.FullName;
                        bool hasChildren;
                        try
                        {
                            hasChildren = FsHelpers.SafeEnumerateDirectories(path)
                                .Any(directory => showHidden || !FsHelpers.IsHidden(directory));
                        }
                        catch
                        {
                            hasChildren = false;
                        }
                        return (Path: path, HasChildren: hasChildren);
                    })
                    .OrderBy(t => t.Path, StringComparer.OrdinalIgnoreCase)
                    .ToList());
        }
        catch
        {
            return;
        }

        FolderTree.Items.Clear();
        foreach (var (path, hasChildren) in entries)
        {
            var item = new TreeViewItem
            {
                Header = FormatTreeHeader(path),
                Tag = path
            };
            if (hasChildren)
            {
                item.Items.Add(null);
            }
            FolderTree.Items.Add(item);
        }

        QueueFolderTreeSyncToActivePane();
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

    private void CollapseAllFolders_Click(object sender, RoutedEventArgs e) => CollapseAllFolders();

    /// <summary>Collapses every node in the folder tree back to the roots.</summary>
    private void CollapseAllFolders()
    {
        CollapseFolderItems();

        // A folder-tree sync (QueueFolderTreeSyncToActivePane, posted at
        // DispatcherPriority.Loaded) may still be pending and would re-expand the
        // active path right after this collapse — making it look like one click
        // didn't fully close the tree. Re-run the collapse at a lower priority so
        // it runs after any such pending reveal and wins.
        Dispatcher.BeginInvoke(CollapseFolderItems, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void CollapseFolderItems()
    {
        foreach (var obj in FolderTree.Items)
        {
            if (obj is TreeViewItem item)
            {
                CollapseTreeItem(item);
            }
        }
    }

    private static void CollapseTreeItem(TreeViewItem item)
    {
        foreach (var child in item.Items)
        {
            if (child is TreeViewItem childItem)
            {
                CollapseTreeItem(childItem);
            }
        }
        item.IsExpanded = false;
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
        // A single click now only selects (highlights) the node. Opening a folder
        // — navigating the active file list into it — happens on double-click (see
        // FolderTree_MouseDoubleClick). The _syncingFolderTree guard is kept so a
        // programmatic selection from SyncFolderTreeToPath stays a no-op here.
        if (_syncingFolderTree)
        {
            return;
        }
    }

    /// <summary>
    /// Double-click on a folder node: open it (expand + show its contents in the
    /// active file list) when it is closed, or close it (collapse) when it is
    /// open. A node with no subfolders simply opens its contents.
    /// </summary>
    /// <remarks>
    /// Handled on the tunneling <c>PreviewMouseLeftButtonDown</c> (not
    /// <c>MouseDoubleClick</c>): the inner <see cref="TreeViewItem"/> handles the
    /// bubbling mouse-down for selection / its own expand toggle first, which would
    /// otherwise cancel out our toggle. The preview pass runs before that, and
    /// <c>e.ClickCount == 2</c> identifies the double-click.
    /// </remarks>
    private void FolderTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
        {
            return; // single click: let the default selection happen
        }

        var node = e.OriginalSource as DependencyObject;
        while (node != null && node is not TreeView)
        {
            // The expander chevron toggles itself; don't double-handle it.
            if (node is System.Windows.Controls.Primitives.ToggleButton)
            {
                return;
            }

            if (node is TreeViewItem item && item.Tag is string path)
            {
                var hasChildren = item.Items.Count > 0;
                if (hasChildren && item.IsExpanded)
                {
                    // Open → close.
                    item.IsExpanded = false;
                }
                else
                {
                    // Closed (or a leaf) → open: reveal subfolders and show the
                    // folder's contents in the active file list.
                    if (hasChildren)
                    {
                        item.IsExpanded = true;
                    }
                    if (Directory.Exists(path))
                    {
                        Navigate(_activeGrid, path, true);
                    }
                }
                e.Handled = true;
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
        if (ArchivePath.TryParse(path, out var archive, out _))
        {
            path = Path.GetDirectoryName(archive) ?? archive;
        }
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
