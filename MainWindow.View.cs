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
        UpdatePreview(lb.SelectedItem as FileItem);
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

        if (node is ListBoxItem lbItem && lbItem.Content is FileItem fi && !fi.IsParent)
        {
            if (!lb.SelectedItems.Contains(fi))
            {
                lb.SelectedItems.Clear();
                lb.SelectedItems.Add(fi);
            }
        }

        UpdateActivePane(SideOf(lb));
    }

    private void IconView_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not ListBox lb)
        {
            e.Handled = true;
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

        lb.ContextMenu = BuildGridContextMenu(grid);
    }

    private void IconView_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || sender is not ListBox lb)
        {
            _dragStart = e.GetPosition(this);
            return;
        }

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var paths = lb.SelectedItems.OfType<FileItem>().Where(i => !i.IsParent).Select(i => i.FullPath).ToArray();
        if (paths.Length == 0)
        {
            return;
        }

        var data = new DataObject();
        var collection = new StringCollection();
        collection.AddRange(paths);
        data.SetFileDropList(collection);
        var effect = DragDrop.DoDragDrop(lb, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);

        if (effect != DragDropEffects.None)
        {
            Reload(LeftGrid);
            Reload(RightGrid);
        }
    }
}
