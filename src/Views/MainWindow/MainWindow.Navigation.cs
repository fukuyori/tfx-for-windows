using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Path = System.IO.Path;

namespace Tfx;

public partial class MainWindow
{
    private void Navigate(DataGrid grid, string path, bool pushHistory, string? selectName = "..")
    {
        if (!IsNavigablePath(path))
        {
            SetStatus(Loc.F("Folder not found: {0}", path));
            return;
        }

        // Navigating cancels any in-flight subfolder search and clears the
        // search box so the new folder shows real contents.
        if (_subfolderSearchActive)
        {
            CancelSubfolderSearch();
            SearchBox.Text = "";
        }

        var pane0 = PaneOf(grid);
        var tab = ActiveTab(pane0);
        var current = GetCurrentPath(grid);
        if (pushHistory && IsNavigablePath(current) && !string.Equals(current, path, StringComparison.OrdinalIgnoreCase))
        {
            tab.Back.Add(current);
            tab.Forward.Clear();
        }

        tab.Path = path;
        if (grid == LeftGrid)
        {
            _leftPath = path;
        }
        else
        {
            _rightPath = path;
        }
        RebuildTabStrip(pane0);

        Reload(grid, selectName);
        UpdatePathText();
        if (grid == _activeGrid)
        {
            QueueFolderTreeSyncToActivePane();
        }
        UpdateWatcherForPane(PaneOf(grid));
        RefreshGitStatusForPane(PaneOf(grid));
        SaveSettings();
    }

    private async void Reload(DataGrid grid, string? selectName = null)
    {
        var pane = PaneOf(grid);
        var path = GetCurrentPath(grid);
        var target = ItemsOf(pane);
        target.Clear();
        var loadLargeIcons = _settings.ViewMode == ViewMode.Icons;
        var loadSmallIcons = !loadLargeIcons;
        var options = new DirectoryLoadOptions(
            ShowHidden,
            loadSmallIcons,
            loadLargeIcons,
            IsFileColumnVisible("Owner"));
        SetPendingSelectionName(pane, selectName);
        var cts = ReplaceReloadToken(pane);

        try
        {
            var items = await Task.Run(() => DirectoryLoader.Load(path, options, cts.Token), cts.Token);
            if (cts.IsCancellationRequested || !string.Equals(GetCurrentPath(grid), path, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            target.Clear();
            const int batchSize = 200;
            for (var i = 0; i < items.Count; i++)
            {
                target.Add(items[i]);
                if ((i + 1) % batchSize == 0 && i + 1 < items.Count)
                {
                    await Dispatcher.Yield(DispatcherPriority.Background);
                    if (cts.IsCancellationRequested || !string.Equals(GetCurrentPath(grid), path, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                }
            }

            ApplyPendingSelection(grid, pane);
            ApplyGitBadges(pane);
            // Refresh the pinned-list highlight so it matches the freshly
            // loaded folder (or clears if the path no longer corresponds to
            // any pinned entry). Without this the pinned list stays stuck on
            // the last-clicked entry, making re-click a no-op.
            if (PaneOf(grid) == ActivePane)
            {
                SyncPinnedSelectionToActivePane();
            }

            // First successful reload of the left pane after startup: force
            // focus onto the ".." row (or the first entry at a drive root)
            // so the user can immediately use Up / Down. This is the reliable
            // event-driven path; the Loaded handler in MainWindow.xaml.cs is
            // a belt-and-braces backup.
            if (!_initialLeftFocusDone && grid == LeftGrid && grid.Items.Count > 0)
            {
                _initialLeftFocusDone = true;
                if (grid.SelectedItem is null)
                {
                    grid.SelectedIndex = 0;
                }
                FocusPane(Pane.Left);
            }

            UpdateStatus();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private CancellationTokenSource ReplaceReloadToken(Pane pane)
    {
        var next = new CancellationTokenSource();
        var previous = pane == Pane.Left ? _leftReloadCts : _rightReloadCts;
        previous?.Cancel();
        previous?.Dispose();

        if (pane == Pane.Left)
        {
            _leftReloadCts = next;
        }
        else
        {
            _rightReloadCts = next;
        }

        return next;
    }

    private bool IsFileColumnVisible(string id) =>
        _settings.VisibleFileColumns.Any(column => string.Equals(column, id, StringComparison.OrdinalIgnoreCase));

    private void SetPendingSelectionName(Pane pane, string? name)
    {
        if (pane == Pane.Left)
        {
            _leftPendingSelectionName = name;
        }
        else
        {
            _rightPendingSelectionName = name;
        }
    }

    private string? TakePendingSelectionName(Pane pane)
    {
        if (pane == Pane.Left)
        {
            var value = _leftPendingSelectionName;
            _leftPendingSelectionName = null;
            return value;
        }

        var rightValue = _rightPendingSelectionName;
        _rightPendingSelectionName = null;
        return rightValue;
    }

    private void ApplyPendingSelection(DataGrid grid, Pane pane)
    {
        var name = TakePendingSelectionName(pane);
        if (string.IsNullOrWhiteSpace(name))
        {
            TakePendingRename(pane);
            return;
        }

        var source = ItemsOf(pane);
        var item = source.FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.CurrentCultureIgnoreCase));
        if (item is null)
        {
            TakePendingRename(pane);
            return;
        }

        var iconView = IconViewOf(pane);
        _syncingSelection = true;
        try
        {
            grid.SelectedItems.Clear();
            grid.SelectedItem = item;
            grid.ScrollIntoView(item);

            iconView.SelectedItems.Clear();
            iconView.SelectedItem = item;
            iconView.ScrollIntoView(item);
        }
        finally
        {
            _syncingSelection = false;
        }

        var renaming = TakePendingRename(pane);
        if (grid == _activeGrid)
        {
            if (renaming)
            {
                // Explorer-style new item: go straight to inline rename. The normal
                // focus-retry queue is intentionally skipped here — it refocuses the
                // cell/row at ContextIdle/ApplicationIdle right after BeginEdit and
                // would drop us back out of edit mode.
                Dispatcher.BeginInvoke(() => BeginRenameNewItem(grid, item, 0), DispatcherPriority.Background);
            }
            else
            {
                FocusSelectedListingItem(grid, iconView, item);
                SchedulePreviewUpdate(item);
            }
        }
    }

    /// <summary>
    /// Selects the row, makes its name cell current, and enters inline edit mode.
    /// Retries at Background priority until the row container is realized and
    /// <see cref="DataGrid.BeginEdit()"/> succeeds (virtualized rows are not
    /// generated until a layout pass after the items are added).
    /// </summary>
    private void BeginRenameNewItem(DataGrid grid, FileItem item, int attempt)
    {
        var nameColumn = grid == LeftGrid ? LeftNameColumn : RightNameColumn;

        grid.IsReadOnly = false;
        grid.SelectedItem = item;
        grid.ScrollIntoView(item);
        grid.UpdateLayout();

        var realized = grid.ItemContainerGenerator.ContainerFromItem(item) is DataGridRow;
        if (realized)
        {
            grid.CurrentCell = new DataGridCellInfo(item, nameColumn);
            grid.Focus();
            if (grid.BeginEdit())
            {
                return;
            }
        }

        if (attempt < 8)
        {
            Dispatcher.BeginInvoke(() => BeginRenameNewItem(grid, item, attempt + 1), DispatcherPriority.Background);
        }
    }

    private void SetPendingRename(Pane pane, bool value)
    {
        if (pane == Pane.Left)
        {
            _leftPendingRename = value;
        }
        else
        {
            _rightPendingRename = value;
        }
    }

    private bool TakePendingRename(Pane pane)
    {
        if (pane == Pane.Left)
        {
            var value = _leftPendingRename;
            _leftPendingRename = false;
            return value;
        }

        var rightValue = _rightPendingRename;
        _rightPendingRename = false;
        return rightValue;
    }

    private void FocusSelectedListingItem(DataGrid grid, ListBox iconView, FileItem item)
    {
        var listing = _settings.ViewMode == ViewMode.Icons ? (Control)iconView : grid;
        listing.Focus();

        QueueSelectedListingItemFocus(grid, iconView, item, DispatcherPriority.Input);
        QueueSelectedListingItemFocus(grid, iconView, item, DispatcherPriority.ContextIdle);
        QueueSelectedListingItemFocus(grid, iconView, item, DispatcherPriority.ApplicationIdle);
    }

    private void QueueSelectedListingItemFocus(DataGrid grid, ListBox iconView, FileItem item, DispatcherPriority priority)
    {
        Dispatcher.BeginInvoke(() => FocusSelectedListingItemNow(grid, iconView, item), priority);
    }

    private void FocusSelectedListingItemNow(DataGrid grid, ListBox iconView, FileItem item)
    {
        if (grid != _activeGrid)
        {
            return;
        }

        if (_settings.ViewMode == ViewMode.Icons)
        {
            iconView.UpdateLayout();
            if (iconView.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem listBoxItem)
            {
                FocusElement(listBoxItem);
                return;
            }

            FocusElement(iconView);
            return;
        }

        grid.UpdateLayout();
        if (grid.ItemContainerGenerator.ContainerFromItem(item) is DataGridRow row)
        {
            if (FocusFileNameCell(grid, row, item))
            {
                return;
            }

            FocusElement(row);
            return;
        }

        FocusElement(grid);
    }

    private bool FocusFileNameCell(DataGrid grid, DataGridRow row, FileItem item)
    {
        var nameColumn = grid == LeftGrid ? LeftNameColumn : RightNameColumn;
        grid.CurrentCell = new DataGridCellInfo(item, nameColumn);
        grid.ScrollIntoView(item, nameColumn);
        row.ApplyTemplate();

        var presenter = FindVisualChild<DataGridCellsPresenter>(row);
        if (presenter is null)
        {
            grid.UpdateLayout();
            presenter = FindVisualChild<DataGridCellsPresenter>(row);
        }

        if (presenter?.ItemContainerGenerator.ContainerFromIndex(nameColumn.DisplayIndex) is not DataGridCell cell)
        {
            return false;
        }

        cell.IsSelected = true;
        FocusElement(cell);
        return Keyboard.FocusedElement == cell;
    }

    private void FocusActiveListing()
    {
        var iconView = IconViewOf(ActivePane);
        var selected = _settings.ViewMode == ViewMode.Icons
            ? iconView.SelectedItem as FileItem
            : _activeGrid.SelectedItem as FileItem;
        if (selected is not null)
        {
            FocusSelectedListingItemNow(_activeGrid, iconView, selected);
            return;
        }

        FocusElement(_settings.ViewMode == ViewMode.Icons ? iconView : _activeGrid);
    }

    // Range-selection state for Shift+Up/Down/PageUp/PageDown in the listing.
    // _anchor is the fixed end of the range; _lead is the moving (focused) end.
    private int _listingAnchorIndex = -1;
    private int _listingLeadIndex = -1;

    /// <summary>The smallest active-listing index among the given items (0 if none).</summary>
    private int FirstSelectedListingIndex(IReadOnlyList<FileItem> selection)
    {
        var items = _settings.ViewMode == ViewMode.Icons ? IconViewOf(ActivePane).Items : _activeGrid.Items;
        var min = int.MaxValue;
        foreach (var s in selection)
        {
            var idx = items.IndexOf(s);
            if (idx >= 0 && idx < min)
            {
                min = idx;
            }
        }
        return min == int.MaxValue ? 0 : min;
    }

    /// <summary>
    /// Diff-refreshes both panes after a mutation (delete) so the lists update in
    /// place without flicker, then selects/focuses the active listing row at
    /// <paramref name="focusIndex"/> (clamped) so focus stays on a sensible
    /// neighbour of the removed item.
    /// </summary>
    private async void RefreshActivePaneAfterMutation(int focusIndex)
    {
        var pane = ActivePane;
        await ReloadDiffAsync(GridOf(pane));
        _ = ReloadDiffAsync(GridOf(pane == Pane.Left ? Pane.Right : Pane.Left));
        if (pane == ActivePane)
        {
            SelectAndFocusActiveIndex(focusIndex);
        }
    }

    private void SelectAndFocusActiveIndex(int index)
    {
        var iconView = IconViewOf(ActivePane);
        var items = _settings.ViewMode == ViewMode.Icons ? iconView.Items : _activeGrid.Items;
        if (items.Count == 0)
        {
            FocusActiveListing();
            return;
        }

        var i = Math.Clamp(index, 0, items.Count - 1);
        if (items[i] is not FileItem item)
        {
            FocusActiveListing();
            return;
        }

        _syncingSelection = true;
        try
        {
            _activeGrid.SelectedItems.Clear();
            _activeGrid.SelectedItem = item;
            _activeGrid.ScrollIntoView(item);

            iconView.SelectedItems.Clear();
            iconView.SelectedItem = item;
            iconView.ScrollIntoView(item);
        }
        finally
        {
            _syncingSelection = false;
        }

        FocusSelectedListingItemNow(_activeGrid, iconView, item);
    }

    private void MoveActiveListingSelection(Key key, bool extend = false)
    {
        var iconView = IconViewOf(ActivePane);
        var items = _settings.ViewMode == ViewMode.Icons ? iconView.Items : _activeGrid.Items;
        if (items.Count == 0)
        {
            FocusActiveListing();
            return;
        }

        var lastIndex = items.Count - 1;
        var current = _settings.ViewMode == ViewMode.Icons
            ? iconView.SelectedItem
            : _activeGrid.SelectedItem;
        var currentIndex = current is null ? -1 : items.IndexOf(current);

        // Re-anchor when the selection moved outside keyboard navigation (e.g. a
        // mouse click), so a following Shift+arrow extends from the new spot.
        if (_listingLeadIndex != currentIndex)
        {
            _listingAnchorIndex = currentIndex;
            _listingLeadIndex = currentIndex;
        }

        var leadIndex = _listingLeadIndex;

        int nextIndex;
        if (leadIndex < 0)
        {
            // No selection (e.g. just after a rename or focus loss): land on the
            // first item (the ".." parent row when one exists).
            nextIndex = 0;
        }
        else if (!extend && key == Key.Up && leadIndex == 0)
        {
            // ".." + Up wraps to the bottom of the listing (single-select only).
            nextIndex = lastIndex;
        }
        else if (!extend && key == Key.Down && leadIndex == lastIndex)
        {
            // Last entry + Down wraps to ".." (single-select only).
            nextIndex = 0;
        }
        else
        {
            var step = key switch
            {
                Key.Up => -1,
                Key.PageUp => -10,
                Key.PageDown => 10,
                _ => 1, // Down
            };
            nextIndex = Math.Clamp(leadIndex + step, 0, lastIndex);
        }

        if (items[nextIndex] is not FileItem item)
        {
            FocusActiveListing();
            return;
        }

        var extending = extend && _listingAnchorIndex >= 0 && _listingAnchorIndex <= lastIndex;

        _syncingSelection = true;
        try
        {
            if (extending)
            {
                SelectListingRange(items, iconView, _listingAnchorIndex, nextIndex, item);
            }
            else
            {
                _listingAnchorIndex = nextIndex;
                _activeGrid.SelectedItems.Clear();
                _activeGrid.SelectedItem = item;
                _activeGrid.ScrollIntoView(item);

                iconView.SelectedItems.Clear();
                iconView.SelectedItem = item;
                iconView.ScrollIntoView(item);
            }
            _listingLeadIndex = nextIndex;
        }
        finally
        {
            _syncingSelection = false;
        }

        if (extending)
        {
            // Do NOT focus the lead cell/row here: focusing a cell makes the
            // DataGrid/ListBox collapse the Extended selection back to that single
            // row. Focus is already inside the listing, so subsequent Shift+arrow
            // keys keep working via the tracked anchor/lead indices.
            SchedulePreviewUpdate(SelectedItems(_activeGrid));
        }
        else
        {
            FocusSelectedListingItemNow(_activeGrid, iconView, item);
            SchedulePreviewUpdate(item);
        }
        UpdateStatus();
    }

    /// <summary>Selects the inclusive range [anchor, lead] in both listing views.</summary>
    private void SelectListingRange(ItemCollection items, ListBox iconView, int anchor, int lead, FileItem leadItem)
    {
        var lo = Math.Min(anchor, lead);
        var hi = Math.Max(anchor, lead);
        var range = new List<FileItem>();
        for (var i = lo; i <= hi; i++)
        {
            if (items[i] is FileItem fi)
            {
                range.Add(fi);
            }
        }

        // Set SelectedItem FIRST: the Selector.SelectedItem setter collapses the
        // selection to that single item, so it must run before adding the rest of
        // the range (doing it last would wipe the range). leadItem stays the
        // primary/current item, which keeps the anchor/lead bookkeeping correct.
        _activeGrid.SelectedItem = leadItem;
        foreach (var fi in range)
        {
            if (!ReferenceEquals(fi, leadItem))
            {
                _activeGrid.SelectedItems.Add(fi);
            }
        }
        _activeGrid.ScrollIntoView(leadItem);

        iconView.SelectedItem = leadItem;
        foreach (var fi in range)
        {
            if (!ReferenceEquals(fi, leadItem))
            {
                iconView.SelectedItems.Add(fi);
            }
        }
        iconView.ScrollIntoView(leadItem);
    }

    private static void FocusElement(IInputElement element)
    {
        if (element is Control control)
        {
            control.Focus();
            FocusManager.SetFocusedElement(FocusManager.GetFocusScope(control), control);
        }

        Keyboard.Focus(element);
    }

    private void NavigateParent()
    {
        var current = GetCurrentPath(_activeGrid);
        if (ArchivePath.TryParse(current, out var archive, out var inner))
        {
            var parent = ArchivePath.GetParent(current);
            if (string.IsNullOrEmpty(parent))
            {
                return;
            }
            var selectName = string.IsNullOrEmpty(inner)
                ? Path.GetFileName(archive)
                : (inner.TrimEnd('/').Split('/').LastOrDefault() ?? "");
            Navigate(_activeGrid, parent, true, selectName);
            return;
        }

        var parentDir = Directory.GetParent(current);
        if (parentDir is not null)
        {
            Navigate(_activeGrid, parentDir.FullName, true, Path.GetFileName(current.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
        }
    }

    private static bool IsNavigablePath(string path)
    {
        if (ArchivePath.TryParse(path, out var archive, out _))
        {
            return File.Exists(archive);
        }
        return Directory.Exists(path);
    }

    private void NavigateBack()
    {
        var tab = ActiveTab(ActivePane);
        if (tab.Back.Count == 0)
        {
            return;
        }

        var current = GetCurrentPath(_activeGrid);
        var path = tab.Back[^1];
        tab.Back.RemoveAt(tab.Back.Count - 1);
        tab.Forward.Add(current);
        Navigate(_activeGrid, path, false);
    }

    private void NavigateForward()
    {
        var tab = ActiveTab(ActivePane);
        if (tab.Forward.Count == 0)
        {
            return;
        }

        var current = GetCurrentPath(_activeGrid);
        var path = tab.Forward[^1];
        tab.Forward.RemoveAt(tab.Forward.Count - 1);
        tab.Back.Add(current);
        Navigate(_activeGrid, path, false);
    }

    private void Back_Click(object sender, RoutedEventArgs e) => NavigateBack();

    private void Forward_Click(object sender, RoutedEventArgs e) => NavigateForward();

    private void Parent_Click(object sender, RoutedEventArgs e) => NavigateParent();

    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        Reload(LeftGrid);
        Reload(RightGrid);
    }
}
