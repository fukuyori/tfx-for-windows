using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Tfx;

public partial class MainWindow
{
    private void ApplyViewMode()
    {
        var icons = _settings.ViewMode == ViewMode.Icons;
        LeftGrid.Visibility = icons ? Visibility.Collapsed : Visibility.Visible;
        RightGrid.Visibility = icons ? Visibility.Collapsed : Visibility.Visible;
        LeftIconView.Visibility = icons ? Visibility.Visible : Visibility.Collapsed;
        RightIconView.Visibility = icons ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ViewMode_Click(object sender, RoutedEventArgs e)
    {
        _settings.ViewMode = _settings.ViewMode == ViewMode.Details ? ViewMode.Icons : ViewMode.Details;
        ApplyViewMode();
        Reload(LeftGrid);
        Reload(RightGrid);
        SaveSettings();
    }

    private DataGrid SideOf(DependencyObject view) =>
        view == LeftIconView || view == LeftGrid ? LeftGrid : RightGrid;

    private void IconView_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is ListBox lb)
        {
            UpdateActivePane(SideOf(lb));
        }
    }

    private void IconView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection || sender is not ListBox lb)
        {
            return;
        }

        var grid = SideOf(lb);

        _syncingSelection = true;
        try
        {
            grid.SelectedItems.Clear();
            foreach (FileItem item in lb.SelectedItems)
            {
                grid.SelectedItems.Add(item);
            }
        }
        finally
        {
            _syncingSelection = false;
        }

        UpdateActivePane(grid);
        SchedulePreviewUpdate(lb.SelectedItems.OfType<FileItem>());
        UpdateStatus();
    }

    private void IconView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox lb && lb.SelectedItem is FileItem item)
        {
            OpenItem(SideOf(lb), item);
        }
    }

    private void IconView_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox lb)
        {
            return;
        }

        var node = e.OriginalSource as DependencyObject;
        while (node != null && node is not ListBoxItem && node is not ListBox)
        {
            node = VisualTreeHelper.GetParent(node);
        }

        FileItem? rowItem = null;
        if (node is ListBoxItem lbItem && lbItem.Content is FileItem fi)
        {
            rowItem = fi;
            if (!fi.IsParent && !lb.SelectedItems.Contains(fi))
            {
                lb.SelectedItems.Clear();
                lb.SelectedItems.Add(fi);
            }
        }

        UpdateActivePane(SideOf(lb));
        SyncIconSelectionToGrid(lb);
        lb.ContextMenu = BuildGridContextMenu(SideOf(lb));

        // Prime a possible right-button drag (mirror of Grid_PreviewMouseRightButtonDown).
        _dragStart = e.GetPosition(this);
        _pendingFileDragItem = null;
        _pendingFileDragPaths = [];
        if (rowItem is { IsParent: false })
        {
            _pendingFileDragItem = rowItem;
            var selectedItems = lb.SelectedItems.OfType<FileItem>().Where(i => !i.IsParent).ToArray();
            _pendingFileDragPaths = selectedItems.Contains(rowItem)
                ? selectedItems.Select(i => i.FullPath).ToArray()
                : [rowItem.FullPath];
        }
    }

    private void IconView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        _pendingFileDragItem = null;
        _pendingFileDragPaths = [];

        if (sender is not ListBox lb)
        {
            return;
        }

        UpdateActivePane(SideOf(lb));

        var itemContainer = FindVisualAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (itemContainer?.Content is not FileItem item)
        {
            BeginRubberBandSelection(null, lb, e);
            return;
        }

        if (item.IsParent)
        {
            return;
        }

        _pendingFileDragItem = item;
        var selectedItems = lb.SelectedItems.OfType<FileItem>().Where(i => !i.IsParent).ToArray();
        var itemAlreadySelected = selectedItems.Contains(item);
        _pendingFileDragPaths = itemAlreadySelected
            ? selectedItems.Select(i => i.FullPath).ToArray()
            : [item.FullPath];

        if (itemAlreadySelected && selectedItems.Length > 1 && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
        }
    }

    private void IconView_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (_suppressNextContextMenu)
        {
            _suppressNextContextMenu = false;
            e.Handled = true;
            return;
        }

        if (sender is not ListBox lb)
        {
            e.Handled = true;
            return;
        }

        SyncIconSelectionToGrid(lb);
        lb.ContextMenu = BuildGridContextMenu(SideOf(lb));
    }

    private void SyncIconSelectionToGrid(ListBox lb)
    {
        var grid = SideOf(lb);

        _syncingSelection = true;
        try
        {
            grid.SelectedItems.Clear();
            foreach (FileItem item in lb.SelectedItems)
            {
                grid.SelectedItems.Add(item);
            }
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private void IconView_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not ListBox lb)
        {
            return;
        }

        var leftPressed = e.LeftButton == MouseButtonState.Pressed;
        var rightPressed = e.RightButton == MouseButtonState.Pressed;

        if (!leftPressed && !rightPressed)
        {
            _dragStart = e.GetPosition(this);
            _pendingFileDragItem = null;
            _pendingFileDragPaths = [];
            return;
        }

        if (leftPressed && _isRubberBandSelecting)
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

        StartFileDrag(lb, isRightDrag: rightPressed && !leftPressed);
    }
}
