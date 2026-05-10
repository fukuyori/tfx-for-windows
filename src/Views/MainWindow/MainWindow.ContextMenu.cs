using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Tfx;

public partial class MainWindow
{
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

        if (node is DataGridRow row && row.Item is FileItem item && !item.IsParent)
        {
            if (!grid.SelectedItems.Contains(item))
            {
                grid.SelectedItems.Clear();
                grid.SelectedItems.Add(item);
            }
        }

        UpdateActivePane(grid);
    }

    private void Grid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
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
        var hasZipSelection = selection.Any(i => !i.IsDirectory && System.IO.Path.GetExtension(i.FullPath).Equals(".zip", StringComparison.OrdinalIgnoreCase));
        var hasClipboard = Clipboard.ContainsFileDropList();

        var open = new MenuItem { Header = Loc.T("Open"), InputGestureText = "Enter", IsEnabled = oneSelected };
        open.Click += (_, _) =>
        {
            if (grid.SelectedItem is FileItem item)
            {
                OpenItem(grid, item);
            }
        };
        menu.Items.Add(open);

        var reveal = new MenuItem { Header = Loc.T("Reveal in Explorer") };
        reveal.Click += (_, _) => RevealInExplorer();
        menu.Items.Add(reveal);

        menu.Items.Add(new Separator());

        var cut = new MenuItem { Header = Loc.T("Cut"), InputGestureText = "Ctrl+X", IsEnabled = hasSelection };
        cut.Click += (_, _) => CopySelection(true);
        menu.Items.Add(cut);

        var copy = new MenuItem { Header = Loc.T("Copy"), InputGestureText = "Ctrl+C", IsEnabled = hasSelection };
        copy.Click += (_, _) => CopySelection(false);
        menu.Items.Add(copy);

        var paste = new MenuItem { Header = Loc.T("Paste"), InputGestureText = "Ctrl+V", IsEnabled = hasClipboard };
        paste.Click += (_, _) => PasteIntoActivePane();
        menu.Items.Add(paste);

        var compress = new MenuItem { Header = Loc.T("Compress to Zip"), InputGestureText = "Ctrl+K", IsEnabled = hasSelection };
        compress.Click += (_, _) => CompressSelection();
        menu.Items.Add(compress);

        var extract = new MenuItem { Header = Loc.T("Extract Zip"), InputGestureText = "Ctrl+Shift+E", IsEnabled = hasZipSelection };
        extract.Click += (_, _) => ExtractSelectedArchives();
        menu.Items.Add(extract);

        menu.Items.Add(new Separator());

        var rename = new MenuItem { Header = Loc.T("Rename"), InputGestureText = "F2", IsEnabled = oneSelected };
        rename.Click += (_, _) =>
        {
            if (grid.SelectedItem is FileItem item && !item.IsParent)
            {
                StartRename(grid, item);
            }
        };
        menu.Items.Add(rename);

        var trash = new MenuItem { Header = Loc.T("Move to Recycle Bin"), InputGestureText = "Del", IsEnabled = hasSelection };
        trash.Click += (_, _) => MoveSelectionToTrash();
        menu.Items.Add(trash);

        var perm = new MenuItem { Header = Loc.T("Delete permanently"), InputGestureText = "Shift+Del", IsEnabled = hasSelection };
        perm.Click += (_, _) => DeletePermanently();
        menu.Items.Add(perm);

        menu.Items.Add(new Separator());

        var newFolder = new MenuItem { Header = Loc.T("New Folder"), InputGestureText = "Ctrl+N" };
        newFolder.Click += (_, _) => NewFolder();
        menu.Items.Add(newFolder);

        var newFile = new MenuItem { Header = Loc.T("New File"), InputGestureText = "Ctrl+Shift+N" };
        newFile.Click += (_, _) => NewFile();
        menu.Items.Add(newFile);

        var openTerminal = new MenuItem { Header = Loc.T("Open Terminal here"), InputGestureText = "Ctrl+Shift+T" };
        openTerminal.Click += (_, _) => OpenTerminal();
        menu.Items.Add(openTerminal);

        return menu;
    }
}
