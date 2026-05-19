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

internal static class PreviewLoader
{
    public static PreviewContent Load(string path, CancellationToken cancellationToken)
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
            var preview = PdfPreviewRenderer.TryRenderFirstPage(path, 1200, cancellationToken, out var error);
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
