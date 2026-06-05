using System.Collections.ObjectModel;
using System.IO;
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
            return;
        }

        SetGitState(pane, root, GetGitStatus(pane));
        _ = FetchGitStatusAsync(pane, root);
    }

    private async Task FetchGitStatusAsync(Pane pane, string root)
    {
        CancelGitFetch(pane);
        var cts = new CancellationTokenSource();
        if (pane == Pane.Left) _leftGitCts = cts;
        else _rightGitCts = cts;

        GitWorkingCopyStatus? status;
        try
        {
            // Run off the UI thread: ReadAsync spawns `git status` and the
            // process spawn itself (before its first await) would otherwise run
            // synchronously on the caller. This handler is invoked on the UI
            // thread for every navigation, tab switch and external change.
            status = await Task.Run(() => GitStatusReader.ReadAsync(root, cts.Token), cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (cts.IsCancellationRequested)
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
                // worktree change, "?" if it contains untracked entries.
                item.GitStatusText = AggregateDirectory(status, rel);
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

    private static string AggregateDirectory(GitWorkingCopyStatus status, string dirRel)
    {
        // Empty rel means the repo root directory itself — we never tag the
        // current folder, only entries within it.
        if (string.IsNullOrEmpty(dirRel))
        {
            return "";
        }

        var prefix = dirRel.EndsWith('/') ? dirRel : dirRel + "/";
        var sawUntracked = false;
        var sawTracked = false;
        foreach (var pair in status.Files)
        {
            if (!pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (pair.Value == GitFileStatus.Untracked)
            {
                sawUntracked = true;
            }
            else if (pair.Value != GitFileStatus.Ignored)
            {
                sawTracked = true;
            }
            if (sawTracked) break;
        }

        if (sawTracked) return "M";
        if (sawUntracked) return "?";
        return "";
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
}
