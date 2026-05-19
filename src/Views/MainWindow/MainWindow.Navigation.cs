using System.IO;
using Microsoft.Win32;
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

        var current = GetCurrentPath(grid);
        if (pushHistory && IsNavigablePath(current) && !string.Equals(current, path, StringComparison.OrdinalIgnoreCase))
        {
            _back.Add(current);
            _forward.Clear();
        }

        if (grid == LeftGrid)
        {
            _leftPath = path;
        }
        else
        {
            _rightPath = path;
        }

        Reload(grid, selectName);
        UpdatePathText();
        if (grid == _activeGrid)
        {
            QueueFolderTreeSyncToActivePane();
        }
        UpdateWatcherForPane(PaneOf(grid));
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
            return;
        }

        var source = ItemsOf(pane);
        var item = source.FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.CurrentCultureIgnoreCase));
        if (item is null)
        {
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

        if (grid == _activeGrid)
        {
            FocusSelectedListingItem(grid, iconView, item);
            SchedulePreviewUpdate(item);
        }
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

    private void MoveActiveListingSelection(Key key)
    {
        var iconView = IconViewOf(ActivePane);
        var items = _settings.ViewMode == ViewMode.Icons ? iconView.Items : _activeGrid.Items;
        if (items.Count == 0)
        {
            FocusActiveListing();
            return;
        }

        var current = _settings.ViewMode == ViewMode.Icons
            ? iconView.SelectedItem
            : _activeGrid.SelectedItem;
        var currentIndex = current is null ? -1 : items.IndexOf(current);
        var step = key switch
        {
            Key.Up => -1,
            Key.PageUp => -10,
            Key.PageDown => 10,
            _ => 1
        };
        var nextIndex = currentIndex < 0
            ? 0
            : Math.Clamp(currentIndex + step, 0, items.Count - 1);

        if (items[nextIndex] is not FileItem item)
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
        SchedulePreviewUpdate(item);
        UpdateStatus();
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
        if (_back.Count == 0)
        {
            return;
        }

        var current = GetCurrentPath(_activeGrid);
        var path = _back[^1];
        _back.RemoveAt(_back.Count - 1);
        _forward.Add(current);
        Navigate(_activeGrid, path, false);
    }

    private void NavigateForward()
    {
        if (_forward.Count == 0)
        {
            return;
        }

        var current = GetCurrentPath(_activeGrid);
        var path = _forward[^1];
        _forward.RemoveAt(_forward.Count - 1);
        _back.Add(current);
        Navigate(_activeGrid, path, false);
    }

    private void OpenFolderPicker()
    {
        var dialog = new OpenFolderDialog
        {
            Title = Loc.T("Open folder"),
            InitialDirectory = GetCurrentPath(_activeGrid)
        };

        if (dialog.ShowDialog(this) == true && Directory.Exists(dialog.FolderName))
        {
            Navigate(_activeGrid, dialog.FolderName, true);
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e) => NavigateBack();

    private void Forward_Click(object sender, RoutedEventArgs e) => NavigateForward();

    private void Parent_Click(object sender, RoutedEventArgs e) => NavigateParent();

    private void OpenFolder_Click(object sender, RoutedEventArgs e) => OpenFolderPicker();

    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        Reload(LeftGrid);
        Reload(RightGrid);
    }
}
