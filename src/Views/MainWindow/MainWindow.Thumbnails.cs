using System.IO;
using System.Threading;
using System.Windows;
using Path = System.IO.Path;

namespace Tfx;

public partial class MainWindow
{
    // At most two shell-thumbnail extractions in flight: GetImage without
    // InCacheOnly can decode the source file, and an unthrottled burst while
    // scrolling a photo folder would saturate the thread pool and the disk.
    private static readonly SemaphoreSlim ThumbnailGate = new(2);

    // FIFO of items currently holding a decoded thumbnail. Caps memory on
    // huge photo folders (96px BGRA ≈ 36 KB each): the oldest thumbnails are
    // dropped and simply reload if the user scrolls back to them.
    private readonly Queue<FileItem> _thumbnailCache = new();
    private const int ThumbnailCacheCap = 512;
    private const int ThumbnailPixelSize = 96;

    /// <summary>
    /// Extensions worth asking the shell for. The request uses ThumbnailOnly,
    /// so anything without a real thumbnail (no provider installed, an Office
    /// file saved without its preview picture) cleanly falls back to the font
    /// glyph — the list just avoids a pointless COM call per source-code file
    /// in large folders. Video/HEIC/SVG availability depends on the codecs /
    /// shell extensions installed; PDF respects the same safety setting as
    /// the preview pane (PDF shell handlers are third-party code).
    /// </summary>
    private bool CanShowThumbnail(string extension) => extension switch
    {
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".tif" or ".tiff"
            or ".ico" or ".heic" or ".heif" or ".svg" => true,
        ".mp4" or ".m4v" or ".mkv" or ".mov" or ".avi" or ".wmv" or ".webm"
            or ".flv" or ".mpg" or ".mpeg" or ".3gp" => true,
        ".docx" or ".xlsx" or ".pptx" or ".doc" or ".xls" or ".ppt" => true,
        ".pdf" => _settings.AllowShellPdfThumbnail,
        _ => false,
    };

    /// <summary>
    /// Fires each time an icon-view tile is realized (the panel virtualizes,
    /// so this tracks what is actually visible). Queues a thumbnail load for
    /// thumbnail-capable files; everything else keeps its font glyph.
    /// </summary>
    private void IconTile_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FileItem item })
        {
            RequestThumbnail(item);
        }
    }

    private async void RequestThumbnail(FileItem item)
    {
        if (item.ThumbnailRequested || item.IsDirectory || item.IsParent
            || ArchivePath.Contains(item.FullPath)
            || !CanShowThumbnail(Path.GetExtension(item.FullPath).ToLowerInvariant()))
        {
            return;
        }
        item.ThumbnailRequested = true;

        await ThumbnailGate.WaitAsync();
        try
        {
            if (item.Thumbnail is not null)
            {
                return;
            }

            var path = item.FullPath;
            // cacheOnly: false — let the shell generate (and cache) the
            // thumbnail on first sight; TryGetThumbnail freezes the bitmap so
            // it can cross from the pool thread to the UI thread.
            var thumbnail = await Task.Run(() =>
                ShellThumbnail.TryGetThumbnail(path, ThumbnailPixelSize, cacheOnly: false));
            if (thumbnail is null)
            {
                return;
            }

            item.Thumbnail = thumbnail;
            _thumbnailCache.Enqueue(item);
            while (_thumbnailCache.Count > ThumbnailCacheCap)
            {
                var evicted = _thumbnailCache.Dequeue();
                evicted.Thumbnail = null;
                evicted.ThumbnailRequested = false;
            }
        }
        finally
        {
            ThumbnailGate.Release();
        }
    }
}
