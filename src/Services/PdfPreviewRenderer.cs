using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;

namespace Tfx;

/// <summary>
/// Bundle of preview-time security knobs read from <see cref="AppSettings"/>.
/// Passed explicitly so unit tests / future contexts can construct one without
/// pulling in the whole settings file.
/// </summary>
internal sealed record PdfPreviewOptions(
    string UserRendererPath,
    bool AllowShellThumbnailGenerate,
    long MaxRendererMemoryBytes = 256L * 1024 * 1024);

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

    public static BitmapSource? TryRenderFirstPage(
        string path,
        int size,
        PdfPreviewOptions options,
        CancellationToken cancellationToken,
        out string? error)
    {
        error = null;
        cancellationToken.ThrowIfCancellationRequested();

        var cacheKey = BuildCacheKey(path, size);
        if (cacheKey is not null && TryGetCached(cacheKey, out var cached))
        {
            return cached;
        }

        // 0. Persistent disk cache: a PNG of the rendered first page that
        // tfx itself wrote on a previous run. Survives app restarts, so the
        // second visit to a PDF is essentially free even after a relaunch.
        if (cacheKey is not null && TryLoadFromDiskCache(cacheKey) is { } onDisk)
        {
            StoreInCache(cacheKey, onDisk);
            return onDisk;
        }

        // 1. Fast path: the OS already has a thumbnail cached.
        // `cacheOnly: true` never loads a third-party PDF thumbnailer
        // in-process, so this stage is always safe to run.
        var shellCached = ShellThumbnail.TryGetThumbnail(path, size, cacheOnly: true);
        if (shellCached is not null)
        {
            StoreInCache(cacheKey, shellCached);
            return shellCached;
        }

        cancellationToken.ThrowIfCancellationRequested();

        // 2. External pdftoppm — preferred when installed (Poppler/Calibre/
        // Scoop/Chocolatey/Conda or a user-pinned path). Runs in a separate
        // Job-Object-contained process so renderer crashes / bugs are
        // isolated from tfx. We try this BEFORE the in-process Windows
        // renderer so users who deliberately install pdftoppm get its
        // output, which is often more faithful for complex PDFs.
        var pdftoppm = FindPdftoppm(options.UserRendererPath);
        if (pdftoppm is not null)
        {
            var rendered = TryRenderWithPdftoppm(pdftoppm, path, size, options, cancellationToken, out var pdftoppmError);
            if (rendered is not null)
            {
                StoreInCache(cacheKey, rendered);
                return rendered;
            }
            if (!string.IsNullOrEmpty(pdftoppmError))
            {
                error = pdftoppmError;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        // 3. Windows.Data.Pdf — fallback when no external pdftoppm is
        // installed. OS-shipped PDFium-derived renderer, in-process but
        // Microsoft-maintained. Renders at requested resolution and reads
        // file bytes directly so OneDrive / Google Drive virtual files work.
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            var winrt = WinRtPdfRenderer.TryRenderFirstPage(path, (uint)size, cancellationToken, out var winrtError);
            if (winrt is not null)
            {
                StoreInCache(cacheKey, winrt);
                return winrt;
            }
            if (!string.IsNullOrEmpty(winrtError))
            {
                error = $"Windows.Data.Pdf: {winrtError}";
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        // 4. Last resort: ask the shell to generate a thumbnail. This
        // can load arbitrary registered PDF thumbnailers (Adobe / Foxit /
        // Edge / ...) inside the tfx process and is therefore opt-in.
        if (options.AllowShellThumbnailGenerate)
        {
            var generated = ShellThumbnail.TryGetThumbnail(path, size, cacheOnly: false, out var shellError);
            if (generated is not null)
            {
                error = null;
                StoreInCache(cacheKey, generated);
                return generated;
            }
            // Surface the underlying shell-extension failure (HRESULT or
            // exception text) instead of the generic "no provider" message.
            if (!string.IsNullOrEmpty(shellError))
            {
                // 0x8004B2xx is the Windows Cloud Files API error range —
                // OneDrive / Google Drive virtual files where the shell
                // thumbnailer can't (or won't) reach into the cloud
                // provider's storage. Surface a hint so the user knows it
                // isn't a tfx bug they need to chase.
                if (shellError.Contains("0x8004B2", StringComparison.OrdinalIgnoreCase))
                {
                    error = $"shell thumbnail: {shellError} (cloud-synced file — mark it \"Always keep on this device\" or set PdfRendererPath to a local pdftoppm.exe)";
                }
                else
                {
                    error = $"shell thumbnail: {shellError}";
                }
                return null;
            }
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
        // Also persist to disk (fire-and-forget so render latency isn't
        // affected). Subsequent runs can short-circuit the renderer entirely.
        _ = Task.Run(() => SaveToDiskCache(key, image));
    }

    // ─── Persistent disk cache ────────────────────────────────────────────
    //
    // Cache file layout:
    //   %LocalAppData%\tfx\pdf-cache\<sha256-of-cache-key>.png
    //
    // The cache key already encodes path + mtime + length + render size, so a
    // file edit (mtime / length change) naturally produces a new disk entry.
    // Stale entries are bounded by DiskCacheMaxFiles via best-effort LRU
    // eviction on each save.

    private const int DiskCacheMaxFiles = 200;
    private static readonly object DiskCacheLock = new();

    private static string DiskCacheDirectory()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "tfx", "pdf-cache");
        Directory.CreateDirectory(root);
        return root;
    }

    private static string DiskCacheFilePath(string cacheKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(cacheKey));
        var hex = Convert.ToHexString(hash);
        return Path.Combine(DiskCacheDirectory(), hex + ".png");
    }

    private static BitmapSource? TryLoadFromDiskCache(string cacheKey)
    {
        try
        {
            var path = DiskCacheFilePath(cacheKey);
            if (!File.Exists(path))
            {
                return null;
            }
            // Touch mtime so LRU eviction prefers truly cold entries.
            try { File.SetLastWriteTimeUtc(path, DateTime.UtcNow); } catch { }

            var bytes = File.ReadAllBytes(path);
            using var ms = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = ms;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static void SaveToDiskCache(string cacheKey, BitmapSource image)
    {
        try
        {
            var path = DiskCacheFilePath(cacheKey);
            // Encode to PNG via WPF — runs off the UI thread because the
            // BitmapSource is frozen (see Freeze() at every renderer site).
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            // Write to a tmp file then rename so a concurrent reader never
            // sees a half-written PNG.
            var tmp = path + ".tmp-" + Guid.NewGuid().ToString("N")[..8];
            using (var fs = File.Create(tmp))
            {
                encoder.Save(fs);
            }
            try
            {
                File.Move(tmp, path, overwrite: true);
            }
            catch
            {
                try { File.Delete(tmp); } catch { }
            }

            // Best-effort LRU trim: if we have too many cached PNGs, drop
            // the oldest ones by LastWriteTime. Cheap O(n log n) once in a
            // while is fine for this cache size.
            EvictDiskCacheIfNeeded();
        }
        catch
        {
        }
    }

    private static void EvictDiskCacheIfNeeded()
    {
        lock (DiskCacheLock)
        {
            try
            {
                var dir = DiskCacheDirectory();
                var files = Directory.EnumerateFiles(dir, "*.png").ToArray();
                if (files.Length <= DiskCacheMaxFiles)
                {
                    return;
                }
                var infos = files.Select(f => new FileInfo(f))
                                 .OrderBy(fi => fi.LastWriteTimeUtc)
                                 .ToArray();
                var toRemove = infos.Length - DiskCacheMaxFiles;
                for (var i = 0; i < toRemove; i++)
                {
                    try { infos[i].Delete(); } catch { }
                }
            }
            catch
            {
            }
        }
    }

    private static BitmapSource? TryRenderWithPdftoppm(
        string executable,
        string path,
        int size,
        PdfPreviewOptions options,
        CancellationToken cancellationToken,
        out string? error)
    {
        error = null;
        var tempDirectory = Path.Combine(Path.GetTempPath(), "tfx-pdf-preview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var outputBase = Path.Combine(tempDirectory, "page");
        var outputPng = outputBase + ".png";

        JobObject? job = null;
        try
        {
            // Contain pdftoppm in a Job Object: memory cap, no child
            // processes that survive tfx, no escape via spawn-and-exit.
            try { job = new JobObject(options.MaxRendererMemoryBytes); }
            catch { /* Job-object setup is best-effort — preview still runs without it */ }

            using var process = new Process();
            process.StartInfo.FileName = executable;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.RedirectStandardOutput = true;
            // Keep the working directory inside the per-render temp dir so
            // that any "side-channel" file writes by the renderer land in a
            // location we already clean up on exit.
            process.StartInfo.WorkingDirectory = tempDirectory;
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

            // Brief race between Start() and Assign(): we accept the
            // microsecond-scale window — see JobObject summary for rationale.
            job?.Assign(process);

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
            // Disposing the job kills anything still running in it (defense
            // in depth — the Kill call above already targets the same tree).
            job?.Dispose();
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

    // Memoised result of FindPdftoppm so we don't walk %PATH% and run
    // FileVersionInfo on up to 9 candidates on every single PDF render.
    // Keyed by user-configured path so a settings change re-evaluates;
    // first lookup may take 10–100 ms, subsequent ones return immediately.
    private static readonly object FindPdftoppmCacheLock = new();
    private static string? _cachedFindPdftoppmKey;
    private static string? _cachedFindPdftoppmValue;

    /// <summary>
    /// Returns the first trustworthy <c>pdftoppm.exe</c>:
    /// <list type="number">
    ///   <item>the user-configured absolute path
    ///         (<see cref="PdfPreviewOptions.UserRendererPath"/>), if set;</item>
    ///   <item>a <c>pdftoppm.exe</c> on <c>%PATH%</c> whose
    ///         <see cref="FileVersionInfo"/> identifies it as Poppler / Xpdf /
    ///         pdftoppm;</item>
    ///   <item>common install locations for Calibre, the poppler-windows
    ///         standalone build, Scoop, Chocolatey, and Conda — each subject
    ///         to the same vendor check.</item>
    /// </list>
    /// <para>
    /// PATH lookup is gated by the vendor check, so a plain
    /// <c>pdftoppm.exe</c> dropped in a writable PATH entry without the right
    /// metadata strings would be rejected.
    /// </para>
    /// </summary>
    private static string? FindPdftoppm(string userConfiguredPath)
    {
        var key = userConfiguredPath ?? string.Empty;
        lock (FindPdftoppmCacheLock)
        {
            if (_cachedFindPdftoppmKey == key)
            {
                return _cachedFindPdftoppmValue;
            }
        }
        var result = FindPdftoppmUncached(userConfiguredPath);
        lock (FindPdftoppmCacheLock)
        {
            _cachedFindPdftoppmKey = key;
            _cachedFindPdftoppmValue = result;
        }
        return result;
    }

    private static string? FindPdftoppmUncached(string? userConfiguredPath)
    {
        if (!string.IsNullOrWhiteSpace(userConfiguredPath))
        {
            var expanded = Environment.ExpandEnvironmentVariables(userConfiguredPath);
            if (File.Exists(expanded) && LooksLikePoppler(expanded))
            {
                return expanded;
            }
        }

        var pathHit = FindOnPath("pdftoppm.exe");
        if (pathHit is not null && LooksLikePoppler(pathHit))
        {
            return pathHit;
        }

        foreach (var candidate in CommonPdftoppmPaths())
        {
            if (File.Exists(candidate) && LooksLikePoppler(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? FindOnPath(string executable)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathVar))
        {
            return null;
        }
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), executable);
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

    /// <summary>
    /// Well-known absolute install locations of pdftoppm-shipping packages.
    /// Each candidate is still vendor-checked before we run it.
    /// </summary>
    private static IEnumerable<string> CommonPdftoppmPaths()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Calibre (bundles Poppler)
        if (!string.IsNullOrEmpty(programFiles))
        {
            yield return Path.Combine(programFiles, "Calibre2", "app", "bin", "pdftoppm.exe");
        }
        if (!string.IsNullOrEmpty(programFilesX86))
        {
            yield return Path.Combine(programFilesX86, "Calibre2", "app", "bin", "pdftoppm.exe");
        }

        // poppler-windows standalone (typical layouts)
        if (!string.IsNullOrEmpty(programFiles))
        {
            yield return Path.Combine(programFiles, "poppler", "bin", "pdftoppm.exe");
            yield return Path.Combine(programFiles, "poppler", "Library", "bin", "pdftoppm.exe");
        }
        if (!string.IsNullOrEmpty(localAppData))
        {
            yield return Path.Combine(localAppData, "Programs", "poppler", "bin", "pdftoppm.exe");
            yield return Path.Combine(localAppData, "Programs", "poppler", "Library", "bin", "pdftoppm.exe");
        }

        // Scoop (per-user)
        if (!string.IsNullOrEmpty(userProfile))
        {
            yield return Path.Combine(userProfile, "scoop", "apps", "poppler", "current", "bin", "pdftoppm.exe");
            yield return Path.Combine(userProfile, "scoop", "apps", "poppler", "current", "Library", "bin", "pdftoppm.exe");
        }

        // Chocolatey
        yield return @"C:\ProgramData\chocolatey\bin\pdftoppm.exe";

        // Miniforge / Anaconda (per-user default install)
        if (!string.IsNullOrEmpty(userProfile))
        {
            yield return Path.Combine(userProfile, "miniforge3", "Library", "bin", "pdftoppm.exe");
            yield return Path.Combine(userProfile, "anaconda3", "Library", "bin", "pdftoppm.exe");
        }
    }

    /// <summary>
    /// Cheap vendor / product sanity check via <see cref="FileVersionInfo"/>.
    /// Rejects executables whose CompanyName / ProductName don't match the
    /// known strings shipped by Poppler / Calibre / Xpdf builds. This catches
    /// the easy spoofing case ("attacker.exe" renamed to "pdftoppm.exe"); it
    /// does not catch a truly malicious binary that copies these strings.
    /// </summary>
    private static bool LooksLikePoppler(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            var meta = string.Concat(
                info.CompanyName ?? "",
                "|", info.ProductName ?? "",
                "|", info.FileDescription ?? "",
                "|", info.InternalName ?? "",
                "|", info.OriginalFilename ?? "");
            // Accept any of: Poppler / Xpdf / pdftoppm (Calibre bundles ship
            // mixed metadata depending on the version they were built from).
            return meta.Contains("poppler", StringComparison.OrdinalIgnoreCase)
                || meta.Contains("pdftoppm", StringComparison.OrdinalIgnoreCase)
                || meta.Contains("xpdf",    StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

}
