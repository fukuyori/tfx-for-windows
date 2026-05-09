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

        foreach (var folder in _settings.PinnedFolders.Where(Directory.Exists))
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

    private void PinnedList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
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

    private void PinnedList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(string)) is not string path)
        {
            return;
        }

        var point = e.GetPosition(PinnedList);
        var index = _pinned.Count;
        for (var i = 0; i < PinnedList.Items.Count; i++)
        {
            if (PinnedList.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem item)
            {
                var bounds = VisualTreeHelper.GetDescendantBounds(item);
                var topLeft = item.TranslatePoint(new Point(), PinnedList);
                if (point.Y < topLeft.Y + bounds.Height / 2)
                {
                    index = i;
                    break;
                }
            }
        }

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

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        var path = GetCurrentPath(_activeGrid);
        if (!_pinned.Contains(path))
        {
            _pinned.Add(path);
        }
    }

    private void TogglePin_Click(object sender, RoutedEventArgs e)
    {
        var path = GetCurrentPath(_activeGrid);
        if (_pinned.Contains(path))
        {
            _pinned.Remove(path);
            SetStatus($"Unpinned {path}");
        }
        else
        {
            _pinned.Add(path);
            SetStatus($"Pinned {path}");
        }
    }

    private void Unpin_Click(object sender, RoutedEventArgs e)
    {
        if (PinnedList.SelectedItem is string path)
        {
            _pinned.Remove(path);
        }
    }
}
