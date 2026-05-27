using System.IO;
using System.Windows.Media.Imaging;
using Path = System.IO.Path;

namespace Tfx;

internal enum PreviewKind
{
    None,
    Image,
    Text
}

internal sealed record PreviewContent(
    PreviewKind Kind,
    BitmapSource? Image,
    string? Text,
    string? ExtraInfo);

/// <summary>
/// Preview-time security knobs for the various format-specific renderers.
/// Currently only PDF needs them; extend as other risky decoders are added.
/// </summary>
internal sealed record PreviewSecurityOptions(
    bool EnablePdfPreview,
    long PdfMaxBytes,
    PdfPreviewOptions Pdf);

internal static class PreviewLoader
{
    public static PreviewContent Load(string path, PreviewSecurityOptions security, CancellationToken cancellationToken)
    {
        using var _ = PerformanceTrace.Begin($"PreviewLoader.Load({Path.GetFileName(path)})");
        cancellationToken.ThrowIfCancellationRequested();
        var extension = Path.GetExtension(path).ToLowerInvariant();

        if (FsHelpers.IsImage(extension))
        {
            var image = LoadImage(path, cancellationToken);
            return new PreviewContent(PreviewKind.Image, image, null, null);
        }

        if (FsHelpers.IsPdf(extension))
        {
            if (!security.EnablePdfPreview)
            {
                return new PreviewContent(PreviewKind.None, null, null, Loc.T("PDF preview is disabled in settings."));
            }
            // Skip oversized files: pdftoppm memory + time grows with the
            // document, and a malicious PDF that primarily exists to trip
            // up the renderer doesn't need to be small either.
            try
            {
                var length = new FileInfo(path).Length;
                if (length > security.PdfMaxBytes)
                {
                    return new PreviewContent(PreviewKind.None, null, null,
                        Loc.F("PDF preview skipped: file is larger than the configured limit ({0} bytes).", security.PdfMaxBytes));
                }
            }
            catch
            {
                // If the size check itself fails (path gone, permission), let
                // the renderer surface its own error rather than guessing.
            }

            // Target 600 px (matches the long-standing tfx default): preview
            // pane is 320–500 px wide. Larger targets meant ~1.8× more pixels
            // for renderers like pdftoppm / Windows.Data.Pdf to rasterize for
            // no visible quality gain (WPF's HighQuality bitmap scaling now
            // handles the upscale gracefully if the preview pane stretches).
            var preview = PdfPreviewRenderer.TryRenderFirstPage(path, 600, security.Pdf, cancellationToken, out var error);
            return preview is null
                ? new PreviewContent(PreviewKind.None, null, null, Loc.F("PDF preview is unavailable: {0}", error ?? ""))
                : new PreviewContent(PreviewKind.Image, preview, null, null);
        }

        if (FsHelpers.IsText(extension, path))
        {
            var preview = TextPreviewReader.Read(path, cancellationToken);
            var info = $"{Loc.F("Encoding: {0}", preview.EncodingName)}\n{Loc.F("Newline: {0}", preview.NewlineName)}";
            return new PreviewContent(PreviewKind.Text, null, preview.Text, info);
        }

        return new PreviewContent(PreviewKind.None, null, null, null);
    }

    private static BitmapSource LoadImage(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path);
        image.DecodePixelWidth = 900;
        image.EndInit();
        image.Freeze();
        cancellationToken.ThrowIfCancellationRequested();
        return image;
    }
}
