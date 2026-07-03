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

        // The tree was rebuilt from scratch — force the next sync to re-run
        // even if the pane path is unchanged.
        _lastFolderTreeSyncPath = null;
        QueueFolderTreeSyncToActivePane();
    }

    private static TreeViewItem CreateFolderNode(string path)
    {
        var item = new TreeViewItem
        {
            Header = FormatTreeHeader(path),
            Tag = path
        };

        // Always add the expand placeholder instead of probing the folder for
        // visible subdirectories up front: that probe was one extra enumeration
        // PER NODE (N+1) on every tree level realized, and it ran on the UI
        // thread during expansion / path reveal. Nodes that turn out to be
        // empty lose their expander on the first expand attempt (Explorer
        // behaves the same way).
        item.Items.Add(null);

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
        if (e.OriginalSource is not TreeViewItem item || item.Tag is not string)
        {
            return;
        }

        PopulateFolderNodeAsync(item);
    }

    // Nodes currently loading their children on the thread pool, so a rapid
    // collapse/expand doesn't start a second enumeration for the same node.
    private readonly HashSet<TreeViewItem> _treeNodesLoading = [];

    /// <summary>
    /// Fills a node's children on user expansion, enumerating on the thread
    /// pool — a slow disk or network folder no longer stalls the UI while the
    /// tree opens. The synchronous <see cref="EnsureFolderNodeChildren"/> is
    /// still used by the path-reveal walk, which needs children immediately;
    /// whichever populates first wins.
    /// </summary>
    private async void PopulateFolderNodeAsync(TreeViewItem item)
    {
        if (item.Tag is not string path || !HasExpandPlaceholder(item) || !_treeNodesLoading.Add(item))
        {
            return;
        }

        try
        {
            var showHidden = ShowHidden;
            List<string> directories;
            try
            {
                directories = await Task.Run(() =>
                    FsHelpers.SafeEnumerateDirectories(path)
                        .Where(d => showHidden || !FsHelpers.IsHidden(d))
                        .OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase)
                        .ToList());
            }
            catch
            {
                directories = [];
            }

            // The sync path-reveal walk may have populated (or a reload reset)
            // this node while we enumerated — only fill it if the placeholder
            // is still in place.
            if (!HasExpandPlaceholder(item))
            {
                return;
            }

            item.Items.Clear();
            foreach (var directory in directories)
            {
                item.Items.Add(CreateFolderNode(directory));
            }
            if (directories.Count == 0)
            {
                // Nothing inside: collapse so the (now removed) expander state
                // doesn't leave an open empty node.
                item.IsExpanded = false;
            }
        }
        finally
        {
            _treeNodesLoading.Remove(item);
        }
    }

    private static bool HasExpandPlaceholder(TreeViewItem item) =>
        item.Items.Count == 1 && item.Items[0] is null;

    private void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        // A single click (selection change) reflects the folder in the active file
        // list. The _syncingFolderTree guard prevents the programmatic selection
        // made by SyncFolderTreeToPath from navigating back recursively.
        if (_syncingFolderTree)
        {
            return;
        }

        if (FolderTree.SelectedItem is TreeViewItem item && item.Tag is string path && Directory.Exists(path))
        {
            Navigate(_activeGrid, path, true);
        }
    }

    /// <summary>
    /// Double-click on a folder node toggles its expand / collapse state (open or
    /// close the subtree). Navigating the active file list is left to the single
    /// click (selection change) — see <see cref="FolderTree_SelectedItemChanged"/>.
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
            return; // single click: leave selection / navigation to the defaults
        }

        var node = e.OriginalSource as DependencyObject;
        while (node != null && node is not TreeView)
        {
            // The expander chevron toggles itself; don't double-handle it.
            if (node is System.Windows.Controls.Primitives.ToggleButton)
            {
                return;
            }

            if (node is TreeViewItem item && item.Tag is string)
            {
                // Only toggle nodes that actually have subfolders.
                if (item.Items.Count > 0)
                {
                    item.IsExpanded = !item.IsExpanded; // open <-> close
                    e.Handled = true;
                }
                return;
            }

            node = VisualTreeHelper.GetParent(node);
        }
    }

    // Last path the tree was synced to. Selection changes call
    // UpdateActivePane → QueueFolderTreeSyncToActivePane on every arrow-key
    // press; the sync itself walks and enumerates folders on the UI thread, so
    // skip it entirely while the pane's path hasn't changed. Reset to null
    // whenever the tree is rebuilt (LoadDrives) so the next sync re-runs.
    private string? _lastFolderTreeSyncPath;

    private void SyncFolderTreeToActivePane()
    {
        var path = GetCurrentPath(_activeGrid);
        if (string.Equals(path, _lastFolderTreeSyncPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        SyncFolderTreeToPath(path);
        _lastFolderTreeSyncPath = path;
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
            var accumulated = root;
            var rest = path.Length > root.Length ? path[root.Length..] : "";
            var parts = rest.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);

            foreach (var part in parts)
            {
                // `current` is an ancestor of the target here, so expand it to
                // reveal and realize the next level. The target node itself (the
                // final `current` after the loop) is left at its own expanded /
                // collapsed state so double-click stays in control of open/close.
                current.IsExpanded = true;
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
