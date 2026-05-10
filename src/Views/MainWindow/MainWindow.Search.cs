using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace Tfx;

public partial class MainWindow
{
    private void ApplySearchFilter()
    {
        var filter = SearchBox.Text.Trim();
        ApplyGridFilter(LeftGrid, filter);
        ApplyGridFilter(RightGrid, filter);
    }

    private static void ApplyGridFilter(DataGrid grid, string filter)
    {
        if (grid.ItemsSource is null)
        {
            return;
        }

        var view = CollectionViewSource.GetDefaultView(grid.ItemsSource);
        if (view is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(filter))
        {
            view.Filter = null;
        }
        else
        {
            view.Filter = item => item is FileItem file &&
                                  (file.IsParent || file.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase));
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplySearchFilter();

    private void FocusSearch_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SearchBox.Text = "";
            FocusActiveListing();
            e.Handled = true;
        }
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!string.IsNullOrEmpty(SearchBox.Text) || Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        if (e.Key is Key.Down or Key.Up or Key.PageDown or Key.PageUp)
        {
            MoveActiveListingSelection(e.Key);
            e.Handled = true;
        }
    }
}
