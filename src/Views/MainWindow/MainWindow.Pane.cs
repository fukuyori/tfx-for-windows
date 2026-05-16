using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Tfx;

public partial class MainWindow
{
    private void Grid_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is DataGrid grid)
        {
            UpdateActivePane(grid);
        }
    }

    private void UpdateActivePane(DataGrid grid)
    {
        _activeGrid = grid;
        var focusBrush = (Brush)FindResource("TfxFocusBorder");
        var defaultBrush = (Brush)FindResource("TfxBorder");

        LeftPaneBorder.Background = grid == LeftGrid ? _activeBrush : _inactiveBrush;
        RightPaneBorder.Background = grid == RightGrid ? _activeBrush : _inactiveBrush;
        LeftPaneBorder.BorderBrush = grid == LeftGrid ? focusBrush : defaultBrush;
        RightPaneBorder.BorderBrush = grid == RightGrid ? focusBrush : defaultBrush;
        LeftPaneBorder.BorderThickness = new Thickness(grid == LeftGrid ? 2 : 1);
        RightPaneBorder.BorderThickness = new Thickness(grid == RightGrid ? 2 : 1);
        UpdatePathText();
        QueueFolderTreeSyncToActivePane();
    }

    private void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection)
        {
            return;
        }

        if (sender is DataGrid grid)
        {
            UpdateActivePane(grid);
            SchedulePreviewUpdate(SelectedItems(grid).FirstOrDefault());
            UpdateStatus();
        }
    }

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid grid && grid.SelectedItem is FileItem item)
        {
            OpenItem(grid, item);
        }
    }

    private void SetSplitVisible(bool visible)
    {
        PaneSplitterColumn.Width = visible ? new GridLength(5) : new GridLength(0);
        if (visible)
        {
            ApplyPaneSplitRatio();
        }
        else
        {
            RightPaneColumn.Width = new GridLength(0);
            if (_activeGrid == RightGrid)
            {
                UpdateActivePane(LeftGrid);
            }
        }
        RightPaneBorder.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        SplitButton.IsChecked = visible;
    }

    private void ApplyPaneSplitRatio()
    {
        var ratio = Math.Clamp(_settings.LeftPaneRatio, 0.15, 0.85);
        LeftPaneColumn.Width = new GridLength(ratio, GridUnitType.Star);
        RightPaneColumn.Width = new GridLength(1 - ratio, GridUnitType.Star);
    }

    private void Split_Click(object sender, RoutedEventArgs e)
    {
        SetSplitVisible(RightPaneColumn.Width.Value == 0);
        SaveSettings();
    }
}
