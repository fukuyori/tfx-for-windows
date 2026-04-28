using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Path = System.IO.Path;

namespace Tfx;

public partial class MainWindow
{
    private void LoadDrives()
    {
        FolderTree.Items.Clear();
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
        {
            var item = CreateFolderNode(drive.RootDirectory.FullName);
            FolderTree.Items.Add(item);
        }
    }

    private TreeViewItem CreateFolderNode(string path)
    {
        var item = new TreeViewItem
        {
            Header = FormatTreeHeader(path),
            Tag = path
        };

        if (HasSubdirectories(path))
        {
            item.Items.Add(null);
        }

        return item;
    }

    private static string FormatTreeHeader(string path)
    {
        var root = Path.GetPathRoot(path);
        if (string.Equals(root, path, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        return Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }

    private static bool HasSubdirectories(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path).Any();
        }
        catch
        {
            return false;
        }
    }

    private void FolderTree_Expanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not TreeViewItem item || item.Tag is not string path)
        {
            return;
        }

        if (item.Items.Count == 1 && item.Items[0] is null)
        {
            item.Items.Clear();
            foreach (var directory in FsHelpers.SafeEnumerateDirectories(path).OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase))
            {
                item.Items.Add(CreateFolderNode(directory));
            }
        }
    }

    private void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (FolderTree.SelectedItem is TreeViewItem item && item.Tag is string path && Directory.Exists(path))
        {
            Navigate(_activeGrid, path, true);
        }
    }

    private void FolderTree_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var node = e.OriginalSource as DependencyObject;
        while (node != null && node is not TreeView)
        {
            if (node is System.Windows.Controls.Primitives.ToggleButton)
            {
                return;
            }

            if (node is TreeViewItem item && item.Tag is string path && Directory.Exists(path))
            {
                if (!string.Equals(GetCurrentPath(_activeGrid), path, StringComparison.OrdinalIgnoreCase))
                {
                    Navigate(_activeGrid, path, true);
                }
                return;
            }

            node = VisualTreeHelper.GetParent(node);
        }
    }
}
