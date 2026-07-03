using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Path = System.IO.Path;

namespace Tfx;

public partial class MainWindow
{
    private void LoadPinned()
    {
        _pinned.Clear();

        // Load saved pins as-is — do not call Directory.Exists on the UI thread.
        // Network pins that are temporarily offline must still show up so the
        // user can see (and unpin) them; clicking a stale pin fails through the
        // normal Navigate error path.
        foreach (var folder in _settings.PinnedFolders)
        {
            _pinned.Add(folder);
        }

        if (_pinned.Count == 0)
        {
            AddIfExists(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            AddIfExists(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
            AddIfExists(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            AddIfExists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
        }

        PinnedList.ItemsSource = _pinned;
        _pinned.CollectionChanged += Pinned_CollectionChanged;
    }

    private void AddIfExists(string path)
    {
        if (Directory.Exists(path) && !_pinned.Contains(path))
        {
            _pinned.Add(path);
        }
    }

    private void Pinned_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SaveSettings();
    }

    // Set true while SyncPinnedSelectionToActivePane is rewriting the
    // selection so PinnedList_SelectionChanged doesn't ricochet back into a
    // Navigate() call.
    private bool _syncingPinnedSelection;

    private void PinnedList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingPinnedSelection)
        {
            return;
        }
        if (PinnedList.SelectedItem is string path && Directory.Exists(path))
        {
            Navigate(_activeGrid, path, true);
        }
    }

    /// <summary>
    /// Highlight the pinned entry that matches the active pane's current
    /// folder (if any), otherwise clear the highlight. Called whenever the
    /// active pane navigates or the active pane itself switches.
    ///
    /// Without this sync the ListBox kept the last-clicked pin highlighted
    /// even after the user moved elsewhere via the file list / address bar.
    /// Re-clicking the same pin then did nothing, because <c>SelectionChanged</c>
    /// doesn't fire when the selection doesn't actually change.
    /// </summary>
    private void SyncPinnedSelectionToActivePane()
    {
        var activePath = GetCurrentPath(_activeGrid);
        string? match = null;
        foreach (var p in _pinned)
        {
            if (FsHelpers.SamePath(p, activePath))
            {
                match = p;
                break;
            }
        }
        _syncingPinnedSelection = true;
        try
        {
            if (match is null)
            {
                PinnedList.SelectedItem = null;
            }
            else if (!ReferenceEquals(PinnedList.SelectedItem, match))
            {
                PinnedList.SelectedItem = match;
            }
        }
        finally
        {
            _syncingPinnedSelection = false;
        }
    }

    private Point _pinnedMouseDownPoint;
    private bool _pinnedMouseDownOnItem;

    private void PinnedList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pinnedMouseDownPoint = e.GetPosition(PinnedList);
        _pinnedMouseDownOnItem =
            FindVisualAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is not null;
    }

    private void PinnedList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _pinnedMouseDownOnItem = false;
        // SelectionChanged doesn't fire when the clicked pin is already the
        // selected (highlighted) one, so navigation from that event alone made
        // some clicks do nothing. Navigate from the completed click itself;
        // when SelectionChanged already handled it, the pane is at that path
        // by now and this is a no-op.
        if (FindVisualAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is { Content: string path }
            && !FsHelpers.SamePath(GetCurrentPath(_activeGrid), path)
            && Directory.Exists(path))
        {
            Navigate(_activeGrid, path, true);
        }
    }

    private void PinnedList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed
            || !_pinnedMouseDownOnItem
            || PinnedList.SelectedItem is not string path)
        {
            return;
        }

        // Standard drag threshold. Without it, the 1–2 px of jitter in a
        // normal click immediately started an in-place reorder drag, which
        // swallowed the mouse-up — the click then neither changed the
        // selection nor navigated ("clicking a pin sometimes does nothing").
        var position = e.GetPosition(PinnedList);
        if (Math.Abs(position.X - _pinnedMouseDownPoint.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(position.Y - _pinnedMouseDownPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _pinnedMouseDownOnItem = false; // one drag per press
        DragDrop.DoDragDrop(PinnedList, path, DragDropEffects.Move);
    }

    private void PinnedList_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(string)) is string)
        {
            e.Effects = DragDropEffects.Move;
        }
        else if (GetPinnableDirectories(e.Data).Length > 0)
        {
            e.Effects = DragDropEffects.Link;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void PinnedList_Drop(object sender, DragEventArgs e)
    {
        var index = ComputePinnedDropIndex(e.GetPosition(PinnedList));

        if (e.Data.GetData(typeof(string)) is string reorderPath)
        {
            MovePinnedTo(reorderPath, ref index);
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            return;
        }

        var directories = GetPinnableDirectories(e.Data);
        if (directories.Length == 0)
        {
            return;
        }

        string? lastAdded = null;
        var addedCount = 0;
        foreach (var dir in directories)
        {
            if (!_pinned.Contains(dir))
            {
                addedCount++;
                lastAdded = dir;
            }
            MovePinnedTo(dir, ref index);
            index++;
        }

        if (addedCount == 1 && lastAdded != null)
        {
            SetStatus(Loc.F("Pinned {0}", lastAdded));
        }

        e.Effects = DragDropEffects.Link;
        e.Handled = true;
    }

    private static string[] GetPinnableDirectories(IDataObject data)
    {
        if (!data.GetDataPresent(DataFormats.FileDrop))
        {
            return [];
        }
        if (data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return [];
        }
        return paths.Where(Directory.Exists).ToArray();
    }

    private int ComputePinnedDropIndex(Point point)
    {
        for (var i = 0; i < PinnedList.Items.Count; i++)
        {
            if (PinnedList.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem item)
            {
                var bounds = VisualTreeHelper.GetDescendantBounds(item);
                var topLeft = item.TranslatePoint(new Point(), PinnedList);
                if (point.Y < topLeft.Y + bounds.Height / 2)
                {
                    return i;
                }
            }
        }
        return _pinned.Count;
    }

    private void MovePinnedTo(string path, ref int index)
    {
        var oldIndex = _pinned.IndexOf(path);
        if (oldIndex >= 0)
        {
            _pinned.RemoveAt(oldIndex);
            if (oldIndex < index)
            {
                index--;
            }
        }
        _pinned.Insert(Math.Clamp(index, 0, _pinned.Count), path);
    }

    private void PinnedList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var node = e.OriginalSource as DependencyObject;
        while (node != null && node is not ListBoxItem && node is not ListBox)
        {
            node = VisualTreeHelper.GetParent(node);
        }

        if (node is ListBoxItem item && item.Content is string path)
        {
            PinnedList.SelectedItem = path;
        }
    }

    private void PinnedList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (PinnedList.SelectedItem is not string path)
        {
            e.Handled = true;
            return;
        }

        var menu = new ContextMenu();
        var unpin = new MenuItem { Header = Loc.T("Unpin") };
        unpin.Click += (_, _) => UnpinPinnedFolder(path);
        menu.Items.Add(unpin);
        PinnedList.ContextMenu = menu;
    }

    private void TogglePin_Click(object sender, RoutedEventArgs e)
    {
        var path = GetCurrentPath(_activeGrid);
        if (ArchivePath.Contains(path))
        {
            return;
        }
        if (_pinned.Contains(path))
        {
            UnpinPinnedFolder(path);
        }
        else
        {
            _pinned.Add(path);
            SetStatus(Loc.F("Pinned {0}", path));
        }
    }

    private void UnpinPinnedFolder(string path)
    {
        if (_pinned.Remove(path))
        {
            SetStatus(Loc.F("Unpinned {0}", path));
        }
    }
}
