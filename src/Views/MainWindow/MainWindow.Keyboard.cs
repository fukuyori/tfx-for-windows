using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Tfx;

public partial class MainWindow
{
    private static readonly Dictionary<string, string> DefaultShortcutText = new(StringComparer.OrdinalIgnoreCase)
    {
        ["reload"] = "f5",
        ["openTerminal"] = "ctrl+shift+t",
        ["togglePreview"] = "ctrl+shift+p",
        ["toggleFolderTree"] = "ctrl+b",
        ["collapseFolders"] = "ctrl+shift+b",
        ["toggleRendered"] = "ctrl+shift+r",
        ["loadExternalImages"] = "ctrl+shift+i",
        ["toggleSplit"] = "ctrl+backslash",
        ["swapPanes"] = "ctrl+shift+x",
        ["focusSearch"] = "ctrl+f",
        ["focusFilePane"] = "ctrl+1",
        ["focusTerminal"] = "ctrl+2",
        ["toggleHidden"] = "ctrl+shift+.",
        ["goBack"] = "alt+left",
        ["goForward"] = "alt+right",
        ["goUp"] = "alt+up",
        ["openItem"] = "enter",
        ["newFolder"] = "ctrl+shift+n",
        ["newFile"] = "ctrl+n",
        ["rename"] = "f2",
        ["moveToTrash"] = "delete",
        ["compressToZip"] = "ctrl+k",
        ["extractZip"] = "ctrl+shift+e",
        ["copyItems"] = "ctrl+c",
        ["cutItems"] = "ctrl+x",
        ["pasteItems"] = "ctrl+v",
        ["pasteAsText"] = "ctrl+shift+v",
        ["selectAll"] = "ctrl+a",
        ["newTab"] = "ctrl+t",
        ["closeTab"] = "ctrl+w",
        ["nextTab"] = "ctrl+shift+]",
        ["prevTab"] = "ctrl+shift+[",
        ["toggleTerminal"] = "ctrl+j",
        ["quit"] = "ctrl+q",
    };

    private bool InArchiveContext => ArchivePath.Contains(GetCurrentPath(_activeGrid));

    private bool IsShortcut(string action, KeyEventArgs e) =>
        _shortcuts.TryGetValue(action, out var shortcut) && shortcut.Matches(e);

    private string ShortcutText(string action) =>
        _shortcuts.TryGetValue(action, out var shortcut) ? shortcut.DisplayText : "";

    /// <summary>
    /// True while keyboard focus is inside the built-in terminal pane. The
    /// terminal owns all keystrokes (they're written to the shell), so the
    /// window-level shortcut handlers must not intercept anything — except the
    /// terminal-toggle shortcut itself, which still needs to close the pane.
    /// </summary>
    private bool IsFocusInTerminal()
    {
        var focused = Keyboard.FocusedElement as DependencyObject;
        return IsInside(focused, Terminal);
    }

    /// <summary>
    /// Runs the first user-defined command whose shortcut matches and whose
    /// filters (extension / target / selection / git) match the current context.
    /// Returns true if one was run.
    /// </summary>
    private bool TryRunCommandShortcut(KeyEventArgs e)
    {
        if (_config.Commands.Count == 0)
        {
            return false;
        }

        var selection = ActiveSelectedItems().Where(i => !i.IsParent).ToList();
        var isGitRepo = GetGitRoot(ActivePane) is not null;
        foreach (var command in _config.Commands)
        {
            if (command.Shortcut is { } sc && sc.Matches(e) &&
                CommandRunner.Matches(command, selection, isGitRepo))
            {
                ExecuteUserCommand(command, selection);
                return true;
            }
        }
        return false;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (IsFocusInTerminal())
        {
            if (IsShortcut("toggleTerminal", e))
            {
                ToggleTerminalPane();
                e.Handled = true;
            }
            else if (IsShortcut("focusFilePane", e))
            {
                // Move focus back to the file list. Primary path while the
                // terminal is focused is the xterm page forwarding a "focusFiles"
                // message (WebView2 owns most keys); this is a WPF fallback.
                FocusActiveFilePane();
                e.Handled = true;
            }
            return;
        }

        // User-defined command shortcuts take precedence so they can be bound
        // freely. Only fire when the current context matches the command's
        // filters (same check as the context menu).
        if (TryRunCommandShortcut(e))
        {
            e.Handled = true;
            return;
        }

        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        if (IsShortcut("focusSearch", e))
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (IsShortcut("focusFilePane", e))
        {
            FocusActiveFilePane();
            e.Handled = true;
        }
        else if (IsShortcut("focusTerminal", e))
        {
            FocusTerminalPane();
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
        else if (IsShortcut("toggleFolderTree", e))
        {
            ToggleFolderTree();
            e.Handled = true;
        }
        else if (IsShortcut("collapseFolders", e))
        {
            CollapseAllFolders();
            e.Handled = true;
        }
        else if (IsShortcut("toggleRendered", e) && RenderedToggle.IsVisible)
        {
            // Flip the toggle, then run its handler (which reads IsChecked).
            RenderedToggle.IsChecked = RenderedToggle.IsChecked != true;
            RenderedToggle_Click(RenderedToggle, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (IsShortcut("loadExternalImages", e) && LoadImagesButton.IsVisible)
        {
            LoadImages_Click(LoadImagesButton, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (IsShortcut("swapPanes", e))
        {
            SwapPanes();
            e.Handled = true;
        }
        else if (IsShortcut("newTab", e))
        {
            NewTabInActivePane();
            e.Handled = true;
        }
        else if (IsShortcut("closeTab", e))
        {
            CloseActiveTab();
            e.Handled = true;
        }
        else if (IsShortcut("nextTab", e))
        {
            CycleTab(1);
            e.Handled = true;
        }
        else if (IsShortcut("prevTab", e))
        {
            CycleTab(-1);
            e.Handled = true;
        }
        else if (IsShortcut("toggleTerminal", e))
        {
            ToggleTerminalPane();
            e.Handled = true;
        }
        else if (IsShortcut("quit", e))
        {
            // Closing the window runs Window_Closing (saves session, tears down
            // the terminal). Skipped while the terminal is focused so the shell
            // keeps Ctrl+Q (XON/XOFF flow control).
            Close();
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
        // When the terminal has focus it consumes every key itself (forwarded
        // to the shell). Don't let the window-level navigation / selection
        // shortcuts steal Enter, arrows, Tab, etc.
        if (IsFocusInTerminal())
        {
            return;
        }

        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var inTextBox = Keyboard.FocusedElement is TextBox;

        // Clipboard copy / cut / paste for the file list, handled in the tunneling
        // pass so the DataGrid's built-in Ctrl+C (and any other control handling)
        // can't intercept them before the bubbling Window_KeyDown runs.
        if (!inTextBox && IsFocusInActiveListing())
        {
            if (IsShortcut("copyItems", e))
            {
                CopySelection(false);
                e.Handled = true;
                return;
            }
            if (IsShortcut("cutItems", e))
            {
                if (!InArchiveContext) CopySelection(true);
                e.Handled = true;
                return;
            }
            if (IsShortcut("pasteItems", e))
            {
                if (!InArchiveContext) PasteIntoActivePane();
                e.Handled = true;
                return;
            }
            if (IsShortcut("pasteAsText", e))
            {
                if (!InArchiveContext) PasteAsPlainText();
                e.Handled = true;
                return;
            }
        }

        // Alt+Left / Alt+Right / Alt+Up navigation. WPF delivers Alt combos as
        // Key.System (real key in SystemKey), and the DataGrid / ListBox can
        // consume arrow keys via bubbling before the bubbling Window_KeyDown
        // ever runs. Handling them here in the tunneling preview pass — which
        // fires on the Window before any pane control — makes them reliable.
        if (!inTextBox && e.Key == Key.System && e.SystemKey is Key.Left or Key.Right or Key.Up)
        {
            if (IsShortcut("goBack", e))
            {
                NavigateBack();
                e.Handled = true;
                return;
            }
            if (IsShortcut("goForward", e))
            {
                NavigateForward();
                e.Handled = true;
                return;
            }
            if (IsShortcut("goUp", e))
            {
                NavigateParent();
                e.Handled = true;
                return;
            }
        }

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
        // Shift extends the selection (range); otherwise it is a single move.
        // Icon mode also owns Left / Right (they move within the row there;
        // in details mode they keep switching panes via Window_KeyDown).
        var listingNavKey = e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown
            || (_settings.ViewMode == ViewMode.Icons && e.Key is Key.Left or Key.Right);
        if (!inTextBox && listingNavKey
            && (IsFocusInActiveListing() || ShouldStartListingNavigation()))
        {
            var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            MoveActiveListingSelection(e.Key, shift);
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

    /// <summary>
    /// True when an Up / Down press should "adopt" the active listing even though
    /// focus isn't inside it — specifically when nothing is selected there and
    /// focus is somewhere neutral (not the folder tree, search box, terminal, or
    /// the other pane). This makes Down land on the ".." row when the listing has
    /// no current selection.
    /// </summary>
    private bool ShouldStartListingNavigation()
    {
        if (ActiveListingSelectedItem() is not null)
        {
            return false;
        }

        var focused = Keyboard.FocusedElement as DependencyObject;
        // Don't steal arrows from the folder tree or the inactive pane's listing.
        if (IsInside(focused, FolderTree)
            || IsInside(focused, GridOf(ActivePane == Pane.Left ? Pane.Right : Pane.Left))
            || IsInside(focused, IconViewOf(ActivePane == Pane.Left ? Pane.Right : Pane.Left)))
        {
            return false;
        }
        return true;
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

    /// <summary>Moves keyboard focus to the active file listing.</summary>
    private void FocusActiveFilePane() => FocusPane(ActivePane);

    /// <summary>
    /// Moves keyboard focus into the built-in terminal pane. Opens the pane first
    /// if it is hidden (which also focuses it).
    /// </summary>
    private void FocusTerminalPane()
    {
        if (TerminalHost.Visibility == Visibility.Visible)
        {
            Terminal.Focus();
            PostToTerminal(new { type = "focus" });
        }
        else
        {
            // Hidden → open it; SetTerminalVisible(true) focuses the shell.
            ToggleTerminalPane();
        }
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
