using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Tfx;

public partial class MainWindow
{
    private static readonly Dictionary<string, string> DefaultShortcutText = new(StringComparer.OrdinalIgnoreCase)
    {
        ["reload"] = "cmd+r",
        ["openTerminal"] = "cmd+t",
        ["togglePreview"] = "cmd+p",
        ["toggleSplit"] = "cmd+backslash",
        ["swapPanes"] = "cmd+shift+x",
        ["focusSearch"] = "cmd+f",
        ["toggleHidden"] = "cmd+shift+.",
        ["goBack"] = "cmd+[",
        ["goForward"] = "cmd+]",
        ["goUp"] = "cmd+up",
        ["openItem"] = "enter",
        ["newFolder"] = "cmd+n",
        ["newFile"] = "cmd+shift+n",
        ["rename"] = "f2",
        ["moveToTrash"] = "delete",
        ["compressToZip"] = "cmd+k",
        ["extractZip"] = "cmd+shift+e",
        ["copyItems"] = "cmd+c",
        ["cutItems"] = "cmd+x",
        ["pasteItems"] = "cmd+v",
        ["selectAll"] = "cmd+a",
    };

    private bool InArchiveContext => ArchivePath.Contains(GetCurrentPath(_activeGrid));

    private bool IsShortcut(string action, KeyEventArgs e) =>
        _shortcuts.TryGetValue(action, out var shortcut) && shortcut.Matches(e);

    private string ShortcutText(string action) =>
        _shortcuts.TryGetValue(action, out var shortcut) ? shortcut.DisplayText : "";

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        if (IsShortcut("focusSearch", e))
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
        else if (IsShortcut("newFile", e))
        {
            if (!InArchiveContext) NewFile();
            e.Handled = true;
        }
        else if (IsShortcut("newFolder", e))
        {
            if (!InArchiveContext) NewFolder();
            e.Handled = true;
        }
        else if (IsShortcut("compressToZip", e))
        {
            if (!InArchiveContext) CompressSelection();
            e.Handled = true;
        }
        else if (IsShortcut("extractZip", e))
        {
            if (!InArchiveContext) ExtractSelectedArchives();
            e.Handled = true;
        }
        else if (IsShortcut("reload", e))
        {
            Reload(_activeGrid);
            e.Handled = true;
        }
        else if (IsShortcut("copyItems", e))
        {
            CopySelection(false);
            e.Handled = true;
        }
        else if (IsShortcut("cutItems", e))
        {
            if (!InArchiveContext) CopySelection(true);
            e.Handled = true;
        }
        else if (IsShortcut("pasteItems", e))
        {
            if (!InArchiveContext) PasteIntoActivePane();
            e.Handled = true;
        }
        else if (IsShortcut("selectAll", e))
        {
            if (_settings.ViewMode == ViewMode.Icons)
            {
                IconViewOf(ActivePane).SelectAll();
            }
            else
            {
                _activeGrid.SelectAll();
            }
            e.Handled = true;
        }
        else if (IsShortcut("goBack", e))
        {
            NavigateBack();
            e.Handled = true;
        }
        else if (IsShortcut("goForward", e))
        {
            NavigateForward();
            e.Handled = true;
        }
        else if (IsShortcut("goUp", e))
        {
            NavigateParent();
            e.Handled = true;
        }
        else if (IsShortcut("openTerminal", e))
        {
            if (!InArchiveContext) OpenTerminal();
            e.Handled = true;
        }
        else if (IsShortcut("toggleHidden", e))
        {
            ToggleHidden();
            e.Handled = true;
        }
        else if (IsShortcut("toggleSplit", e))
        {
            ToggleSplit();
            e.Handled = true;
        }
        else if (IsShortcut("togglePreview", e))
        {
            TogglePreview();
            e.Handled = true;
        }
        else if (IsShortcut("swapPanes", e))
        {
            SwapPanes();
            e.Handled = true;
        }
        else if (IsShortcut("moveToTrash", e))
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
        else if (IsShortcut("rename", e) && _activeGrid.SelectedItem is FileItem renameItem && !renameItem.IsParent)
        {
            if (!InArchiveContext) StartRename(_activeGrid, renameItem);
            e.Handled = true;
        }
        else if (e.Key == Key.Back)
        {
            NavigateParent();
            e.Handled = true;
        }
        else if (e.Key == Key.Left || e.Key == Key.Right)
        {
            // Left / Right move focus between file panes only when focus is
            // already inside a pane. From the toolbar, folder tree, search
            // box, etc. these keys fall through to default behavior.
            var focused = Keyboard.FocusedElement as DependencyObject;
            var inLeftPane = IsInsidePane(focused, isLeft: true);
            var inRightPane = IsInsidePane(focused, isLeft: false);
            if (!inLeftPane && !inRightPane)
            {
                return;
            }
            if (e.Key == Key.Left && inRightPane)
            {
                FocusPane(Pane.Left);
                e.Handled = true;
            }
            else if (e.Key == Key.Right && inLeftPane && RightPaneColumn.Width.Value > 0)
            {
                FocusPane(Pane.Right);
                e.Handled = true;
            }
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

        // Always own Up / Down / PageUp / PageDown while focus is anywhere
        // inside the active pane (container, row, or cell). Relying on the
        // DataGrid / ListBox built-in handler races with focus settling after
        // a Tab switch — sometimes focus ends up on the container and the
        // built-in handler does nothing. Intercepting here makes navigation
        // deterministic regardless of where focus landed inside the pane.
        if (!inTextBox && e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown && IsFocusInActiveListing())
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
            return IsInside(focused, IconViewOf(ActivePane));
        }

        return IsInside(focused, _activeGrid);
    }

    private object? ActiveListingSelectedItem() =>
        _settings.ViewMode == ViewMode.Icons
            ? IconViewOf(ActivePane).SelectedItem
            : _activeGrid.SelectedItem;

    private bool HandleTabFocusCycle()
    {
        // Tab is intercepted only when focus is already in a file pane, and
        // only when split view is on. Other targets (folder tree, toolbar,
        // search box) fall through to the default WPF Tab traversal.
        if (RightPaneColumn.Width.Value <= 0)
        {
            return false;
        }

        var focused = Keyboard.FocusedElement as DependencyObject;
        var inLeft = IsInsidePane(focused, isLeft: true);
        var inRight = IsInsidePane(focused, isLeft: false);
        if (!inLeft && !inRight)
        {
            return false;
        }

        FocusPane(inLeft ? Pane.Right : Pane.Left);
        return true;
    }

    private bool IsInsidePane(DependencyObject? focused, bool isLeft)
    {
        if (focused is null)
        {
            return false;
        }
        var grid = isLeft ? LeftGrid : RightGrid;
        var iconView = isLeft ? LeftIconView : RightIconView;
        return IsInside(focused, grid) || IsInside(focused, iconView);
    }

    private void FocusPane(Pane pane)
    {
        var grid = GridOf(pane);
        var iconView = IconViewOf(pane);
        UpdateActivePane(grid);

        // Ensure something is selected so Up / Down have a starting point.
        if (_settings.ViewMode == ViewMode.Icons)
        {
            if (iconView.SelectedItem is null && iconView.Items.Count > 0)
            {
                iconView.SelectedIndex = 0;
            }
        }
        else
        {
            if (grid.SelectedItem is null && grid.Items.Count > 0)
            {
                grid.SelectedIndex = 0;
            }
        }

        var selected = _settings.ViewMode == ViewMode.Icons
            ? iconView.SelectedItem as FileItem
            : grid.SelectedItem as FileItem;
        if (selected is not null)
        {
            // Use the queued variant: the single-shot version sometimes fires
            // before the DataGrid row container is realized after a Tab focus
            // switch, leaving focus on the container instead of the row. The
            // queued version retries at Input / ContextIdle / ApplicationIdle
            // priorities so the row receives focus once it exists.
            FocusSelectedListingItem(grid, iconView, selected);
        }
        else
        {
            FocusElement(_settings.ViewMode == ViewMode.Icons ? iconView : grid);
        }
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
