using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using Path = System.IO.Path;

namespace Tfx;

public partial class MainWindow
{
    // Per-pane cached Git state.
    private string? _leftGitRoot;
    private string? _rightGitRoot;
    private GitWorkingCopyStatus? _leftGitStatus;
    private GitWorkingCopyStatus? _rightGitStatus;
    private CancellationTokenSource? _leftGitCts;
    private CancellationTokenSource? _rightGitCts;

    private string? GetGitRoot(Pane pane) => pane == Pane.Left ? _leftGitRoot : _rightGitRoot;
    private GitWorkingCopyStatus? GetGitStatus(Pane pane) => pane == Pane.Left ? _leftGitStatus : _rightGitStatus;

    private void SetGitState(Pane pane, string? root, GitWorkingCopyStatus? status)
    {
        if (pane == Pane.Left)
        {
            _leftGitRoot = root;
            _leftGitStatus = status;
        }
        else
        {
            _rightGitRoot = root;
            _rightGitStatus = status;
        }
    }

    /// <summary>
    /// Called from <c>Navigate</c> after the new path is set. Walks for a
    /// <c>.git</c> ancestor and, if found, kicks off a background
    /// <c>git status</c>. When the path is outside a working copy, clears any
    /// previous Git state and badges for that pane.
    /// </summary>
    private void RefreshGitStatusForPane(Pane pane)
    {
        var path = PathOf(pane);
        var root = GitStatusReader.FindRoot(path);

        if (root is null)
        {
            CancelGitFetch(pane);
            SetGitState(pane, null, null);
            ClearGitBadges(pane);
            UpdateGitBranchText();
            DisposeGitWatcher(pane);
            return;
        }

        SetGitState(pane, root, GetGitStatus(pane));
        EnsureGitRootWatcher(pane, root);
        _ = FetchGitStatusAsync(pane, root);
    }

    private async Task FetchGitStatusAsync(Pane pane, string root)
    {
        CancelGitFetch(pane);
        var cts = new CancellationTokenSource();
        // Capture the token before handing it to the lambda: the lambda runs on
        // the thread pool after this method returns, and by then a rapid
        // re-navigation may have disposed `cts` — reading cts.Token at that
        // point throws ObjectDisposedException into an unobserved task.
        var token = cts.Token;
        if (pane == Pane.Left) _leftGitCts = cts;
        else _rightGitCts = cts;

        GitWorkingCopyStatus? status;
        try
        {
            // Run off the UI thread: ReadAsync spawns `git status` and the
            // process spawn itself (before its first await) would otherwise run
            // synchronously on the caller. This handler is invoked on the UI
            // thread for every navigation, tab switch and external change.
            status = await Task.Run(() => GitStatusReader.ReadAsync(root, token), token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested)
        {
            return;
        }
        // If user navigated away while we were fetching, drop the result.
        if (!string.Equals(GetGitRoot(pane), root, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        SetGitState(pane, root, status);
        ApplyGitBadges(pane);
        UpdateGitBranchText();
    }

    private void CancelGitFetch(Pane pane)
    {
        var existing = pane == Pane.Left ? _leftGitCts : _rightGitCts;
        existing?.Cancel();
        existing?.Dispose();
        if (pane == Pane.Left) _leftGitCts = null;
        else _rightGitCts = null;
    }

    /// <summary>
    /// Stamp each FileItem in the pane with its Git status badge, based on
    /// the cached working-copy status.
    /// </summary>
    private void ApplyGitBadges(Pane pane)
    {
        var items = ItemsOf(pane);
        var status = GetGitStatus(pane);
        var root = GetGitRoot(pane);

        if (status is null || root is null)
        {
            foreach (var item in items)
            {
                if (item.GitStatusText.Length != 0)
                {
                    item.GitStatusText = "";
                }
            }
            return;
        }

        foreach (var item in items)
        {
            if (item.IsParent)
            {
                item.GitStatusText = "";
                continue;
            }
            var rel = MakeRelativeForGit(root, item.FullPath);
            if (rel is null)
            {
                item.GitStatusText = "";
                continue;
            }

            if (item.IsDirectory)
            {
                // Aggregate: directory is "M" if any tracked descendant has a
                // worktree change, "?" if it contains untracked entries. The
                // per-status dictionary makes this an O(1) lookup per row.
                item.GitStatusText = rel.Length != 0
                    && status.DirectoryBadges.TryGetValue(rel.TrimEnd('/'), out var badge)
                        ? badge
                        : "";
            }
            else
            {
                item.GitStatusText = status.Files.TryGetValue(rel, out var s)
                    ? GitStatusParser.Badge(s)
                    : "";
            }
        }
    }

    private void ClearGitBadges(Pane pane)
    {
        foreach (var item in ItemsOf(pane))
        {
            if (item.GitStatusText.Length != 0)
            {
                item.GitStatusText = "";
            }
        }
    }

    private static string? MakeRelativeForGit(string root, string fullPath)
    {
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        var rest = fullPath[normalizedRoot.Length..]
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return rest.Replace('\\', '/');
    }

    private void UpdateGitBranchText()
    {
        var status = GetGitStatus(ActivePane);
        if (status?.Branch is { Length: > 0 } branch)
        {
            GitBranchText.Text = "⎇ " + branch;
        }
        else
        {
            GitBranchText.Text = "";
        }
    }

    // ---- Git-root watcher: refresh badges on repo-wide changes ----
    //
    // The pane's auto-refresh FileSystemWatcher (MainWindow.AutoRefresh) is
    // non-recursive and only covers the displayed folder, so edits in
    // subfolders (an IDE saving while a parent is displayed) and `git add` /
    // `git commit` from a terminal (which only touch `.git`) never re-fetched
    // `git status` — badges went stale until the next navigation. Each pane
    // gets a recursive watcher on its Git root that debounces into a
    // badge-only refresh (no folder reload).

    private static readonly TimeSpan GitWatchDebounce = TimeSpan.FromMilliseconds(500);
    private const long GitWatchMaxWaitMs = 2000;

    private FileSystemWatcher? _leftGitWatcher;
    private FileSystemWatcher? _rightGitWatcher;
    // Bumped every time EnsureGitRootWatcher/DisposeGitWatcher runs so a call
    // that resumes after its background hop can tell it has been superseded
    // (same pattern as UpdateWatcherForPane).
    private int _leftGitWatcherGeneration;
    private int _rightGitWatcherGeneration;
    private DispatcherTimer? _leftGitDebounceTimer;
    private DispatcherTimer? _rightGitDebounceTimer;
    private long _leftGitDebounceFirstKick;
    private long _rightGitDebounceFirstKick;
    // Set when watcher events arrive while the window is inactive: the refresh
    // is deferred to the next Activated so background builds and commits don't
    // keep running `git status` for a window nobody is looking at.
    private bool _leftGitRefreshPending;
    private bool _rightGitRefreshPending;

    private void InitializeGitWatch()
    {
        _leftGitDebounceTimer = new DispatcherTimer { Interval = GitWatchDebounce };
        _leftGitDebounceTimer.Tick += (_, _) =>
        {
            _leftGitDebounceTimer!.Stop();
            _leftGitDebounceFirstKick = 0;
            FireGitRefresh(Pane.Left);
        };

        _rightGitDebounceTimer = new DispatcherTimer { Interval = GitWatchDebounce };
        _rightGitDebounceTimer.Tick += (_, _) =>
        {
            _rightGitDebounceTimer!.Stop();
            _rightGitDebounceFirstKick = 0;
            FireGitRefresh(Pane.Right);
        };

        Activated += (_, _) =>
        {
            if (_leftGitRefreshPending)
            {
                _leftGitRefreshPending = false;
                RefreshGitStatusForPane(Pane.Left);
            }
            if (_rightGitRefreshPending)
            {
                _rightGitRefreshPending = false;
                RefreshGitStatusForPane(Pane.Right);
            }
        };
    }

    private void FireGitRefresh(Pane pane)
    {
        var root = GetGitRoot(pane);
        if (root is not null)
        {
            _ = FetchGitStatusAsync(pane, root);
        }
    }

    private async void EnsureGitRootWatcher(Pane pane, string root)
    {
        var existing = pane == Pane.Left ? _leftGitWatcher : _rightGitWatcher;
        if (existing is not null && string.Equals(existing.Path, root, StringComparison.OrdinalIgnoreCase))
        {
            return; // still watching the same repo
        }

        var generation = pane == Pane.Left ? ++_leftGitWatcherGeneration : ++_rightGitWatcherGeneration;
        existing?.Dispose();
        SetGitWatcher(pane, null);

        // UNC / network shares: FSW is unreliable there (see UpdateWatcherForPane).
        // Badges then refresh on navigation/tab switch only, as before.
        if (IsLikelyNetworkPath(root))
        {
            return;
        }

        // Create on a background thread — recursive watcher setup on a large
        // tree or slow drive must not stall the UI during navigation.
        FileSystemWatcher? watcher;
        try
        {
            watcher = await Task.Run(() =>
            {
                try
                {
                    if (!Directory.Exists(root))
                    {
                        return null;
                    }
                    return new FileSystemWatcher(root)
                    {
                        NotifyFilter = NotifyFilters.FileName
                            | NotifyFilters.DirectoryName
                            | NotifyFilters.LastWrite
                            | NotifyFilters.Size,
                        IncludeSubdirectories = true,
                        // Largest allowed buffer: a build touching thousands of
                        // files overflows the default 8KB immediately.
                        InternalBufferSize = 64 * 1024,
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

        var currentGeneration = pane == Pane.Left ? _leftGitWatcherGeneration : _rightGitWatcherGeneration;
        if (generation != currentGeneration
            || !string.Equals(GetGitRoot(pane), root, StringComparison.OrdinalIgnoreCase))
        {
            watcher.Dispose();
            return;
        }

        FileSystemEventHandler change = (_, e) => OnGitWatchEvent(pane, root, e.FullPath);
        watcher.Changed += change;
        watcher.Created += change;
        watcher.Deleted += change;
        watcher.Renamed += (_, e) =>
        {
            OnGitWatchEvent(pane, root, e.OldFullPath);
            OnGitWatchEvent(pane, root, e.FullPath);
        };
        // Buffer overflow means "an unknown number of things changed" — treat
        // it as a change rather than tearing the watcher down.
        watcher.Error += (_, _) => Dispatcher.BeginInvoke(() => KickGitDebounce(pane));

        try
        {
            watcher.EnableRaisingEvents = true;
        }
        catch
        {
            watcher.Dispose();
            return;
        }

        SetGitWatcher(pane, watcher);
    }

    /// <summary>Runs on the FSW callback thread — keep it allocation-light.</summary>
    private void OnGitWatchEvent(Pane pane, string root, string fullPath)
    {
        if (!IsGitBadgeRelevantChange(root, fullPath))
        {
            return;
        }
        Dispatcher.BeginInvoke(() => KickGitDebounce(pane));
    }

    /// <summary>
    /// Filters watcher events down to those that can change a badge or the
    /// branch label. Worktree paths always qualify; inside <c>.git</c> only
    /// <c>index</c> (stage/unstage), <c>HEAD</c> / <c>*_HEAD</c> and
    /// <c>refs/</c> (commit, branch switch, merge) matter. Object/pack writes,
    /// reflog appends and <c>*.lock</c> churn are noise. Note `git status`
    /// itself may rewrite <c>index</c> (stat-cache refresh) — that costs one
    /// extra fetch which converges immediately.
    /// </summary>
    private static bool IsGitBadgeRelevantChange(string root, string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath))
        {
            return false;
        }

        var dotGit = Path.Combine(root, ".git");
        if (!fullPath.StartsWith(dotGit, StringComparison.OrdinalIgnoreCase))
        {
            return true; // ordinary worktree change
        }
        var rest = fullPath[dotGit.Length..];
        if (rest.Length > 0 && rest[0] != Path.DirectorySeparatorChar && rest[0] != Path.AltDirectorySeparatorChar)
        {
            return true; // ".github", ".gitignore", … — merely shares the prefix
        }

        var rel = rest.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
        if (rel.Length == 0)
        {
            return false; // the .git directory entry itself
        }
        if (rel.EndsWith(".lock", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (rel.StartsWith("objects/", StringComparison.OrdinalIgnoreCase)
            || rel.StartsWith("logs/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return rel.Equals("index", StringComparison.OrdinalIgnoreCase)
            || rel.Equals("HEAD", StringComparison.OrdinalIgnoreCase)
            || rel.EndsWith("_HEAD", StringComparison.OrdinalIgnoreCase)
            || rel.StartsWith("refs/", StringComparison.OrdinalIgnoreCase);
    }

    private void KickGitDebounce(Pane pane)
    {
        if (!_windowActive)
        {
            if (pane == Pane.Left) _leftGitRefreshPending = true;
            else _rightGitRefreshPending = true;
            return;
        }

        var timer = pane == Pane.Left ? _leftGitDebounceTimer : _rightGitDebounceTimer;
        if (timer is null)
        {
            return;
        }

        // Trailing debounce with a max wait, same rationale as KickDebounce:
        // a burst (build output, large checkout) must not postpone the refresh
        // forever.
        var now = Environment.TickCount64;
        var first = pane == Pane.Left ? _leftGitDebounceFirstKick : _rightGitDebounceFirstKick;
        if (first == 0)
        {
            if (pane == Pane.Left) _leftGitDebounceFirstKick = now;
            else _rightGitDebounceFirstKick = now;
        }
        else if (now - first > GitWatchMaxWaitMs)
        {
            timer.Stop();
            if (pane == Pane.Left) _leftGitDebounceFirstKick = 0;
            else _rightGitDebounceFirstKick = 0;
            FireGitRefresh(pane);
            return;
        }

        timer.Stop();
        timer.Start();
    }

    private void SetGitWatcher(Pane pane, FileSystemWatcher? watcher)
    {
        if (pane == Pane.Left)
        {
            _leftGitWatcher = watcher;
        }
        else
        {
            _rightGitWatcher = watcher;
        }
    }

    private void DisposeGitWatcher(Pane pane)
    {
        var existing = pane == Pane.Left ? _leftGitWatcher : _rightGitWatcher;
        if (existing is not null)
        {
            existing.EnableRaisingEvents = false;
            existing.Dispose();
        }
        SetGitWatcher(pane, null);
        // Supersede any EnsureGitRootWatcher still on its background hop.
        if (pane == Pane.Left) _leftGitWatcherGeneration++;
        else _rightGitWatcherGeneration++;

        var timer = pane == Pane.Left ? _leftGitDebounceTimer : _rightGitDebounceTimer;
        timer?.Stop();
        if (pane == Pane.Left)
        {
            _leftGitDebounceFirstKick = 0;
            _leftGitRefreshPending = false;
        }
        else
        {
            _rightGitDebounceFirstKick = 0;
            _rightGitRefreshPending = false;
        }
    }

    private void DisposeGitWatch()
    {
        DisposeGitWatcher(Pane.Left);
        DisposeGitWatcher(Pane.Right);
    }
}
