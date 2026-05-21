using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;

namespace Tfx;

internal static class PdfPreviewRenderer
{
    // ─── In-process LRU cache ─────────────────────────────────────────────
    //
    // Repeated visits to the same PDF (arrow-key navigation, toggling back
    // and forth, multi-select preview restoring focus) all re-rendered from
    // scratch. The cache key is (path, last-write time, file length, size)
    // so external edits invalidate the entry naturally.

    private const int CacheCapacity = 10;
    private static readonly object CacheLock = new();
    private static readonly LinkedList<CacheEntry> CacheLruOrder = new();
    private static readonly Dictionary<string, LinkedListNode<CacheEntry>> CacheIndex =
        new(StringComparer.OrdinalIgnoreCase);

    private sealed record CacheEntry(string Key, BitmapSource Image);

    public static BitmapSource? TryRenderFirstPage(string path, int size, CancellationToken cancellationToken, out string? error)
    {
        error = null;
        cancellationToken.ThrowIfCancellationRequested();

        var cacheKey = BuildCacheKey(path, size);
        if (cacheKey is not null && TryGetCached(cacheKey, out var cached))
        {
            return cached;
        }

        // 1. Fast path: the OS already has a thumbnail cached.
        var shellCached = ShellThumbnail.TryGetThumbnail(path, size, cacheOnly: true);
        if (shellCached is not null)
        {
            StoreInCache(cacheKey, shellCached);
            return shellCached;
        }

        cancellationToken.ThrowIfCancellationRequested();

        // 2. pdftoppm if available — high-quality first-page render.
        var pdftoppm = FindPdftoppm();
        if (pdftoppm is not null)
        {
            var rendered = TryRenderWithPdftoppm(pdftoppm, path, size, cancellationToken, out error);
            if (rendered is not null)
            {
                StoreInCache(cacheKey, rendered);
                return rendered;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        // 3. Last resort: ask the shell to generate a thumbnail.
        var generated = ShellThumbnail.TryGetThumbnail(path, size, cacheOnly: false);
        if (generated is not null)
        {
            error = null;
            StoreInCache(cacheKey, generated);
            return generated;
        }

        error ??= Loc.T("No PDF renderer or thumbnail provider is available.");
        return null;
    }

    private static string? BuildCacheKey(string path, int size)
    {
        try
        {
            var info = new FileInfo(path);
            return $"{path}|{info.LastWriteTimeUtc.Ticks}|{info.Length}|{size}";
        }
        catch
        {
            return null;
        }
    }

    private static bool TryGetCached(string key, out BitmapSource image)
    {
        lock (CacheLock)
        {
            if (CacheIndex.TryGetValue(key, out var node))
            {
                CacheLruOrder.Remove(node);
                CacheLruOrder.AddFirst(node);
                image = node.Value.Image;
                return true;
            }
        }
        image = null!;
        return false;
    }

    private static void StoreInCache(string? key, BitmapSource image)
    {
        if (key is null)
        {
            return;
        }
        lock (CacheLock)
        {
            if (CacheIndex.TryGetValue(key, out var existing))
            {
                CacheLruOrder.Remove(existing);
            }
            else if (CacheIndex.Count >= CacheCapacity)
            {
                var last = CacheLruOrder.Last;
                if (last is not null)
                {
                    CacheLruOrder.RemoveLast();
                    CacheIndex.Remove(last.Value.Key);
                }
            }
            var node = new LinkedListNode<CacheEntry>(new CacheEntry(key, image));
            CacheLruOrder.AddFirst(node);
            CacheIndex[key] = node;
        }
    }

    private static BitmapSource? TryRenderWithPdftoppm(string executable, string path, int size, CancellationToken cancellationToken, out string? error)
    {
        error = null;
        var tempDirectory = Path.Combine(Path.GetTempPath(), "tfx-pdf-preview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var outputBase = Path.Combine(tempDirectory, "page");
        var outputPng = outputBase + ".png";

        try
        {
            using var process = new Process();
            process.StartInfo.FileName = executable;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.ArgumentList.Add("-f");
            process.StartInfo.ArgumentList.Add("1");
            process.StartInfo.ArgumentList.Add("-l");
            process.StartInfo.ArgumentList.Add("1");
            process.StartInfo.ArgumentList.Add("-singlefile");
            process.StartInfo.ArgumentList.Add("-png");
            process.StartInfo.ArgumentList.Add("-scale-to");
            process.StartInfo.ArgumentList.Add(size.ToString());
            process.StartInfo.ArgumentList.Add(path);
            process.StartInfo.ArgumentList.Add(outputBase);

            process.Start();

            // Replace the 100ms-step polling loop with an async wait that
            // returns the instant the process exits (no up-to-100ms tail
            // latency) while still honoring cancellation and a hard timeout.
            var waitTask = process.WaitForExitAsync(cancellationToken);
            try
            {
                if (!waitTask.Wait(TimeSpan.FromSeconds(8)))
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                    error = Loc.T("PDF renderer timed out.");
                    return null;
                }
            }
            catch (AggregateException ae) when (ae.InnerException is OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                cancellationToken.ThrowIfCancellationRequested();
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (process.ExitCode != 0 || !File.Exists(outputPng))
            {
                var stderr = process.StandardError.ReadToEnd().Trim();
                error = string.IsNullOrEmpty(stderr) ? Loc.F("PDF renderer exited with code {0}.", process.ExitCode) : stderr;
                return null;
            }

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(outputPng);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    private static string? FindPdftoppm()
    {
        var pathEntry = FindOnPath("pdftoppm.exe");
        if (pathEntry is not null)
        {
            return pathEntry;
        }

        foreach (var candidate in CommonPdftoppmPaths())
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? FindOnPath(string executable)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim(), executable);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
            }
        }

        return null;
    }

    private static IEnumerable<string> CommonPdftoppmPaths()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        if (!string.IsNullOrEmpty(programFiles))
        {
            yield return Path.Combine(programFiles, "Calibre2", "app", "bin", "pdftoppm.exe");
        }

        if (!string.IsNullOrEmpty(programFilesX86))
        {
            yield return Path.Combine(programFilesX86, "Calibre2", "app", "bin", "pdftoppm.exe");
        }
    }
}
