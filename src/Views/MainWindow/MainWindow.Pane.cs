using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Tfx;

internal enum Pane
{
    Left,
    Right
}

public partial class MainWindow
{
    private Pane PaneOf(DataGrid grid) => grid == LeftGrid ? Pane.Left : Pane.Right;

    private DataGrid GridOf(Pane pane) => pane == Pane.Left ? LeftGrid : RightGrid;

    private ListBox IconViewOf(Pane pane) => pane == Pane.Left ? LeftIconView : RightIconView;

    private BulkObservableCollection<FileItem> ItemsOf(Pane pane) => pane == Pane.Left ? LeftItems : RightItems;

    private string PathOf(Pane pane) => pane == Pane.Left ? _leftPath : _rightPath;

    private void SetPathOf(Pane pane, string value)
    {
        if (pane == Pane.Left)
        {
            _leftPath = value;
        }
        else
        {
            _rightPath = value;
        }
    }

    private Pane ActivePane => _activeGrid == LeftGrid ? Pane.Left : Pane.Right;

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
        UpdateGitBranchText();
        SyncPinnedSelectionToActivePane();
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
            SchedulePreviewUpdate(SelectedItems(grid));
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

    private void FolderTree_Click(object sender, RoutedEventArgs e) => ToggleFolderTree();

    private void ToggleFolderTree()
    {
        SetFolderTreeVisible(SidebarColumn.Width.Value == 0); // hidden → show
        SaveSettings();
    }

    /// <summary>
    /// Shows or hides the left sidebar (pinned folders + folder tree). Hiding it
    /// collapses the column and its splitter so the file panes get the space.
    /// </summary>
    private void SetFolderTreeVisible(bool visible)
    {
        if (visible)
        {
            var width = _settings.SidebarWidth >= 180 ? _settings.SidebarWidth : 260;
            SidebarColumn.MinWidth = 180;
            SidebarColumn.Width = new GridLength(width);
        }
        else
        {
            SidebarColumn.MinWidth = 0;
            SidebarColumn.Width = new GridLength(0);
        }
        SidebarSplitterColumn.Width = visible ? new GridLength(5) : new GridLength(0);
        SidebarBorder.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        SidebarSplitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        FolderTreeButton.IsChecked = visible;
    }

    private void ApplyPaneSplitRatio()
    {
        var ratio = Math.Clamp(_settings.LeftPaneRatio, 0.15, 0.85);
        LeftPaneColumn.Width = new GridLength(ratio, GridUnitType.Star);
        RightPaneColumn.Width = new GridLength(1 - ratio, GridUnitType.Star);
    }

    private void SwapPanes_Click(object sender, RoutedEventArgs e) => SwapPanes();

    private void SwapPanes()
    {
        if (RightPaneColumn.Width.Value <= 0)
        {
            // Single-pane view: nothing to swap.
            return;
        }

        var oldLeftPath = _leftPath;
        var oldRightPath = _rightPath;
        var activeWasLeft = _activeGrid == LeftGrid;

        if (string.Equals(oldLeftPath, oldRightPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Navigate(LeftGrid, oldRightPath, false);
        Navigate(RightGrid, oldLeftPath, false);

        // Follow the path: the active pane physically moves to the other side
        // so the user stays on the same folder they were viewing.
        UpdateActivePane(activeWasLeft ? RightGrid : LeftGrid);
        SaveSettings();
    }

    private void Split_Click(object sender, RoutedEventArgs e) => ToggleSplit();

    private void ToggleSplit()
    {
        var enabling = RightPaneColumn.Width.Value == 0;
        if (enabling)
        {
            // When the user toggles split back on, divide the single-pane area
            // evenly. The previous (possibly skewed) ratio is intentionally
            // discarded; the user can still drag the splitter afterwards, and
            // that new ratio will be saved.
            _settings.LeftPaneRatio = 0.5;
        }
        SetSplitVisible(enabling);

        // Open the right pane at the same folder as the left pane so the
        // user starts with a known location instead of an unrelated saved
        // path. Only when toggling on, and only when the two diverge.
        if (enabling && !string.IsNullOrEmpty(_leftPath) &&
            !string.Equals(_leftPath, _rightPath, StringComparison.OrdinalIgnoreCase))
        {
            Navigate(RightGrid, _leftPath, false);
        }

        SaveSettings();
    }
}
