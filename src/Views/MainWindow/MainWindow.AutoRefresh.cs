using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Tfx;

public partial class MainWindow
{
    private static readonly TimeSpan AutoRefreshDebounce = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan AutoRefreshPeriodic = TimeSpan.FromSeconds(30);

    private bool _windowActive = true;
    private bool _leftRefreshInFlight;
    private bool _rightRefreshInFlight;

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
            if (!_windowActive)
            {
                return;
            }
            // Only run periodic refresh when the FileSystemWatcher is not available
            // for that pane (network share, archive, FSW failure). When FSW is healthy
            // it already covers external changes, and polling is redundant.
            if (_leftWatcher is null)
            {
                _ = ReloadDiffAsync(LeftGrid);
            }
            if (_rightWatcher is null && RightPaneColumn.Width.Value > 0)
            {
                _ = ReloadDiffAsync(RightGrid);
            }
        };
        _periodicTimer.Start();

        Activated += (_, _) => _windowActive = true;
        Deactivated += (_, _) => _windowActive = false;
        StateChanged += (_, _) =>
        {
            _windowActive = WindowState != System.Windows.WindowState.Minimized;
        };
    }

    private void DisposeAutoRefresh()
    {
        _periodicTimer?.Stop();
        _leftDebounceTimer?.Stop();
        _rightDebounceTimer?.Stop();
        if (_leftWatcher is not null)
        {
            _leftWatcher.EnableRaisingEvents = false;
            _leftWatcher.Dispose();
            _leftWatcher = null;
        }
        if (_rightWatcher is not null)
        {
            _rightWatcher.EnableRaisingEvents = false;
            _rightWatcher.Dispose();
            _rightWatcher = null;
        }
    }

    private async void UpdateWatcherForPane(Pane pane)
    {
        var existing = pane == Pane.Left ? _leftWatcher : _rightWatcher;
        existing?.Dispose();
        SetWatcher(pane, null);

        var path = PathOf(pane);
        if (string.IsNullOrEmpty(path) || ArchivePath.Contains(path))
        {
            return;
        }

        // UNC / network shares: FileSystemWatcher is unreliable (events drop
        // silently, creation can hang on slow shares). Rely on the periodic
        // poll instead — it auto-runs for panes whose watcher is null.
        if (IsLikelyNetworkPath(path))
        {
            return;
        }

        // Run Directory.Exists and FileSystemWatcher creation on a background
        // thread so a slow drive does not stall the UI during navigation.
        FileSystemWatcher? watcher;
        try
        {
            watcher = await Task.Run(() =>
            {
                try
                {
                    if (!Directory.Exists(path))
                    {
                        return null;
                    }
                    return new FileSystemWatcher(path)
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
                    return null;
                }
            });
        }
        catch
        {
            return;
        }

        if (watcher is null)
        {
            return;
        }

        // The pane may have navigated again while we were on the background
        // thread; in that case throw away the watcher we just built.
        if (!string.Equals(PathOf(pane), path, StringComparison.OrdinalIgnoreCase))
        {
            watcher.Dispose();
            return;
        }

        FileSystemEventHandler change = (_, _) => Dispatcher.BeginInvoke(() => KickDebounce(pane));
        RenamedEventHandler rename = (_, _) => Dispatcher.BeginInvoke(() => KickDebounce(pane));
        ErrorEventHandler error = (_, _) => Dispatcher.BeginInvoke(() => UpdateWatcherForPane(pane));

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

        SetWatcher(pane, watcher);
    }

    private static bool IsLikelyNetworkPath(string path) =>
        path.StartsWith(@"\\", StringComparison.Ordinal);

    private void SetWatcher(Pane pane, FileSystemWatcher? watcher)
    {
        if (pane == Pane.Left)
        {
            _leftWatcher = watcher;
        }
        else
        {
            _rightWatcher = watcher;
        }
    }

    private void KickDebounce(Pane pane)
    {
        var timer = pane == Pane.Left ? _leftDebounceTimer : _rightDebounceTimer;
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

        var pane = PaneOf(grid);
        var inFlight = pane == Pane.Left ? _leftRefreshInFlight : _rightRefreshInFlight;
        if (inFlight)
        {
            return;
        }

        if (pane == Pane.Left)
        {
            _leftRefreshInFlight = true;
        }
        else
        {
            _rightRefreshInFlight = true;
        }

        try
        {
            await ReloadDiffCoreAsync(grid, pane);
        }
        finally
        {
            if (pane == Pane.Left)
            {
                _leftRefreshInFlight = false;
            }
            else
            {
                _rightRefreshInFlight = false;
            }
        }
    }

    private async Task ReloadDiffCoreAsync(DataGrid grid, Pane pane)
    {
        var path = GetCurrentPath(grid);
        if (string.IsNullOrEmpty(path) || ArchivePath.Contains(path))
        {
            return;
        }

        var target = ItemsOf(pane);
        var loadLarge = _settings.ViewMode == ViewMode.Icons;
        var options = new DirectoryLoadOptions(
            ShowHidden,
            LoadSmallIcons: !loadLarge,
            LoadLargeIcons: loadLarge,
            IncludeOwner: IsFileColumnVisible("Owner"));

        List<FileItem>? newItems;
        try
        {
            // Run Directory.Exists on the background thread along with the
            // enumeration — both can stall on a slow / offline network share.
            newItems = await Task.Run(() =>
            {
                if (!Directory.Exists(path))
                {
                    return null;
                }
                return DirectoryLoader.Load(path, options, CancellationToken.None);
            });
        }
        catch
        {
            return;
        }

        if (newItems is null)
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
