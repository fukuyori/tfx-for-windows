using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Tfx;

public partial class MainWindow
{
    private void BeginRubberBandSelection(DataGrid? grid, ListBox? listBox, MouseButtonEventArgs e)
    {
        var source = (FrameworkElement?)grid ?? listBox;
        if (source is null || e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        _pendingFileDragItem = null;
        _pendingFileDragPaths = [];
        _isRubberBandSelecting = true;
        _rubberBandSource = source;
        _rubberBandGrid = grid;
        _rubberBandListBox = listBox;
        _rubberBandStart = e.GetPosition(source);

        if (grid is not null)
        {
            UpdateActivePane(grid);
            if (Keyboard.Modifiers != ModifierKeys.Control)
            {
                grid.SelectedItems.Clear();
            }
        }
        else if (listBox is not null)
        {
            UpdateActivePane(SideOf(listBox));
            if (Keyboard.Modifiers != ModifierKeys.Control)
            {
                listBox.SelectedItems.Clear();
            }
        }

        ShowSelectionRect(source, new Rect(_rubberBandStart, _rubberBandStart));
        source.CaptureMouse();
        e.Handled = true;
    }

    private void UpdateRubberBandSelection(MouseEventArgs e)
    {
        if (!_isRubberBandSelecting || _rubberBandSource is null)
        {
            return;
        }

        var current = e.GetPosition(_rubberBandSource);
        var selectionRect = NormalizeRect(_rubberBandStart, current);
        ShowSelectionRect(_rubberBandSource, selectionRect);

        if (_rubberBandGrid is not null)
        {
            SelectGridItemsInRect(_rubberBandGrid, selectionRect);
        }
        else if (_rubberBandListBox is not null)
        {
            SelectIconItemsInRect(_rubberBandListBox, selectionRect);
        }

        e.Handled = true;
    }

    private void Listing_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isRubberBandSelecting)
        {
            if (TryGetListingItem(e.OriginalSource as DependencyObject, out var item))
            {
                if (_pendingFileDragItem == item && _pendingFileDragPaths.Length > 1)
                {
                    if (sender is DependencyObject view)
                    {
                        SelectSingleListingItem(SideOf(view), item);
                    }
                    ClearPendingFileDrag();
                    e.Handled = true;
                    return;
                }
            }

            ClearPendingFileDrag();
            return;
        }

        FinishRubberBandSelection();
        e.Handled = true;
    }

    private void FinishRubberBandSelection()
    {
        _rubberBandSource?.ReleaseMouseCapture();
        HideSelectionRect(_rubberBandSource);

        _isRubberBandSelecting = false;
        _rubberBandSource = null;
        _rubberBandGrid = null;
        _rubberBandListBox = null;
        UpdateStatus();
    }

    private void SelectSingleListingItem(DataGrid grid, FileItem item)
    {
        var iconView = grid == LeftGrid ? LeftIconView : RightIconView;

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

        UpdateActivePane(grid);
        UpdatePreview(item);
        UpdateStatus();
    }

    private void ClearPendingFileDrag()
    {
        _pendingFileDragItem = null;
        _pendingFileDragPaths = [];
    }

    private static bool TryGetListingItem(DependencyObject? source, out FileItem item)
    {
        var row = FindVisualAncestor<DataGridRow>(source);
        if (row?.Item is FileItem rowItem)
        {
            item = rowItem;
            return true;
        }

        var listBoxItem = FindVisualAncestor<ListBoxItem>(source);
        if (listBoxItem?.Content is FileItem iconItem)
        {
            item = iconItem;
            return true;
        }

        item = null!;
        return false;
    }

    private void SelectGridItemsInRect(DataGrid grid, Rect selectionRect)
    {
        _syncingSelection = true;
        try
        {
            grid.SelectedItems.Clear();
            foreach (var raw in grid.Items)
            {
                if (raw is not FileItem item || item.IsParent)
                {
                    continue;
                }

                if (grid.ItemContainerGenerator.ContainerFromItem(item) is DataGridRow row &&
                    ElementIntersects(row, grid, selectionRect))
                {
                    grid.SelectedItems.Add(item);
                }
            }
        }
        finally
        {
            _syncingSelection = false;
        }

        UpdatePreview(grid.SelectedItems.OfType<FileItem>().FirstOrDefault());
    }

    private void SelectIconItemsInRect(ListBox listBox, Rect selectionRect)
    {
        _syncingSelection = true;
        try
        {
            listBox.SelectedItems.Clear();
            foreach (var raw in listBox.Items)
            {
                if (raw is not FileItem item || item.IsParent)
                {
                    continue;
                }

                if (listBox.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem listBoxItem &&
                    ElementIntersects(listBoxItem, listBox, selectionRect))
                {
                    listBox.SelectedItems.Add(item);
                }
            }

            var grid = SideOf(listBox);
            grid.SelectedItems.Clear();
            foreach (FileItem item in listBox.SelectedItems)
            {
                grid.SelectedItems.Add(item);
            }
        }
        finally
        {
            _syncingSelection = false;
        }

        UpdatePreview(listBox.SelectedItems.OfType<FileItem>().FirstOrDefault());
    }

    private static bool ElementIntersects(FrameworkElement element, Visual relativeTo, Rect selectionRect)
    {
        if (!element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return false;
        }

        try
        {
            var transform = element.TransformToAncestor(relativeTo);
            var bounds = transform.TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            return selectionRect.IntersectsWith(bounds);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static Rect NormalizeRect(Point start, Point end) =>
        new(
            Math.Min(start.X, end.X),
            Math.Min(start.Y, end.Y),
            Math.Abs(start.X - end.X),
            Math.Abs(start.Y - end.Y));

    private void ShowSelectionRect(FrameworkElement source, Rect rect)
    {
        var (overlay, border) = GetSelectionOverlay(source);
        if (overlay is null || border is null)
        {
            return;
        }

        overlay.Visibility = Visibility.Visible;
        Canvas.SetLeft(border, rect.Left);
        Canvas.SetTop(border, rect.Top);
        border.Width = rect.Width;
        border.Height = rect.Height;
    }

    private void HideSelectionRect(FrameworkElement? source)
    {
        if (source is null)
        {
            return;
        }

        var (overlay, border) = GetSelectionOverlay(source);
        if (overlay is null || border is null)
        {
            return;
        }

        overlay.Visibility = Visibility.Collapsed;
        border.Width = 0;
        border.Height = 0;
    }

    private (Canvas? Overlay, Border? Border) GetSelectionOverlay(FrameworkElement source) =>
        SideOf(source) == LeftGrid
            ? (LeftSelectionOverlay, LeftSelectionRect)
            : (RightSelectionOverlay, RightSelectionRect);
}
