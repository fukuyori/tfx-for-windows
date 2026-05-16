using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Tfx;

public partial class MainWindow
{
    private static readonly TimeSpan AutoRefreshDebounce = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan AutoRefreshPeriodic = TimeSpan.FromSeconds(10);

    private FileSystemWatcher? _leftWatcher;
    private FileSystemWatcher? _rightWatcher;
    private DispatcherTimer? _leftDebounceTimer;
    private DispatcherTimer? _rightDebounceTimer;
    private DispatcherTimer? _periodicTimer;

    private void InitializeAutoRefresh()
    {
        _leftDebounceTimer = new DispatcherTimer { Interval = AutoRefreshDebounce };
        _leftDebounceTimer.Tick += (_, _) =>
        {
            _leftDebounceTimer!.Stop();
            _ = ReloadDiffAsync(LeftGrid);
        };

        _rightDebounceTimer = new DispatcherTimer { Interval = AutoRefreshDebounce };
        _rightDebounceTimer.Tick += (_, _) =>
        {
            _rightDebounceTimer!.Stop();
            _ = ReloadDiffAsync(RightGrid);
        };

        _periodicTimer = new DispatcherTimer { Interval = AutoRefreshPeriodic };
        _periodicTimer.Tick += (_, _) =>
        {
            _ = ReloadDiffAsync(LeftGrid);
            if (RightPaneColumn.Width.Value > 0)
            {
                _ = ReloadDiffAsync(RightGrid);
            }
        };
        _periodicTimer.Start();
    }

    private void DisposeAutoRefresh()
    {
        _periodicTimer?.Stop();
        _leftDebounceTimer?.Stop();
        _rightDebounceTimer?.Stop();
        _leftWatcher?.Dispose();
        _rightWatcher?.Dispose();
    }

    private void UpdateWatcherForPane(bool isLeft)
    {
        var existing = isLeft ? _leftWatcher : _rightWatcher;
        existing?.Dispose();
        if (isLeft)
        {
            _leftWatcher = null;
        }
        else
        {
            _rightWatcher = null;
        }

        var path = isLeft ? _leftPath : _rightPath;
        if (string.IsNullOrEmpty(path) || ArchivePath.Contains(path) || !Directory.Exists(path))
        {
            return;
        }

        FileSystemWatcher watcher;
        try
        {
            watcher = new FileSystemWatcher(path)
            {
                NotifyFilter = NotifyFilters.FileName
                    | NotifyFilters.DirectoryName
                    | NotifyFilters.LastWrite
                    | NotifyFilters.Size
                    | NotifyFilters.Attributes,
                IncludeSubdirectories = false,
            };
        }
        catch
        {
            return;
        }

        FileSystemEventHandler change = (_, _) => Dispatcher.BeginInvoke(() => KickDebounce(isLeft));
        RenamedEventHandler rename = (_, _) => Dispatcher.BeginInvoke(() => KickDebounce(isLeft));
        ErrorEventHandler error = (_, _) => Dispatcher.BeginInvoke(() => UpdateWatcherForPane(isLeft));

        watcher.Changed += change;
        watcher.Created += change;
        watcher.Deleted += change;
        watcher.Renamed += rename;
        watcher.Error += error;

        try
        {
            watcher.EnableRaisingEvents = true;
        }
        catch
        {
            watcher.Dispose();
            return;
        }

        if (isLeft)
        {
            _leftWatcher = watcher;
        }
        else
        {
            _rightWatcher = watcher;
        }
    }

    private void KickDebounce(bool isLeft)
    {
        var timer = isLeft ? _leftDebounceTimer : _rightDebounceTimer;
        if (timer is null)
        {
            return;
        }
        timer.Stop();
        timer.Start();
    }

    private async Task ReloadDiffAsync(DataGrid grid)
    {
        if (IsBusyForRefresh(grid))
        {
            return;
        }

        var isLeft = grid == LeftGrid;
        var path = GetCurrentPath(grid);
        if (string.IsNullOrEmpty(path) || ArchivePath.Contains(path))
        {
            return;
        }

        if (!Directory.Exists(path))
        {
            return;
        }

        var target = isLeft ? LeftItems : RightItems;
        var loadLarge = _settings.ViewMode == ViewMode.Icons;
        var options = new DirectoryLoadOptions(
            ShowHidden,
            LoadSmallIcons: !loadLarge,
            LoadLargeIcons: loadLarge,
            IncludeOwner: IsFileColumnVisible("Owner"));

        List<FileItem> newItems;
        try
        {
            newItems = await Task.Run(() => DirectoryLoader.Load(path, options, CancellationToken.None));
        }
        catch
        {
            return;
        }

        if (!string.Equals(GetCurrentPath(grid), path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (IsBusyForRefresh(grid))
        {
            return;
        }

        DiffApply(target, newItems);
        ApplySearchFilter();
        UpdateStatus();
    }

    private bool IsBusyForRefresh(DataGrid grid)
    {
        if (!grid.IsReadOnly)
        {
            return true;
        }
        if (_pendingFileDragItem is not null)
        {
            return true;
        }
        if (_isRubberBandSelecting)
        {
            return true;
        }
        if (grid.ContextMenu is { IsOpen: true })
        {
            return true;
        }
        return false;
    }

    private static void DiffApply(ObservableCollection<FileItem> existing, List<FileItem> newItems)
    {
        if (newItems.Count == 0 && existing.Count == 0)
        {
            return;
        }

        var newNames = new HashSet<string>(newItems.Select(i => i.Name), StringComparer.OrdinalIgnoreCase);

        for (var i = existing.Count - 1; i >= 0; i--)
        {
            if (!newNames.Contains(existing[i].Name))
            {
                existing.RemoveAt(i);
            }
        }

        for (var i = 0; i < newItems.Count; i++)
        {
            var n = newItems[i];
            if (i < existing.Count && NameEquals(existing[i].Name, n.Name))
            {
                continue;
            }

            var found = FindIndexFrom(existing, n.Name, i);
            if (found >= 0)
            {
                if (found != i)
                {
                    existing.Move(found, i);
                }
            }
            else
            {
                existing.Insert(i, n);
            }
        }
    }

    private static int FindIndexFrom(ObservableCollection<FileItem> coll, string name, int start)
    {
        for (var j = start; j < coll.Count; j++)
        {
            if (NameEquals(coll[j].Name, name))
            {
                return j;
            }
        }
        return -1;
    }

    private static bool NameEquals(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
