using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Path = System.IO.Path;

namespace Tfx;

// Drop support for the FOLDERS sidebar tree. The tree is a drop target only:
// its nodes are never drag sources. Files / folders dragged from the file
// panes (or external apps) can be dropped onto a tree folder — same volume
// defaults to Move, across volumes to Copy (Shift / Ctrl / Alt override, same
// as the file panes), and a right-button drag pops the Copy / Move / Shortcut
// menu. Dropping a folder onto itself or any of its descendants is rejected.
public partial class MainWindow
{
    private static readonly TimeSpan TreeHoverExpandDelay = TimeSpan.FromMilliseconds(800);

    private TreeViewItem? _treeDropTarget;
    private DispatcherTimer? _treeHoverExpandTimer;
    private TreeViewItem? _treeHoverExpandItem;

    private void FolderTree_DragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;

        var target = FindVisualAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        if (target?.Tag is not string destination ||
            e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0 ||
            IsInvalidFolderDrop(paths, destination))
        {
            SetTreeDropHighlight(null);
            CancelTreeHoverExpand();
            e.Effects = DragDropEffects.None;
            return;
        }

        e.Effects = ResolveDropEffect(e, destination);
        SetTreeDropHighlight(target);
        ScheduleTreeHoverExpand(target);
    }

    private void FolderTree_DragLeave(object sender, DragEventArgs e)
    {
        // DragLeave also fires when moving between the tree's child elements;
        // only reset when the cursor has actually left the tree bounds.
        var p = e.GetPosition(FolderTree);
        if (p.X < 0 || p.Y < 0 || p.X > FolderTree.ActualWidth || p.Y > FolderTree.ActualHeight)
        {
            SetTreeDropHighlight(null);
            CancelTreeHoverExpand();
        }
    }

    private void FolderTree_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        SetTreeDropHighlight(null);
        CancelTreeHoverExpand();

        var target = FindVisualAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        if (target?.Tag is not string destination ||
            e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0 ||
            IsInvalidFolderDrop(paths, destination))
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        // Right-button drag: same Copy / Move / Shortcut / Cancel menu as the
        // file panes, opened after the drag's modal loop unwinds.
        if (_nativeRightDragInProgress || e.Data.GetDataPresent(TfxRightDragFormat))
        {
            e.Effects = DragDropEffects.Copy;
            var allowMoveLink = !paths.Any(ArchivePath.Contains);
            Dispatcher.BeginInvoke(() => ShowRightDragMenu(FolderTree, allowMoveLink, chosen =>
            {
                if (chosen is { } chosenEffect)
                {
                    ExecuteDrop(paths, destination, chosenEffect);
                }
            }));
            return;
        }

        var effect = ResolveDropEffect(e, destination);
        ExecuteDrop(paths, destination, effect);
        e.Effects = effect;
    }

    /// <summary>
    /// True when the drop must be rejected because the destination is one of
    /// the dragged items itself or a descendant of one (moving / copying a
    /// folder into its own subtree). One offending item rejects the whole
    /// multi-selection, like Explorer.
    /// </summary>
    private static bool IsInvalidFolderDrop(IReadOnlyList<string> paths, string destination)
    {
        try
        {
            var dest = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
            foreach (var path in paths)
            {
                var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
                if (string.Equals(source, dest, StringComparison.OrdinalIgnoreCase) ||
                    dest.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch
        {
            // Malformed path from an external source — let the shell decide.
        }
        return false;
    }

    /// <summary>
    /// Highlights the hovered tree node as the drop target. Deliberately not
    /// via selection: selecting a tree node navigates the active pane.
    /// </summary>
    private void SetTreeDropHighlight(TreeViewItem? item)
    {
        if (ReferenceEquals(_treeDropTarget, item))
        {
            return;
        }
        _treeDropTarget?.ClearValue(Control.BackgroundProperty);
        _treeDropTarget = item;
        item?.SetResourceReference(Control.BackgroundProperty, "TfxSelection");
    }

    /// <summary>
    /// Explorer-style hover expand: lingering over a collapsed node during a
    /// drag opens it so deeper folders become reachable drop targets.
    /// </summary>
    private void ScheduleTreeHoverExpand(TreeViewItem target)
    {
        if (ReferenceEquals(_treeHoverExpandItem, target))
        {
            return; // timer for this node is already running
        }
        CancelTreeHoverExpand();
        if (target.IsExpanded || target.Items.Count == 0)
        {
            return;
        }

        _treeHoverExpandItem = target;
        var timer = new DispatcherTimer { Interval = TreeHoverExpandDelay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (ReferenceEquals(_treeHoverExpandTimer, timer) && _treeHoverExpandItem is { } item)
            {
                item.IsExpanded = true; // lazily populates via FolderTree_Expanded
            }
        };
        _treeHoverExpandTimer = timer;
        timer.Start();
    }

    private void CancelTreeHoverExpand()
    {
        _treeHoverExpandTimer?.Stop();
        _treeHoverExpandTimer = null;
        _treeHoverExpandItem = null;
    }

    /// <summary>
    /// Refreshes the realized tree nodes affected by a copy / move (the
    /// destination and each source's parent) so the tree reflects the new
    /// folder layout without a full rebuild. Nodes that were never expanded
    /// keep their lazy placeholder and re-enumerate on expansion anyway.
    /// </summary>
    private void RefreshFolderTreeNodes(IReadOnlyList<string> sources, string destination)
    {
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { destination };
        foreach (var source in sources)
        {
            if (Path.GetDirectoryName(source) is { Length: > 0 } parent)
            {
                dirs.Add(parent);
            }
        }

        foreach (var dir in dirs)
        {
            if (FindRealizedTreeNode(dir) is { } node)
            {
                RefreshTreeNodeChildren(node);
            }
        }
    }

    /// <summary>Finds the tree node for a path among already-created nodes only
    /// (never expands or enumerates anything).</summary>
    private TreeViewItem? FindRealizedTreeNode(string path)
    {
        var wanted = Path.TrimEndingDirectorySeparator(path);

        TreeViewItem? Find(ItemCollection items)
        {
            foreach (var obj in items)
            {
                if (obj is not TreeViewItem item)
                {
                    continue;
                }
                if (item.Tag is string tag &&
                    string.Equals(Path.TrimEndingDirectorySeparator(tag), wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return item;
                }
                if (item.Items.Count > 0 && !HasExpandPlaceholder(item) && Find(item.Items) is { } found)
                {
                    return found;
                }
            }
            return null;
        }

        return Find(FolderTree.Items);
    }

    /// <summary>
    /// Re-enumerates a node's children against the disk, keeping existing
    /// child nodes (and their expansion state) for folders that still exist.
    /// </summary>
    private void RefreshTreeNodeChildren(TreeViewItem node)
    {
        if (node.Tag is not string path || HasExpandPlaceholder(node))
        {
            return;
        }

        List<string> directories;
        try
        {
            directories = VisibleDirectories(path)
                .OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch
        {
            return;
        }

        ApplyTreeNodeChildren(node, directories);
    }

    /// <summary>
    /// Mirrors a freshly loaded pane listing onto the realized FOLDERS-tree
    /// node for that path, so folders created / renamed / deleted in the pane
    /// appear in the tree. Uses the already-loaded items — no disk IO.
    /// </summary>
    private void SyncTreeNodeToListing(string path, IEnumerable<FileItem> items)
    {
        if (ArchivePath.Contains(path) || FindRealizedTreeNode(path) is not { } node)
        {
            return;
        }

        var directories = items
            .Where(i => i.IsDirectory && !i.IsParent)
            .Select(i => i.FullPath)
            .OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        ApplyTreeNodeChildren(node, directories);

        // Same rule as the pane→tree reveal: the active pane's folder node
        // stays expanded so the subfolders the list is showing (e.g. a folder
        // that was just created) are visible in the tree.
        if (node.Items.Count > 0 &&
            string.Equals(
                Path.TrimEndingDirectorySeparator(path),
                Path.TrimEndingDirectorySeparator(GetCurrentPath(_activeGrid)),
                StringComparison.OrdinalIgnoreCase))
        {
            node.IsExpanded = true;
        }
    }

    /// <summary>
    /// Makes a node's children match <paramref name="directories"/> (full
    /// paths, sorted), keeping existing child nodes — and their expansion
    /// state — for folders that still exist. No disk IO of its own.
    /// </summary>
    private void ApplyTreeNodeChildren(TreeViewItem node, IReadOnlyList<string> directories)
    {
        if (HasExpandPlaceholder(node))
        {
            return; // never expanded: enumerates on expansion anyway
        }

        var existing = node.Items.OfType<TreeViewItem>()
            .Where(i => i.Tag is string)
            .GroupBy(i => (string)i.Tag, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // Detaching the selected node (or one of its ancestors) during the
        // rebuild makes the TreeView move its selection, and the unguarded
        // SelectedItemChanged would then NAVIGATE the active pane (e.g. after
        // a paste, the pane jumped to the refreshed parent folder). Suppress
        // selection-driven navigation for the whole synchronous rebuild;
        // re-adding the same node instance (IsSelected still true) restores
        // the selection before the guard lifts.
        var previousGuard = _syncingFolderTree;
        _syncingFolderTree = true;
        try
        {
            // A realized-empty node (leaf) goes through the same rebuild: since
            // the child list is already known, the new nodes are added directly
            // — no lazy placeholder, which would render as a blank row and
            // would never populate without a collapse / re-expand.
            node.Items.Clear();
            foreach (var directory in directories)
            {
                node.Items.Add(existing.TryGetValue(directory, out var old) ? old : CreateFolderNode(directory));
            }

            if (node.Items.Count == 0)
            {
                // Lost its last subfolder: close the now-empty expander.
                // (Collapsing selects the node if a removed child was selected —
                // also covered by the guard above.)
                node.IsExpanded = false;
            }
        }
        finally
        {
            _syncingFolderTree = previousGuard;
        }
    }
}
