using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Path = System.IO.Path;

namespace Tfx;

public partial class MainWindow
{
    private const int SubfolderSearchBatch = 50;
    private static readonly TimeSpan SubfolderStatusInterval = TimeSpan.FromMilliseconds(120);

    private CancellationTokenSource? _subfolderSearchCts;
    private bool _subfolderSearchActive;
    private Pane _subfolderSearchPane;

    private void FocusSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SearchBox.Text = "";
            CancelSubfolderSearch();
            Reload(LeftGrid);
            Reload(RightGrid);
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
        if (!string.IsNullOrEmpty(SearchBox.Text) || Keyboard.Modifiers != ModifierKeys.None)
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
        _subfolderSearchCts = cts;
        _subfolderSearchActive = true;
        _subfolderSearchPane = pane;
        var target = ItemsOf(pane);
        target.Clear();

        var showHidden = ShowHidden;
        SetStatus(Loc.F("Searching {0}...", query));

        _ = RunSubfolderSearchAsync(target, root, query, showHidden, cts);
    }

    private async Task RunSubfolderSearchAsync(
        System.Collections.ObjectModel.ObservableCollection<FileItem> target,
        string root,
        string query,
        bool showHidden,
        CancellationTokenSource cts)
    {
        var token = cts.Token;
        var matches = 0;
        var lastStatusAt = DateTime.UtcNow;
        var batch = new List<FileItem>();

        try
        {
            await foreach (var item in EnumerateMatchesAsync(root, query, showHidden, token))
            {
                token.ThrowIfCancellationRequested();
                batch.Add(item);
                matches++;
                if (batch.Count >= SubfolderSearchBatch)
                {
                    await FlushBatchAsync(target, batch, matches, token);
                    batch = new List<FileItem>();
                    lastStatusAt = DateTime.UtcNow;
                }
                else if (DateTime.UtcNow - lastStatusAt >= SubfolderStatusInterval)
                {
                    var current = matches;
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (token.IsCancellationRequested) return;
                        SetStatus(Loc.F("Searching: {0} matches", current));
                    });
                    lastStatusAt = DateTime.UtcNow;
                }
            }

            if (batch.Count > 0)
            {
                await FlushBatchAsync(target, batch, matches, token);
            }

            await Dispatcher.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested) return;
                SetStatus(Loc.F("Search complete: {0} matches", matches));
            });
        }
        catch (OperationCanceledException)
        {
            // Cancellation: leave whatever results we already streamed.
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() => SetStatus(ex.Message));
        }
        finally
        {
            if (ReferenceEquals(_subfolderSearchCts, cts))
            {
                _subfolderSearchCts = null;
                _subfolderSearchActive = false;
            }
            cts.Dispose();
        }
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
            SetStatus(Loc.F("Searching: {0} matches", totalMatches));
        });
    }

    private static async IAsyncEnumerable<FileItem> EnumerateMatchesAsync(
        string root,
        string query,
        bool showHidden,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
    {
        var channel = System.Threading.Channels.Channel.CreateUnbounded<FileItem>(
            new System.Threading.Channels.UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

        _ = Task.Run(() =>
        {
            try
            {
                Walk(root, root, query, showHidden, channel.Writer, token);
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
        string current,
        string root,
        string query,
        bool showHidden,
        System.Threading.Channels.ChannelWriter<FileItem> writer,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        IEnumerable<string> dirs;
        IEnumerable<string> files;
        try
        {
            dirs = FsHelpers.SafeEnumerateDirectories(current);
            files = FsHelpers.SafeEnumerateFiles(current);
        }
        catch
        {
            return;
        }

        foreach (var dir in dirs)
        {
            token.ThrowIfCancellationRequested();
            if (!showHidden && FsHelpers.IsHidden(dir))
            {
                continue;
            }
            var name = Path.GetFileName(dir);
            if (!string.IsNullOrEmpty(name) && name.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                writer.TryWrite(BuildSearchResult(dir, root, isDirectory: true));
            }
            Walk(dir, root, query, showHidden, writer, token);
        }

        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            if (!showHidden && FsHelpers.IsHidden(file))
            {
                continue;
            }
            var name = Path.GetFileName(file);
            if (!string.IsNullOrEmpty(name) && name.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                writer.TryWrite(BuildSearchResult(file, root, isDirectory: false));
            }
        }
    }

    private static FileItem BuildSearchResult(string fullPath, string root, bool isDirectory)
    {
        var rel = MakeRelative(root, fullPath);
        var item = isDirectory
            ? FileItem.FromDirectory(fullPath, loadSmallIcon: true, loadLargeIcon: false, includeOwner: false)
            : FileItem.FromFile(fullPath, loadSmallIcon: true, loadLargeIcon: false, includeOwner: false);

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

    private void CancelSubfolderSearch()
    {
        _subfolderSearchCts?.Cancel();
        _subfolderSearchCts = null;
        _subfolderSearchActive = false;
    }
}
