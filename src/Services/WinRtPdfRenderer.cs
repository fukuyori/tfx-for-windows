using System.IO;
using System.Runtime.Versioning;
using System.Windows.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Storage.Streams;

namespace Tfx;

/// <summary>
/// Renders the first page of a PDF using <see cref="PdfDocument"/> — the
/// Windows-shipped PDFium-derived engine that Edge / Reader also drive.
/// <para>
/// Unlike the shell thumbnail path this:
/// <list type="bullet">
///   <item>renders at the requested resolution (no 256-px ceiling),</item>
///   <item>reads file bytes via OS file API, so OneDrive / Google Drive
///         virtual files are fetched on demand and work normally,</item>
///   <item>ships with Windows itself — no extra binary in our distribution,
///         no user-side installation, no third-party PATH lookup.</item>
/// </list>
/// Runs in-process, but the implementation is the OS's own PDF engine — the
/// same code that already runs in Explorer / Edge whenever the user opens a
/// folder containing PDFs.
/// </para>
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
internal static class WinRtPdfRenderer
{
    public static BitmapSource? TryRenderFirstPage(string path, uint size, CancellationToken cancellationToken, out string? error)
    {
        error = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return RenderAsync(path, size, cancellationToken).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AggregateException ae) when (ae.InnerException is OperationCanceledException oce)
        {
            throw oce;
        }
        catch (Exception ex)
        {
            error = $"{ex.GetType().Name}: {ex.Message}";
            return null;
        }
    }

    private static async Task<BitmapSource?> RenderAsync(string path, uint size, CancellationToken ct)
    {
        // Open via FileStream + AsRandomAccessStream rather than
        // StorageFile.GetFileFromPathAsync — the StorageFile path goes through
        // the shell namespace and does enough metadata I/O to add noticeable
        // latency on every PDF preview. The OS still resolves OneDrive virtual
        // files on first read here, so cloud-synced PDFs still work.
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, bufferSize: 4096, useAsync: true);
        using var ras = fs.AsRandomAccessStream();
        var pdf = await PdfDocument.LoadFromStreamAsync(ras).AsTask(ct);
        if (pdf.PageCount == 0)
        {
            return null;
        }
        using var page = pdf.GetPage(0);
        var options = new PdfPageRenderOptions
        {
            DestinationHeight = size,
            // Force opaque white so transparent PDF pages don't render as
            // black/dark against the dark tfx preview pane.
            BackgroundColor = new Windows.UI.Color { A = 0xFF, R = 0xFF, G = 0xFF, B = 0xFF }
        };
        using var raStream = new InMemoryRandomAccessStream();
        await page.RenderToStreamAsync(raStream, options).AsTask(ct);

        // WinRT IRandomAccessStream → byte[] → MemoryStream → BitmapImage.
        // Going through DataReader keeps us off the (deprecated) WindowsRuntime
        // stream-extension helpers, and OnLoad caching means the bitmap is
        // independent of the stream once EndInit returns.
        raStream.Seek(0);
        var streamSize = (uint)raStream.Size;
        using var reader = new DataReader(raStream.GetInputStreamAt(0));
        await reader.LoadAsync(streamSize).AsTask(ct);
        var bytes = new byte[streamSize];
        reader.ReadBytes(bytes);

        using var ms = new MemoryStream(bytes);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = ms;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
