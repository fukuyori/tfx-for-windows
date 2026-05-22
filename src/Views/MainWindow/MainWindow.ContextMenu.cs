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
        var hasClipboard = Clipboard.ContainsFileDropList();
        var inArchive = ArchivePath.Contains(GetCurrentPath(grid));
        var selectionHasArchive = selection.Any(s => ArchivePath.Contains(s.FullPath));
        var writableContext = !inArchive && !selectionHasArchive;

        var open = new MenuItem { Header = Loc.T("Open"), InputGestureText = "Enter", IsEnabled = oneSelected };
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

        var cut = new MenuItem { Header = Loc.T("Cut"), InputGestureText = "Ctrl+X", IsEnabled = hasSelection && writableContext };
        cut.Click += (_, _) => CopySelection(true);
        menu.Items.Add(cut);

        var copy = new MenuItem { Header = Loc.T("Copy"), InputGestureText = "Ctrl+C", IsEnabled = hasSelection };
        copy.Click += (_, _) => CopySelection(false);
        menu.Items.Add(copy);

        var paste = new MenuItem { Header = Loc.T("Paste"), InputGestureText = "Ctrl+V", IsEnabled = hasClipboard && !inArchive };
        paste.Click += (_, _) => PasteIntoActivePane();
        menu.Items.Add(paste);

        var copyCurrentPath = new MenuItem { Header = Loc.T("Copy current path"), IsEnabled = oneSelected };
        copyCurrentPath.Click += (_, _) => CopySelectedPath(selection);
        menu.Items.Add(copyCurrentPath);

        menu.Items.Add(new Separator());

        var compress = new MenuItem { Header = Loc.T("Compress to Zip"), InputGestureText = "Ctrl+K", IsEnabled = hasSelection && writableContext };
        compress.Click += (_, _) => CompressSelection();
        menu.Items.Add(compress);

        var extract = new MenuItem { Header = Loc.T("Extract Zip"), InputGestureText = "Ctrl+Shift+E", IsEnabled = hasZipSelection && writableContext };
        extract.Click += (_, _) => ExtractSelectedArchives();
        menu.Items.Add(extract);

        menu.Items.Add(new Separator());

        var newFolder = new MenuItem { Header = Loc.T("New Folder"), InputGestureText = "Ctrl+N", IsEnabled = !inArchive };
        newFolder.Click += (_, _) => NewFolder();
        menu.Items.Add(newFolder);

        var newFile = new MenuItem { Header = Loc.T("New File"), InputGestureText = "Ctrl+Shift+N", IsEnabled = !inArchive };
        newFile.Click += (_, _) => NewFile();
        menu.Items.Add(newFile);

        var openTerminal = new MenuItem { Header = Loc.T("Open Terminal here"), InputGestureText = "Ctrl+Shift+T", IsEnabled = !inArchive };
        openTerminal.Click += (_, _) => OpenTerminal();
        menu.Items.Add(openTerminal);

        var terminalSettings = new MenuItem { Header = Loc.T("Terminal Settings...") };
        terminalSettings.Click += (_, _) => OpenTerminalSettings();
        menu.Items.Add(terminalSettings);

        menu.Items.Add(new Separator());

        var rename = new MenuItem { Header = Loc.T("Rename"), InputGestureText = "F2", IsEnabled = oneSelected && writableContext };
        rename.Click += (_, _) =>
        {
            if (grid.SelectedItem is FileItem item && !item.IsParent)
            {
                StartRename(grid, item);
            }
        };
        menu.Items.Add(rename);

        var trash = new MenuItem { Header = Loc.T("Move to Recycle Bin"), InputGestureText = "Del", IsEnabled = hasSelection && writableContext };
        trash.Click += (_, _) => MoveSelectionToTrash();
        menu.Items.Add(trash);

        var perm = new MenuItem { Header = Loc.T("Delete permanently"), InputGestureText = "Shift+Del", IsEnabled = hasSelection && writableContext };
        perm.Click += (_, _) => DeletePermanently();
        menu.Items.Add(perm);

        return menu;
    }
}
