using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Path = System.IO.Path;

namespace Tfx;

public partial class MainWindow
{
    private void LoadPinned()
    {
        _pinned.Clear();

        // Load saved pins as-is — do not call Directory.Exists on the UI thread.
        // Network pins that are temporarily offline must still show up so the
        // user can see (and unpin) them; clicking a stale pin fails through the
        // normal Navigate error path.
        foreach (var folder in _settings.PinnedFolders)
        {
            _pinned.Add(folder);
        }

        if (_pinned.Count == 0)
        {
            AddIfExists(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            AddIfExists(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
            AddIfExists(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            AddIfExists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
        }

        PinnedList.ItemsSource = _pinned;
        _pinned.CollectionChanged += Pinned_CollectionChanged;
    }

    private void AddIfExists(string path)
    {
        if (Directory.Exists(path) && !_pinned.Contains(path))
        {
            _pinned.Add(path);
        }
    }

    private void Pinned_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SaveSettings();
    }

    private void PinnedList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PinnedList.SelectedItem is string path && Directory.Exists(path))
        {
            Navigate(_activeGrid, path, true);
        }
    }

    private void PinnedList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && PinnedList.SelectedItem is string path)
        {
            DragDrop.DoDragDrop(PinnedList, path, DragDropEffects.Move);
        }
    }

    private void PinnedList_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(string)) is string)
        {
            e.Effects = DragDropEffects.Move;
        }
        else if (GetPinnableDirectories(e.Data).Length > 0)
        {
            e.Effects = DragDropEffects.Link;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void PinnedList_Drop(object sender, DragEventArgs e)
    {
        var index = ComputePinnedDropIndex(e.GetPosition(PinnedList));

        if (e.Data.GetData(typeof(string)) is string reorderPath)
        {
            MovePinnedTo(reorderPath, ref index);
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            return;
        }

        var directories = GetPinnableDirectories(e.Data);
        if (directories.Length == 0)
        {
            return;
        }

        string? lastAdded = null;
        var addedCount = 0;
        foreach (var dir in directories)
        {
            if (!_pinned.Contains(dir))
            {
                addedCount++;
                lastAdded = dir;
            }
            MovePinnedTo(dir, ref index);
            index++;
        }

        if (addedCount == 1 && lastAdded != null)
        {
            SetStatus(Loc.F("Pinned {0}", lastAdded));
        }

        e.Effects = DragDropEffects.Link;
        e.Handled = true;
    }

    private static string[] GetPinnableDirectories(IDataObject data)
    {
        if (!data.GetDataPresent(DataFormats.FileDrop))
        {
            return [];
        }
        if (data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return [];
        }
        return paths.Where(Directory.Exists).ToArray();
    }

    private int ComputePinnedDropIndex(Point point)
    {
        for (var i = 0; i < PinnedList.Items.Count; i++)
        {
            if (PinnedList.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem item)
            {
                var bounds = VisualTreeHelper.GetDescendantBounds(item);
                var topLeft = item.TranslatePoint(new Point(), PinnedList);
                if (point.Y < topLeft.Y + bounds.Height / 2)
                {
                    return i;
                }
            }
        }
        return _pinned.Count;
    }

    private void MovePinnedTo(string path, ref int index)
    {
        var oldIndex = _pinned.IndexOf(path);
        if (oldIndex >= 0)
        {
            _pinned.RemoveAt(oldIndex);
            if (oldIndex < index)
            {
                index--;
            }
        }
        _pinned.Insert(Math.Clamp(index, 0, _pinned.Count), path);
    }

    private void PinnedList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var node = e.OriginalSource as DependencyObject;
        while (node != null && node is not ListBoxItem && node is not ListBox)
        {
            node = VisualTreeHelper.GetParent(node);
        }

        if (node is ListBoxItem item && item.Content is string path)
        {
            PinnedList.SelectedItem = path;
        }
    }

    private void PinnedList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (PinnedList.SelectedItem is not string path)
        {
            e.Handled = true;
            return;
        }

        var menu = new ContextMenu();
        var unpin = new MenuItem { Header = Loc.T("Unpin") };
        unpin.Click += (_, _) => UnpinPinnedFolder(path);
        menu.Items.Add(unpin);
        PinnedList.ContextMenu = menu;
    }

    private void TogglePin_Click(object sender, RoutedEventArgs e)
    {
        var path = GetCurrentPath(_activeGrid);
        if (ArchivePath.Contains(path))
        {
            return;
        }
        if (_pinned.Contains(path))
        {
            UnpinPinnedFolder(path);
        }
        else
        {
            _pinned.Add(path);
            SetStatus(Loc.F("Pinned {0}", path));
        }
    }

    private void UnpinPinnedFolder(string path)
    {
        if (_pinned.Remove(path))
        {
            SetStatus(Loc.F("Unpinned {0}", path));
        }
    }
}
