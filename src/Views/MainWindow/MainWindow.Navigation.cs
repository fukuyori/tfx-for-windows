using System.IO;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using Path = System.IO.Path;

namespace Tfx;

public partial class MainWindow
{
    private void Navigate(DataGrid grid, string path, bool pushHistory)
    {
        if (!Directory.Exists(path))
        {
            SetStatus($"Folder not found: {path}");
            return;
        }

        var current = GetCurrentPath(grid);
        if (pushHistory && Directory.Exists(current) && !string.Equals(current, path, StringComparison.OrdinalIgnoreCase))
        {
            _back.Add(current);
            _forward.Clear();
        }

        if (grid == LeftGrid)
        {
            _leftPath = path;
        }
        else
        {
            _rightPath = path;
        }

        Reload(grid);
        UpdatePathText();
        SaveSettings();
    }

    private void Reload(DataGrid grid)
    {
        var path = GetCurrentPath(grid);
        var target = grid == LeftGrid ? LeftItems : RightItems;
        target.Clear();

        var parent = Directory.GetParent(path);
        if (parent is not null)
        {
            target.Add(FileItem.Parent(parent.FullName));
        }

        foreach (var directory in FsHelpers.SafeEnumerateDirectories(path).OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase))
        {
            if (!ShowHidden && FsHelpers.IsHidden(directory))
            {
                continue;
            }

            target.Add(FileItem.FromDirectory(directory));
        }

        foreach (var file in FsHelpers.SafeEnumerateFiles(path).OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase))
        {
            if (!ShowHidden && FsHelpers.IsHidden(file))
            {
                continue;
            }

            target.Add(FileItem.FromFile(file));
        }

        ApplySearchFilter();
        UpdateStatus();
    }

    private void NavigateParent()
    {
        var parent = Directory.GetParent(GetCurrentPath(_activeGrid));
        if (parent is not null)
        {
            Navigate(_activeGrid, parent.FullName, true);
        }
    }

    private void NavigateBack()
    {
        if (_back.Count == 0)
        {
            return;
        }

        var current = GetCurrentPath(_activeGrid);
        var path = _back[^1];
        _back.RemoveAt(_back.Count - 1);
        _forward.Add(current);
        Navigate(_activeGrid, path, false);
    }

    private void NavigateForward()
    {
        if (_forward.Count == 0)
        {
            return;
        }

        var current = GetCurrentPath(_activeGrid);
        var path = _forward[^1];
        _forward.RemoveAt(_forward.Count - 1);
        _back.Add(current);
        Navigate(_activeGrid, path, false);
    }

    private void OpenFolderPicker()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Open folder",
            InitialDirectory = GetCurrentPath(_activeGrid)
        };

        if (dialog.ShowDialog(this) == true && Directory.Exists(dialog.FolderName))
        {
            Navigate(_activeGrid, dialog.FolderName, true);
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e) => NavigateBack();

    private void Forward_Click(object sender, RoutedEventArgs e) => NavigateForward();

    private void Parent_Click(object sender, RoutedEventArgs e) => NavigateParent();

    private void OpenFolder_Click(object sender, RoutedEventArgs e) => OpenFolderPicker();

    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        Reload(LeftGrid);
        Reload(RightGrid);
    }
}
