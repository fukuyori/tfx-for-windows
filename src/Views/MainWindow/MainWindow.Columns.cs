using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace Tfx;

public partial class MainWindow
{
    private void InitializeFileColumns()
    {
        var defs = new List<FileColumnDefinition>
        {
            new("Name", Loc.T("Name"), LeftNameColumn, RightNameColumn),
            new("Git", Loc.T("Git"), LeftGitColumn, RightGitColumn),
            new("DateModified", Loc.T("Date modified"), LeftDateModifiedColumn, RightDateModifiedColumn),
            new("Type", Loc.T("Type"), LeftTypeColumn, RightTypeColumn),
            new("Size", Loc.T("Size"), LeftSizeColumn, RightSizeColumn),
            new("DateCreated", Loc.T("Date created"), LeftDateCreatedColumn, RightDateCreatedColumn),
            new("Owner", Loc.T("Owner"), LeftOwnerColumn, RightOwnerColumn),
            new("Attribute", Loc.T("Attribute"), LeftAttributeColumn, RightAttributeColumn)
        };
        foreach (var def in defs)
        {
            def.Left.Header = def.Title;
            def.Right.Header = def.Title;
        }

        _fileColumns.Clear();

        var savedOrder = (_settings.FileColumnOrder ?? [])
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var id in savedOrder)
        {
            var def = defs.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
            if (def is not null)
            {
                _fileColumns.Add(def);
            }
        }

        foreach (var def in defs)
        {
            if (!_fileColumns.Any(c => string.Equals(c.Id, def.Id, StringComparison.OrdinalIgnoreCase)))
            {
                _fileColumns.Add(def);
            }
        }

        var validIds = defs.Select(d => d.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _settings.VisibleFileColumns = _settings.VisibleFileColumns
            .Where(id => validIds.Contains(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (_settings.VisibleFileColumns.Count == 0)
        {
            _settings.VisibleFileColumns = _fileColumns.Select(c => c.Id).ToList();
        }
    }

    private void ApplyColumnOrder()
    {
        for (var i = 0; i < _fileColumns.Count; i++)
        {
            _fileColumns[i].Left.DisplayIndex = i;
            _fileColumns[i].Right.DisplayIndex = i;
        }
    }

    private bool _suspendColumnReorder;

    /// <summary>
    /// Persists a header drag-reorder: rebuilds the column order from the grid
    /// the user dragged in, mirrors it onto the other pane, and saves. Without
    /// this, a dragged order is lost on the next reload and conflicts with the
    /// Columns popup order.
    /// </summary>
    private void FileGrid_ColumnReordered(object sender, DataGridColumnEventArgs e)
    {
        if (_suspendColumnReorder || sender is not DataGrid grid)
        {
            return;
        }

        var ordered = grid.Columns
            .OrderBy(c => c.DisplayIndex)
            .Select(c => _fileColumns.FirstOrDefault(fc => ReferenceEquals(fc.Left, c) || ReferenceEquals(fc.Right, c)))
            .Where(fc => fc is not null)
            .Select(fc => fc!)
            .ToList();
        if (ordered.Count != _fileColumns.Count)
        {
            return; // unexpected; leave order untouched
        }

        _fileColumns.Clear();
        _fileColumns.AddRange(ordered);

        _suspendColumnReorder = true;
        try
        {
            ApplyColumnOrder(); // mirror the new order onto the other pane
        }
        finally
        {
            _suspendColumnReorder = false;
        }
        SaveSettings();
    }

    private void ApplyColumnVisibility()
    {
        var visible = _settings.VisibleFileColumns.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var column in _fileColumns)
        {
            var visibility = visible.Contains(column.Id) ? Visibility.Visible : Visibility.Collapsed;
            column.Left.Visibility = visibility;
            column.Right.Visibility = visibility;
        }
    }

    private void Grid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        if (sender is not DataGrid grid)
        {
            return;
        }

        e.Handled = true;

        var direction = e.Column.SortDirection != ListSortDirection.Ascending
            ? ListSortDirection.Ascending
            : ListSortDirection.Descending;

        foreach (var column in grid.Columns)
        {
            if (column != e.Column)
            {
                column.SortDirection = null;
            }
        }
        e.Column.SortDirection = direction;

        if (CollectionViewSource.GetDefaultView(grid.ItemsSource) is ListCollectionView view)
        {
            view.CustomSort = new FileItemComparer(e.Column.SortMemberPath ?? "Name", direction);
        }
    }

    private void Columns_Click(object sender, RoutedEventArgs e)
    {
        if ((DateTime.Now - _columnsClosedAt).TotalMilliseconds < 200)
        {
            return;
        }

        EnsureColumnsPopup();
        PopulateColumnsPanel();
        _columnsPopup!.IsOpen = true;
    }

    private void EnsureColumnsPopup()
    {
        if (_columnsPopup is not null)
        {
            return;
        }

        _columnsPanel = new StackPanel { Margin = new Thickness(6) };

        var border = new Border
        {
            Background = (Brush)FindResource("TfxFloatingPanel"),
            BorderBrush = (Brush)FindResource("TfxBorder"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6),
            SnapsToDevicePixels = true,
            Child = _columnsPanel
        };

        _columnsPopup = new Popup
        {
            PlacementTarget = ColumnsButton,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            Child = border
        };
        _columnsPopup.Closed += (_, _) => _columnsClosedAt = DateTime.Now;
    }

    private void PopulateColumnsPanel()
    {
        if (_columnsPanel is null)
        {
            return;
        }

        _columnsPanel.Children.Clear();

        var fg = (Brush)FindResource("TfxForeground");
        var muted = (Brush)FindResource("TfxMuted");
        var uiFont = new FontFamily("Yu Gothic UI, Meiryo, Segoe UI");
        var iconFont = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

        for (var i = 0; i < _fileColumns.Count; i++)
        {
            var col = _fileColumns[i];
            var index = i;

            var row = new Grid { Margin = new Thickness(2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var check = new CheckBox
            {
                Content = col.Title,
                IsChecked = col.Left.Visibility == Visibility.Visible,
                Foreground = fg,
                FontFamily = uiFont,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 8, 0)
            };
            check.Click += (_, _) =>
            {
                var willBeVisible = check.IsChecked == true;
                if (!willBeVisible && _fileColumns.Count(c => c.Left.Visibility == Visibility.Visible) <= 1)
                {
                    check.IsChecked = true;
                    SetStatus(Loc.T("At least one file column must be visible"));
                    return;
                }
                SetColumnVisibleInternal(col, willBeVisible);
            };
            Grid.SetColumn(check, 0);
            row.Children.Add(check);

            var up = new Button
            {
                Content = "\uE70E",
                FontFamily = iconFont,
                Foreground = muted,
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Width = 24,
                Height = 24,
                Margin = new Thickness(1, 0, 1, 0),
                IsEnabled = index > 0
            };
            up.Click += (_, _) => MoveColumn(col, -1);
            Grid.SetColumn(up, 1);
            row.Children.Add(up);

            var down = new Button
            {
                Content = "\uE70D",
                FontFamily = iconFont,
                Foreground = muted,
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Width = 24,
                Height = 24,
                Margin = new Thickness(1, 0, 1, 0),
                IsEnabled = index < _fileColumns.Count - 1
            };
            down.Click += (_, _) => MoveColumn(col, +1);
            Grid.SetColumn(down, 2);
            row.Children.Add(down);

            _columnsPanel.Children.Add(row);
        }
    }

    private void SetColumnVisibleInternal(FileColumnDefinition column, bool visible)
    {
        column.Left.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        column.Right.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        SaveSettings();
    }

    private void MoveColumn(FileColumnDefinition column, int delta)
    {
        var idx = _fileColumns.IndexOf(column);
        var target = idx + delta;
        if (idx < 0 || target < 0 || target >= _fileColumns.Count)
        {
            return;
        }

        _fileColumns.RemoveAt(idx);
        _fileColumns.Insert(target, column);

        ApplyColumnOrder();
        PopulateColumnsPanel();
        SaveSettings();
    }
}

public sealed record FileColumnDefinition(string Id, string Title, DataGridColumn Left, DataGridColumn Right);
