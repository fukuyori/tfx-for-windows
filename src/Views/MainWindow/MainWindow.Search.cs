using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Path = System.IO.Path;

namespace Tfx;

public partial class MainWindow
{
    private const int SubfolderSearchBatch = 50;
    private static readonly TimeSpan SubfolderStatusInterval = TimeSpan.FromMilliseconds(250);

    private CancellationTokenSource? _subfolderSearchCts;
    // True while the walker is running.
    private bool _subfolderSearchActive;
    // True from search start until the user leaves search mode (Esc, empty
    // Enter, navigation, reload). Covers the "complete" state too, so the
    // status line keeps the search summary while results are on screen.
    private bool _subfolderSearchShown;
    private Pane _subfolderSearchPane;
    private string _subfolderSearchRoot = "";
    private string _subfolderSearchQuery = "";
    private int _subfolderSearchMatches;
    private SubfolderSearchProgress? _subfolderSearchProgress;
    private readonly System.Diagnostics.Stopwatch _subfolderSearchStopwatch = new();
    private System.Windows.Threading.DispatcherTimer? _subfolderSearchTimer;

    /// <summary>
    /// Shared between the walker thread and the UI: the number of entries
    /// enumerated so far (matching or not). Read on the UI thread by the
    /// status timer; written with Interlocked on the walker thread.
    /// </summary>
    private sealed class SubfolderSearchProgress
    {
        public long Scanned;
    }

    /// <summary>True when the active pane is showing subfolder-search results.</summary>
    private bool IsSearchStatusShown => _subfolderSearchShown && _subfolderSearchPane == ActivePane;

    /// <summary>
    /// Single source of the search status text: "Searching / Search complete",
    /// elapsed time, entries scanned, matches. UpdateStatus renders this while
    /// search results are shown, so no other status writer interleaves with it.
    /// </summary>
    private string BuildSearchStatusText()
    {
        var scanned = _subfolderSearchProgress is { } progress
            ? Interlocked.Read(ref progress.Scanned)
            : 0L;
        var elapsed = FormatSearchElapsed(_subfolderSearchStopwatch.Elapsed);
        var key = _subfolderSearchActive
            ? "Searching \"{0}\"  {1}  scanned {2:N0}  matched {3:N0}"
            : "Search complete \"{0}\"  {1}  scanned {2:N0}  matched {3:N0}";
        return Loc.F(key, _subfolderSearchQuery, elapsed, scanned, _subfolderSearchMatches);
    }

    private static string FormatSearchElapsed(TimeSpan elapsed)
    {
        if (elapsed.TotalSeconds < 60)
        {
            return Loc.F("{0:0.0}s", elapsed.TotalSeconds);
        }
        return Loc.F("{0}m {1:00}s", (int)elapsed.TotalMinutes, elapsed.Seconds);
    }

    private void FocusSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_subfolderSearchShown)
            {
                LeaveSubfolderSearch();
            }
            else
            {
                // No search on screen: Esc just clears the box and refreshes.
                SearchBox.Text = "";
                Reload(LeftGrid);
                Reload(RightGrid);
            }
            FocusActiveListing();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            var query = SearchBox.Text.Trim();
            if (string.IsNullOrEmpty(query))
            {
                CancelSubfolderSearch();
                Reload(LeftGrid);
                Reload(RightGrid);
            }
            else
            {
                StartSubfolderSearch(query);
            }
            e.Handled = true;
        }
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Up / Down / PageUp / PageDown drive the listing selection from the
        // search box when the box is empty, and also while search results are
        // on screen (the query text stays in the box after Enter, so the
        // empty-box rule alone left no way to step into the results).
        var forwardToListing = string.IsNullOrEmpty(SearchBox.Text) || _subfolderSearchShown;
        if (!forwardToListing || Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        if (e.Key is Key.Down or Key.Up or Key.PageDown or Key.PageUp)
        {
            MoveActiveListingSelection(e.Key);
            e.Handled = true;
        }
    }

    // ─── Recursive (subfolder) search ──────────────────────────────────────

    private void StartSubfolderSearch(string query)
    {
        CancelSubfolderSearch();

        var pane = ActivePane;
        var grid = GridOf(pane);
        var root = GetCurrentPath(grid);

        // Subfolder search is disabled inside zip archives — the existing
        // archive listing is already flat enough, and a recursive walk would
        // need to re-open the archive per visit.
        if (string.IsNullOrEmpty(root) || ArchivePath.Contains(root))
        {
            return;
        }

        var cts = new CancellationTokenSource();
        var progress = new SubfolderSearchProgress();
        _subfolderSearchCts = cts;
        _subfolderSearchActive = true;
        _subfolderSearchShown = true;
        _subfolderSearchPane = pane;
        _subfolderSearchRoot = root;
        _subfolderSearchQuery = query;
        _subfolderSearchMatches = 0;
        _subfolderSearchProgress = progress;
        _subfolderSearchStopwatch.Restart();
        var target = ItemsOf(pane);
        target.Clear();

        var showHidden = ShowHidden;
        UpdateStatus();
        StartSearchStatusTimer();

        _ = RunSubfolderSearchAsync(target, root, query, showHidden, progress, cts);
    }

    // Periodic status refresh on the UI thread. Decoupled from match arrival
    // so a search that finds nothing for a long stretch still shows elapsed
    // time and the scanned count moving.
    private void StartSearchStatusTimer()
    {
        if (_subfolderSearchTimer is null)
        {
            _subfolderSearchTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = SubfolderStatusInterval,
            };
            _subfolderSearchTimer.Tick += (_, _) =>
            {
                if (_subfolderSearchActive)
                {
                    UpdateStatus();
                }
                else
                {
                    _subfolderSearchTimer!.Stop();
                }
            };
        }
        _subfolderSearchTimer.Start();
    }

    private void StopSearchStatusTimer()
    {
        _subfolderSearchTimer?.Stop();
        _subfolderSearchStopwatch.Stop();
    }

    private async Task RunSubfolderSearchAsync(
        System.Collections.ObjectModel.ObservableCollection<FileItem> target,
        string root,
        string query,
        bool showHidden,
        SubfolderSearchProgress progress,
        CancellationTokenSource cts)
    {
        var token = cts.Token;
        var matches = 0;
        var batch = new List<FileItem>();

        try
        {
            await foreach (var item in EnumerateMatchesAsync(root, query, showHidden, progress, token))
            {
                token.ThrowIfCancellationRequested();
                batch.Add(item);
                matches++;
                if (batch.Count >= SubfolderSearchBatch)
                {
                    await FlushBatchAsync(target, batch, matches, token);
                    batch = new List<FileItem>();
                }
            }

            if (batch.Count > 0)
            {
                await FlushBatchAsync(target, batch, matches, token);
            }

            await Dispatcher.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested) return;
                _subfolderSearchMatches = matches;
                FinishSubfolderSearch(cts);
                UpdateStatus();
            });
        }
        catch (OperationCanceledException)
        {
            // Cancellation: leave whatever results we already streamed.
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                FinishSubfolderSearch(cts);
                SetStatus(ex.Message);
            });
        }
        finally
        {
            FinishSubfolderSearch(cts);
            cts.Dispose();
        }
    }

    // Marks the walker as finished (idempotent). Leaves _subfolderSearchShown
    // set: results stay on screen with the "Search complete" summary until the
    // user leaves search mode.
    private void FinishSubfolderSearch(CancellationTokenSource cts)
    {
        if (!ReferenceEquals(_subfolderSearchCts, cts))
        {
            return;
        }
        _subfolderSearchCts = null;
        _subfolderSearchActive = false;
        StopSearchStatusTimer();
    }

    private async Task FlushBatchAsync(
        System.Collections.ObjectModel.ObservableCollection<FileItem> target,
        List<FileItem> batch,
        int totalMatches,
        CancellationToken token)
    {
        var snapshot = batch.ToArray();
        await Dispatcher.InvokeAsync(() =>
        {
            if (token.IsCancellationRequested) return;
            foreach (var item in snapshot)
            {
                target.Add(item);
            }
            _subfolderSearchMatches = totalMatches;
            UpdateStatus();
        });
    }

    private static async IAsyncEnumerable<FileItem> EnumerateMatchesAsync(
        string root,
        string query,
        bool showHidden,
        SubfolderSearchProgress progress,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
    {
        // Bounded with backpressure: an unbounded channel let a search that
        // matches hundreds of thousands of entries grow memory without limit
        // while the UI consumed slower than the walker produced.
        var channel = System.Threading.Channels.Channel.CreateBounded<FileItem>(
            new System.Threading.Channels.BoundedChannelOptions(4096)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = System.Threading.Channels.BoundedChannelFullMode.Wait,
            });

        _ = Task.Run(() =>
        {
            try
            {
                Walk(root, query, showHidden, progress, channel.Writer, token);
            }
            catch (OperationCanceledException) { }
            finally
            {
                channel.Writer.TryComplete();
            }
        }, token);

        await foreach (var item in channel.Reader.ReadAllAsync(token))
        {
            yield return item;
        }
    }

    private static void Walk(
        string root,
        string query,
        bool showHidden,
        SubfolderSearchProgress progress,
        System.Threading.Channels.ChannelWriter<FileItem> writer,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        // Single recursive enumeration via .NET's built-in walker. The
        // runtime hands one `FindFirstFile`-backed iterator per directory and
        // surfaces both files and folders together with their attributes
        // already populated — no extra stat per entry, no manual recursion
        // overhead. Critical for SMB / network shares where every round trip
        // is expensive.
        var skipAttrs = FileAttributes.ReparsePoint;
        if (!showHidden)
        {
            skipAttrs |= FileAttributes.Hidden | FileAttributes.System;
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = skipAttrs,
            ReturnSpecialDirectories = false,
        };

        DirectoryInfo rootInfo;
        try
        {
            rootInfo = new DirectoryInfo(root);
        }
        catch
        {
            return;
        }

        IEnumerable<FileSystemInfo> enumerator;
        try
        {
            enumerator = rootInfo.EnumerateFileSystemInfos("*", options);
        }
        catch
        {
            return;
        }

        var compareInfo = System.Globalization.CultureInfo.CurrentCulture.CompareInfo;
        const System.Globalization.CompareOptions matchOpts =
            System.Globalization.CompareOptions.IgnoreCase
            | System.Globalization.CompareOptions.IgnoreWidth
            | System.Globalization.CompareOptions.IgnoreKanaType;

        var iterator = enumerator.GetEnumerator();
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                bool moved;
                try
                {
                    moved = iterator.MoveNext();
                }
                catch
                {
                    // A single inaccessible entry threw during enumeration;
                    // bail this directory but keep results found so far.
                    break;
                }
                if (!moved) break;

                var info = iterator.Current;
                // Count every enumerated entry, matching or not: this is the
                // "scanned" figure in the status line.
                Interlocked.Increment(ref progress.Scanned);
                var name = info.Name;
                if (string.IsNullOrEmpty(name)) continue;

                // tfx hides dot-prefixed entries when ShowHidden is off
                // (independent of Windows Hidden attribute).
                if (!showHidden && name.Length > 1 && name[0] == '.') continue;

                if (compareInfo.IndexOf(name, query, matchOpts) < 0) continue;

                // Blocking write (the walker runs on its own Task.Run thread):
                // when the channel is full this waits for the UI to drain it
                // instead of dropping the match like TryWrite would.
                var write = writer.WriteAsync(BuildSearchResult(info, root), token);
                if (!write.IsCompletedSuccessfully)
                {
                    write.AsTask().GetAwaiter().GetResult();
                }
            }
        }
        finally
        {
            iterator.Dispose();
        }
    }

    private static FileItem BuildSearchResult(FileSystemInfo info, string root)
    {
        // The enumerated info already carries size/attributes/timestamps, so the
        // row is built without a second stat per match. Shell icons are never
        // shown (the list binds IconGlyph), so skip loading them.
        var rel = MakeRelative(root, info.FullName);
        var item = info is DirectoryInfo directory
            ? FileItem.FromDirectory(directory, loadSmallIcon: false, loadLargeIcon: false, includeOwner: false)
            : FileItem.FromFile((FileInfo)info, loadSmallIcon: false, loadLargeIcon: false, includeOwner: false);

        // Use the relative path as the visible name so the user can see where
        // each match lives. FullPath is preserved so Open / Reveal continue
        // to work.
        return new FileItem
        {
            Name = rel,
            FullPath = item.FullPath,
            Kind = item.Kind,
            IsDirectory = item.IsDirectory,
            IsParent = false,
            IsSearchResult = true,
            Size = item.Size,
            Modified = item.Modified,
            Created = item.Created,
            SizeText = item.SizeText,
            ModifiedText = item.ModifiedText,
            CreatedText = item.CreatedText,
            OwnerText = item.OwnerText,
            AttributeText = item.AttributeText,
            Icon = item.Icon,
            LargeIcon = item.LargeIcon,
        };
    }

    private static string MakeRelative(string root, string fullPath)
    {
        if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(fullPath))
        {
            return Path.GetFileName(fullPath);
        }
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            var rel = fullPath[normalizedRoot.Length..];
            return rel.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        return fullPath;
    }

    /// <summary>
    /// User-initiated exit from search mode (Esc in the search box or in the
    /// listing): clears the box, stops the walk, and restores the real folder
    /// listing in the pane that held the results.
    /// </summary>
    private void LeaveSubfolderSearch()
    {
        var pane = _subfolderSearchPane;
        SearchBox.Text = "";
        CancelSubfolderSearch();
        Reload(GridOf(pane));
    }

    /// <summary>True when <paramref name="pane"/> is showing subfolder-search results.</summary>
    private bool IsPaneInSearchMode(Pane pane) => _subfolderSearchShown && _subfolderSearchPane == pane;

    // ─── File operations while results are shown ──────────────────────────
    //
    // Rule: an operation that removes or renames result rows keeps the pane
    // in search mode and patches the rows in place (delete, move out, rename);
    // an operation that creates something in the searched folder itself
    // (paste / drop into it, new item, zip, extract) leaves search mode and
    // shows the real listing, since the results view cannot show the new item.

    /// <summary>
    /// After a delete / move, drops result rows whose entry no longer exists at
    /// its recorded path. Only rows at or under <paramref name="affectedPaths"/>
    /// are probed (one stat per candidate), so a large result set is not
    /// re-checked wholesale.
    /// </summary>
    private void PruneSearchResults(Pane pane, IReadOnlyCollection<string> affectedPaths)
    {
        if (!IsPaneInSearchMode(pane) || affectedPaths.Count == 0)
        {
            return;
        }

        var items = ItemsOf(pane);
        var removed = 0;
        for (var i = items.Count - 1; i >= 0; i--)
        {
            var path = items[i].FullPath;
            if (!IsAtOrUnderAny(path, affectedPaths))
            {
                continue;
            }
            if (File.Exists(path) || Directory.Exists(path))
            {
                continue;
            }
            items.RemoveAt(i);
            removed++;
        }

        if (removed > 0)
        {
            _subfolderSearchMatches = Math.Max(0, _subfolderSearchMatches - removed);
            UpdateStatus();
        }
    }

    private static bool IsAtOrUnderAny(string path, IEnumerable<string> roots)
    {
        foreach (var root in roots)
        {
            if (FsHelpers.SamePath(path, root))
            {
                return true;
            }
            var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// After an in-place rename of a result row, rebuilds that row — and, for
    /// a renamed folder, every result beneath it — with the new path, keeping
    /// the pane in search mode. Returns the replacement row for the renamed
    /// item (null when the pane is not in search mode).
    /// </summary>
    private FileItem? ReplaceSearchResultAfterRename(Pane pane, FileItem renamed, string newFullPath)
    {
        if (!IsPaneInSearchMode(pane))
        {
            return null;
        }

        var items = ItemsOf(pane);
        var root = _subfolderSearchRoot;
        var oldPath = renamed.FullPath;
        var oldPrefix = oldPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        FileItem? replacement = null;

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            string? movedTo = null;
            if (ReferenceEquals(item, renamed) || FsHelpers.SamePath(item.FullPath, oldPath))
            {
                movedTo = newFullPath;
            }
            else if (renamed.IsDirectory && item.FullPath.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
            {
                movedTo = Path.Combine(newFullPath, item.FullPath[oldPrefix.Length..]);
            }
            if (movedTo is null)
            {
                continue;
            }

            try
            {
                FileSystemInfo info = item.IsDirectory ? new DirectoryInfo(movedTo) : new FileInfo(movedTo);
                var rebuilt = BuildSearchResult(info, root);
                items[i] = rebuilt;
                if (ReferenceEquals(item, renamed))
                {
                    replacement = rebuilt;
                }
            }
            catch
            {
                // Metadata for the new path could not be read (e.g. it vanished
                // right after the rename); leave the stale row rather than
                // dropping it silently.
            }
        }

        return replacement;
    }

    /// <summary>
    /// Refreshes both panes after a shell copy / move finished. A pane in
    /// search mode either leaves it (the destination is the searched folder
    /// itself, so the new items must be shown) or prunes rows that moved away.
    /// </summary>
    private void RefreshPanesAfterShellOperation(string[] sources, string destination)
    {
        foreach (var pane in new[] { Pane.Left, Pane.Right })
        {
            if (IsPaneInSearchMode(pane))
            {
                if (FsHelpers.SamePath(destination, _subfolderSearchRoot))
                {
                    LeaveSubfolderSearch();
                }
                else
                {
                    PruneSearchResults(pane, sources);
                }
            }
            else
            {
                _ = ReloadDiffAsync(GridOf(pane));
            }
        }
    }

    /// <summary>
    /// Leaves search mode: stops an in-flight walk and drops the "results
    /// shown" state so the status line returns to the normal folder summary.
    /// </summary>
    private void CancelSubfolderSearch()
    {
        _subfolderSearchCts?.Cancel();
        _subfolderSearchCts = null;
        _subfolderSearchActive = false;
        _subfolderSearchShown = false;
        _subfolderSearchProgress = null;
        StopSearchStatusTimer();
    }
}
