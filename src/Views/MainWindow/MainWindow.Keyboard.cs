using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Tfx;

public partial class MainWindow
{
    private bool InArchiveContext => ArchivePath.Contains(GetCurrentPath(_activeGrid));

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        if (ctrl && e.Key == Key.F)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.F4)
        {
            GetActivePathBar().EnterEditMode();
            e.Handled = true;
        }
        else if (ctrl && shift && e.Key == Key.N)
        {
            if (!InArchiveContext) NewFile();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.N)
        {
            if (!InArchiveContext) NewFolder();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.K)
        {
            if (!InArchiveContext) CompressSelection();
            e.Handled = true;
        }
        else if (ctrl && shift && e.Key == Key.E)
        {
            if (!InArchiveContext) ExtractSelectedArchives();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.R)
        {
            Reload(_activeGrid);
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.C)
        {
            CopySelection(false);
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.X)
        {
            if (!InArchiveContext) CopySelection(true);
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.V)
        {
            if (!InArchiveContext) PasteIntoActivePane();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.A)
        {
            if (_settings.ViewMode == ViewMode.Icons)
            {
                (_activeGrid == LeftGrid ? LeftIconView : RightIconView).SelectAll();
            }
            else
            {
                _activeGrid.SelectAll();
            }
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.Oem4)
        {
            NavigateBack();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.Oem6)
        {
            NavigateForward();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.Up)
        {
            NavigateParent();
            e.Handled = true;
        }
        else if (ctrl && shift && e.Key == Key.T)
        {
            if (!InArchiveContext) OpenTerminal();
            e.Handled = true;
        }
        else if (ctrl && shift && e.Key == Key.OemPeriod)
        {
            ToggleHidden();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            if (!InArchiveContext)
            {
                if (shift)
                {
                    DeletePermanently();
                }
                else
                {
                    MoveSelectionToTrash();
                }
            }
            e.Handled = true;
        }
        else if (e.Key == Key.F2 && _activeGrid.SelectedItem is FileItem renameItem && !renameItem.IsParent)
        {
            if (!InArchiveContext) StartRename(_activeGrid, renameItem);
            e.Handled = true;
        }
        else if (e.Key == Key.Back)
        {
            NavigateParent();
            e.Handled = true;
        }
        else if (e.Key == Key.Left)
        {
            UpdateActivePane(LeftGrid);
            LeftGrid.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Right && RightPaneColumn.Width.Value > 0)
        {
            UpdateActivePane(RightGrid);
            RightGrid.Focus();
            e.Handled = true;
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var inTextBox = Keyboard.FocusedElement is TextBox;

        if (e.Key == Key.Tab && !inTextBox)
        {
            if (HandleTabFocusCycle())
            {
                e.Handled = true;
            }
            return;
        }

        if (ctrl && e.Key == Key.L && !inTextBox)
        {
            GetActivePathBar().EnterEditMode();
            e.Handled = true;
            return;
        }

        if (!inTextBox && e.Key is Key.Up or Key.Down && IsFocusInActiveListing() && ActiveListingSelectedItem() is null)
        {
            MoveActiveListingSelection(e.Key);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete && !inTextBox)
        {
            if (!InArchiveContext)
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    DeletePermanently();
                }
                else
                {
                    MoveSelectionToTrash();
                }
            }
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && !inTextBox && _activeGrid.SelectedItem is FileItem item)
        {
            OpenItem(_activeGrid, item);
            e.Handled = true;
        }
    }

    private bool IsFocusInActiveListing()
    {
        var focused = Keyboard.FocusedElement as DependencyObject;
        if (_settings.ViewMode == ViewMode.Icons)
        {
            return IsInside(focused, _activeGrid == LeftGrid ? LeftIconView : RightIconView);
        }

        return IsInside(focused, _activeGrid);
    }

    private object? ActiveListingSelectedItem() =>
        _settings.ViewMode == ViewMode.Icons
            ? (_activeGrid == LeftGrid ? LeftIconView : RightIconView).SelectedItem
            : _activeGrid.SelectedItem;

    private bool HandleTabFocusCycle()
    {
        var focused = Keyboard.FocusedElement as DependencyObject;
        var inFolderTree = IsInside(focused, FolderTree);
        var inLeft = IsInside(focused, LeftGrid);
        var inRight = IsInside(focused, RightGrid);

        if (!inFolderTree && !inLeft && !inRight)
        {
            return false;
        }

        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var rightVisible = RightPaneColumn.Width.Value > 0;

        var sequence = new List<IInputElement> { FolderTree, LeftGrid };
        if (rightVisible)
        {
            sequence.Add(RightGrid);
        }

        var currentIdx = inFolderTree ? 0 : (inLeft ? 1 : 2);
        if (currentIdx >= sequence.Count)
        {
            currentIdx = sequence.Count - 1;
        }

        var step = shift ? -1 : 1;
        var nextIdx = ((currentIdx + step) % sequence.Count + sequence.Count) % sequence.Count;

        FocusView(sequence[nextIdx]);
        return true;
    }

    private void FocusView(IInputElement element)
    {
        if (element is DataGrid grid)
        {
            if (grid.SelectedItem == null && grid.Items.Count > 0)
            {
                grid.SelectedIndex = 0;
            }
            grid.Focus();
            UpdateActivePane(grid);
        }
        else if (element is TreeView tree)
        {
            if (tree.SelectedItem is TreeViewItem selected)
            {
                selected.Focus();
            }
            else
            {
                tree.Focus();
            }
        }
        else
        {
            element.Focus();
        }
    }

    private static bool IsInside(DependencyObject? element, DependencyObject ancestor)
    {
        while (element != null)
        {
            if (element == ancestor)
            {
                return true;
            }

            DependencyObject? parent = null;
            if (element is Visual or System.Windows.Media.Media3D.Visual3D)
            {
                parent = VisualTreeHelper.GetParent(element);
            }
            parent ??= LogicalTreeHelper.GetParent(element);
            element = parent;
        }
        return false;
    }
}
