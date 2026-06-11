using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Tfx;

public partial class MainWindow
{
    private void OpenWithDialog(string path)
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            ShellOpenWith.Show(hwnd, path);
        }
        catch (Exception ex)
        {
            SetStatus(Loc.F("Open with failed: {0}", ex.Message));
        }
    }


    private void Grid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid)
        {
            return;
        }

        var node = e.OriginalSource as DependencyObject;
        while (node != null && node is not DataGridRow && node is not DataGrid)
        {
            node = VisualTreeHelper.GetParent(node);
        }

        FileItem? rowItem = null;
        if (node is DataGridRow row && row.Item is FileItem item)
        {
            rowItem = item;
            if (!item.IsParent && !grid.SelectedItems.Contains(item))
            {
                grid.SelectedItems.Clear();
                grid.SelectedItems.Add(item);
            }
        }

        UpdateActivePane(grid);
        grid.ContextMenu = BuildGridContextMenu(grid);

        // Prime a possible right-button drag. The actual DoDragDrop call is
        // launched from Grid_PreviewMouseMove once the cursor crosses the
        // system drag threshold while the right button is held.
        _dragStart = e.GetPosition(this);
        _pendingFileDragItem = null;
        _pendingFileDragPaths = [];
        if (rowItem is { IsParent: false })
        {
            _pendingFileDragItem = rowItem;
            var selectedItems = SelectedItems(grid).Where(i => !i.IsParent).ToArray();
            _pendingFileDragPaths = selectedItems.Contains(rowItem)
                ? selectedItems.Select(i => i.FullPath).ToArray()
                : [rowItem.FullPath];
        }
    }

    private void Grid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        // After a right-button drag completes, suppress the context menu that
        // would otherwise pop from the eventual right-button-up event.
        if (_suppressNextContextMenu)
        {
            _suppressNextContextMenu = false;
            e.Handled = true;
            return;
        }

        if (sender is not DataGrid grid)
        {
            e.Handled = true;
            return;
        }

        grid.ContextMenu = BuildGridContextMenu(grid);
    }

    private ContextMenu BuildGridContextMenu(DataGrid grid)
    {
        var menu = new ContextMenu();
        var selection = ActiveSelectedItems().Where(i => !i.IsParent).ToArray();
        var hasSelection = selection.Length > 0;
        var oneSelected = selection.Length == 1;
        var hasZipSelection = selection.Any(i => !i.IsDirectory && System.IO.Path.GetExtension(i.FullPath).Equals(".zip", StringComparison.OrdinalIgnoreCase) && !ArchivePath.Contains(i.FullPath));
        // Paste handles files, or — when the clipboard holds no files — creates a
        // file from its content (CSV / image / text).
        var hasClipboard = Clipboard.ContainsFileDropList()
            || Clipboard.ContainsData(DataFormats.CommaSeparatedValue)
            || Clipboard.ContainsImage()
            || Clipboard.ContainsText();
        var inArchive = ArchivePath.Contains(GetCurrentPath(grid));
        var selectionHasArchive = selection.Any(s => ArchivePath.Contains(s.FullPath));
        var writableContext = !inArchive && !selectionHasArchive;

        var open = new MenuItem { Header = Loc.T("Open"), InputGestureText = ShortcutText("openItem"), IsEnabled = oneSelected };
        open.Click += (_, _) =>
        {
            if (grid.SelectedItem is FileItem item)
            {
                OpenItem(grid, item);
            }
        };
        menu.Items.Add(open);

        var openWithEnabled = oneSelected && !selection[0].IsDirectory;
        var openWith = new MenuItem { Header = Loc.T("Open with..."), IsEnabled = openWithEnabled };
        openWith.Click += (_, _) =>
        {
            if (selection.Length == 1 && !selection[0].IsDirectory)
            {
                OpenWithDialog(selection[0].FullPath);
            }
        };
        menu.Items.Add(openWith);

        var reveal = new MenuItem { Header = Loc.T("Reveal in Explorer") };
        reveal.Click += (_, _) => RevealInExplorer();
        menu.Items.Add(reveal);

        var pinTargetIsDir = oneSelected && selection[0].IsDirectory && !ArchivePath.Contains(selection[0].FullPath);
        var pinTargetPath = pinTargetIsDir ? selection[0].FullPath : null;
        var pinAlreadyPinned = pinTargetPath != null && _pinned.Contains(pinTargetPath);
        var pin = new MenuItem
        {
            Header = Loc.T(pinAlreadyPinned ? "Unpin" : "Pin"),
            IsEnabled = pinTargetIsDir,
        };
        pin.Click += (_, _) =>
        {
            if (pinTargetPath == null)
            {
                return;
            }
            if (pinAlreadyPinned)
            {
                UnpinPinnedFolder(pinTargetPath);
            }
            else
            {
                _pinned.Add(pinTargetPath);
                SetStatus(Loc.F("Pinned {0}", pinTargetPath));
            }
        };
        menu.Items.Add(pin);

        menu.Items.Add(new Separator());

        var cut = new MenuItem { Header = Loc.T("Cut"), InputGestureText = ShortcutText("cutItems"), IsEnabled = hasSelection && writableContext };
        cut.Click += (_, _) => CopySelection(true);
        menu.Items.Add(cut);

        var copy = new MenuItem { Header = Loc.T("Copy"), InputGestureText = ShortcutText("copyItems"), IsEnabled = hasSelection };
        copy.Click += (_, _) => CopySelection(false);
        menu.Items.Add(copy);

        var paste = new MenuItem { Header = Loc.T("Paste"), InputGestureText = ShortcutText("pasteItems"), IsEnabled = hasClipboard && !inArchive };
        paste.Click += (_, _) => PasteIntoActivePane();
        menu.Items.Add(paste);

        // "Paste special": create a file from a chosen clipboard format.
        if (!inArchive && BuildPasteSpecialMenu() is { } pasteSpecial)
        {
            menu.Items.Add(pasteSpecial);
        }

        var copyCurrentPath = new MenuItem { Header = Loc.T("Copy current path"), IsEnabled = oneSelected };
        copyCurrentPath.Click += (_, _) => CopySelectedPath(selection);
        menu.Items.Add(copyCurrentPath);

        menu.Items.Add(new Separator());

        var compress = new MenuItem { Header = Loc.T("Compress to Zip"), InputGestureText = ShortcutText("compressToZip"), IsEnabled = hasSelection && writableContext };
        compress.Click += (_, _) => CompressSelection();
        menu.Items.Add(compress);

        var extract = new MenuItem { Header = Loc.T("Extract Zip"), InputGestureText = ShortcutText("extractZip"), IsEnabled = hasZipSelection && writableContext };
        extract.Click += (_, _) => ExtractSelectedArchives();
        menu.Items.Add(extract);

        menu.Items.Add(new Separator());

        var newFolder = new MenuItem { Header = Loc.T("New Folder"), InputGestureText = ShortcutText("newFolder"), IsEnabled = !inArchive };
        newFolder.Click += (_, _) => NewFolder();
        menu.Items.Add(newFolder);

        var newFile = new MenuItem { Header = Loc.T("New File"), InputGestureText = ShortcutText("newFile"), IsEnabled = !inArchive };
        newFile.Click += (_, _) => NewFile();
        menu.Items.Add(newFile);

        var newTab = new MenuItem { Header = Loc.T("New Tab"), InputGestureText = ShortcutText("newTab") };
        newTab.Click += (_, _) => NewTabInActivePane();
        menu.Items.Add(newTab);

        var selectedDir = oneSelected && selection[0].IsDirectory ? selection[0] : null;
        if (selectedDir is not null && !inArchive)
        {
            var openInNewTab = new MenuItem { Header = Loc.T("Open in New Tab") };
            openInNewTab.Click += (_, _) => OpenNewTab(ActivePane, selectedDir.FullPath);
            menu.Items.Add(openInNewTab);
        }

        var openTerminal = new MenuItem { Header = Loc.T("Open Terminal here"), InputGestureText = ShortcutText("openTerminal"), IsEnabled = !inArchive };
        openTerminal.Click += (_, _) => OpenTerminal();
        menu.Items.Add(openTerminal);

        var toggleTerminalPane = new MenuItem { Header = Loc.T("Toggle terminal pane"), InputGestureText = ShortcutText("toggleTerminal") };
        toggleTerminalPane.Click += (_, _) => ToggleTerminalPane();
        menu.Items.Add(toggleTerminalPane);

        var terminalSettings = new MenuItem { Header = Loc.T("Terminal Settings...") };
        terminalSettings.Click += (_, _) => OpenTerminalSettings();
        menu.Items.Add(terminalSettings);

        AddUserCommandMenuItems(menu, selection);

        menu.Items.Add(new Separator());

        var rename = new MenuItem { Header = Loc.T("Rename"), InputGestureText = ShortcutText("rename"), IsEnabled = oneSelected && writableContext };
        rename.Click += (_, _) =>
        {
            if (grid.SelectedItem is FileItem item && !item.IsParent)
            {
                StartRename(grid, item);
            }
        };
        menu.Items.Add(rename);

        var trash = new MenuItem { Header = Loc.T("Move to Recycle Bin"), InputGestureText = ShortcutText("moveToTrash"), IsEnabled = hasSelection && writableContext };
        trash.Click += (_, _) => MoveSelectionToTrash();
        menu.Items.Add(trash);

        var perm = new MenuItem { Header = Loc.T("Delete permanently"), InputGestureText = "Shift+Del", IsEnabled = hasSelection && writableContext };
        perm.Click += (_, _) => DeletePermanently();
        menu.Items.Add(perm);

        return menu;
    }

    /// <summary>
    /// Builds the "Paste special" submenu listing each clipboard format that can be
    /// turned into a file (text / rich text / HTML / image / CSV / URL). Returns
    /// null when the clipboard holds nothing pasteable as a file.
    /// </summary>
    private MenuItem? BuildPasteSpecialMenu()
    {
        var root = new MenuItem { Header = Loc.T("Paste special") };

        if (ClipboardHasUrl())
        {
            root.Items.Add(MakePasteItem(Loc.T("As URL (.url)"), PasteAsUrl));
        }
        if (ClipboardHasCsv())
        {
            root.Items.Add(MakePasteItem(Loc.T("As CSV (.csv)"), PasteAsCsv));
        }
        if (ClipboardHasImage())
        {
            root.Items.Add(MakePasteItem(Loc.T("As image (.png)"), PasteAsImage));
        }
        if (Clipboard.ContainsData(DataFormats.Html))
        {
            root.Items.Add(MakePasteItem(Loc.T("As HTML (.html)"), PasteAsHtml));
        }
        if (Clipboard.ContainsData(DataFormats.Rtf))
        {
            root.Items.Add(MakePasteItem(Loc.T("As rich text (.rtf)"), PasteAsRichText));
        }
        if (Clipboard.ContainsText())
        {
            root.Items.Add(MakePasteItem(Loc.T("As text (.txt)"), PasteAsPlainText));
        }

        return root.Items.Count > 0 ? root : null;
    }

    private static MenuItem MakePasteItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>
    /// Adds a "Commands" section for any user-defined <c>[[commands]]</c> entries
    /// whose filters match the current selection. Each runs as an external
    /// process (fire-and-forget). Nothing is added when no command matches.
    /// </summary>
    /// <summary>
    /// The <c>scripts</c> folder next to <c>config.toml</c> (created on demand).
    /// Used to expand the <c>{scripts}</c> token in user-defined commands so they
    /// can ship scripts alongside the config without hard-coding an absolute path.
    /// </summary>
    private string ScriptsDirectory()
    {
        var dir = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(_configPath) ?? ".", "scripts");
        try { System.IO.Directory.CreateDirectory(dir); } catch { }
        return dir;
    }

    private void AddUserCommandMenuItems(ContextMenu menu, IReadOnlyList<FileItem> selection)
    {
        if (_config.Commands.Count == 0)
        {
            return;
        }

        var isGitRepo = GetGitRoot(ActivePane) is not null;
        var matching = _config.Commands.Where(c => CommandRunner.Matches(c, selection, isGitRepo)).ToList();
        if (matching.Count == 0)
        {
            return;
        }

        menu.Items.Add(new Separator());
        foreach (var command in matching)
        {
            var item = new MenuItem
            {
                Header = command.Name,
                InputGestureText = command.Shortcut?.DisplayText ?? "",
            };
            var captured = command;
            item.Click += (_, _) => ExecuteUserCommand(captured, selection);
            menu.Items.Add(item);
        }
    }

    /// <summary>
    /// Runs a user-defined command against the given selection: either captured
    /// into the terminal Output tab (<c>terminal = true</c>) or launched as an
    /// external process. Shared by the context menu and keyboard shortcuts.
    /// </summary>
    private void ExecuteUserCommand(UserCommand command, IReadOnlyList<FileItem> selection)
    {
        var cwd = GetCurrentPath(_activeGrid);
        if (command.Terminal)
        {
            RunCommandInTerminal(command, selection, cwd);
            return;
        }
        if (!CommandRunner.Run(command, selection, cwd, ScriptsDirectory(), ResolveTerminalShell(), out var error))
        {
            SetStatus(Loc.F("Command failed: {0}", error ?? command.Name));
        }
    }
}
