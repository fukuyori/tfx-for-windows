using System.IO;
using System.Windows;
using Markdig;
using Path = System.IO.Path;

namespace Tfx;

public partial class MainWindow
{
    private static readonly MarkdownPipeline MarkdownPipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    private FileItem? _previewItem;
    private Task<bool>? _webViewInitTask;

    private async void UpdatePreview(FileItem? item)
    {
        _previewItem = item;
        var cts = ReplacePreviewToken();
        ImagePreview.Visibility = Visibility.Collapsed;
        TextPreview.Visibility = Visibility.Collapsed;
        TextPreview.Text = "";
        ImagePreview.Source = null;
        HideHtmlPreview();
        UpdateRenderedToggleVisibility(item);

        if (item is null)
        {
            InfoPreview.Text = "";
            return;
        }

        InfoPreview.Text = $"{item.Name}\n{item.FullPath}\n{item.Kind}\n{item.SizeText}\n{Loc.F("Modified: {0}", item.ModifiedText)}\n{Loc.F("Created: {0}", item.CreatedText)}\n{Loc.F("Owner: {0}", item.OwnerText)}\n{Loc.F("Attributes: {0}", item.AttributeText)}";

        if (item.IsDirectory || item.IsParent || !File.Exists(item.FullPath))
        {
            return;
        }

        try
        {
            var path = item.FullPath;
            var preview = await Task.Run(() => PreviewLoader.Load(path, cts.Token), cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            if (!string.IsNullOrEmpty(preview.ExtraInfo))
            {
                InfoPreview.Text += $"\n{preview.ExtraInfo}";
            }

            if (preview.Kind == PreviewKind.Image && preview.Image is not null)
            {
                ImagePreview.Source = preview.Image;
                ImagePreview.Visibility = Visibility.Visible;
            }
            else if (preview.Kind == PreviewKind.Text)
            {
                var extension = Path.GetExtension(path).ToLowerInvariant();
                if (_settings.RenderMarkdownHtml && IsRenderable(extension))
                {
                    await ShowRenderedAsync(path, extension, preview.Text ?? "", cts);
                }
                else
                {
                    TextPreview.Text = preview.Text ?? "";
                    TextPreview.Visibility = Visibility.Visible;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            InfoPreview.Text += $"\n{Loc.F("Preview error: {0}", ex.Message)}";
        }
    }

    private static bool IsRenderable(string extension) =>
        extension is ".md" or ".html" or ".htm";

    private void UpdateRenderedToggleVisibility(FileItem? item)
    {
        var visible = item is { IsDirectory: false, IsParent: false } &&
                      File.Exists(item.FullPath) &&
                      IsRenderable(Path.GetExtension(item.FullPath).ToLowerInvariant());
        RenderedToggle.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        RenderedToggle.IsChecked = _settings.RenderMarkdownHtml;
    }

    private async Task ShowRenderedAsync(string path, string extension, string text, CancellationTokenSource cts)
    {
        if (!await EnsureWebViewAsync())
        {
            if (cts.IsCancellationRequested)
            {
                return;
            }
            TextPreview.Text = text;
            TextPreview.Visibility = Visibility.Visible;
            InfoPreview.Text += $"\n{Loc.F("WebView2 runtime is unavailable: {0}", "Microsoft Edge WebView2 Runtime")}";
            return;
        }

        if (cts.IsCancellationRequested)
        {
            return;
        }

        try
        {
            if (extension == ".md")
            {
                var bodyHtml = Markdown.ToHtml(text, MarkdownPipeline);
                HtmlPreview.NavigateToString(BuildMarkdownDocument(bodyHtml));
            }
            else
            {
                HtmlPreview.CoreWebView2.Navigate(new Uri(path).AbsoluteUri);
            }
            PreviewScroll.Visibility = Visibility.Collapsed;
            HtmlPreview.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            TextPreview.Text = text;
            TextPreview.Visibility = Visibility.Visible;
            InfoPreview.Text += $"\n{Loc.F("Preview error: {0}", ex.Message)}";
        }
    }

    private Task<bool> EnsureWebViewAsync()
    {
        if (_webViewInitTask != null)
        {
            return _webViewInitTask;
        }
        _webViewInitTask = InitWebViewAsync();
        return _webViewInitTask;
    }

    private async Task<bool> InitWebViewAsync()
    {
        try
        {
            await HtmlPreview.EnsureCoreWebView2Async();
            return HtmlPreview.CoreWebView2 != null;
        }
        catch
        {
            return false;
        }
    }

    private void HideHtmlPreview()
    {
        HtmlPreview.Visibility = Visibility.Collapsed;
        PreviewScroll.Visibility = Visibility.Visible;
        if (HtmlPreview.CoreWebView2 != null)
        {
            try
            {
                HtmlPreview.CoreWebView2.NavigateToString("<html><body style='background:#0D1013;'></body></html>");
            }
            catch
            {
            }
        }
    }

    private static string BuildMarkdownDocument(string bodyHtml)
    {
        const string css = """
:root { color-scheme: dark; }
body { background:#0D1013; color:#D6D9DD; font-family:'Yu Gothic UI', Consolas, sans-serif; padding:16px; line-height:1.55; word-break:break-word; }
h1,h2,h3,h4,h5,h6 { color:#E8EAED; border-bottom:1px solid #2A2F35; padding-bottom:.2em; margin-top:1.2em; }
code { background:#171B1F; padding:1px 4px; border-radius:3px; font-family:Consolas, monospace; }
pre { background:#171B1F; padding:10px; border-radius:4px; overflow:auto; }
pre code { padding:0; background:transparent; }
a { color:#7CB7FF; }
table { border-collapse:collapse; }
th, td { border:1px solid #2A2F35; padding:4px 8px; }
blockquote { color:#9AA0A6; border-left:3px solid #2A2F35; padding-left:10px; margin-left:0; }
hr { border:0; border-top:1px solid #2A2F35; }
img { max-width:100%; }
""";
        return $"<!doctype html><html><head><meta charset='utf-8'><style>{css}</style></head><body>{bodyHtml}</body></html>";
    }

    private CancellationTokenSource ReplacePreviewToken()
    {
        var next = new CancellationTokenSource();
        _previewCts?.Cancel();
        _previewCts?.Dispose();
        _previewCts = next;
        return next;
    }

    private void SetPreviewVisible(bool visible)
    {
        PreviewSplitterColumn.Width = visible ? new GridLength(5) : new GridLength(0);
        PreviewColumn.MinWidth = visible ? 240 : 0;
        var previewWidth = _settings.PreviewWidth >= 240 ? _settings.PreviewWidth : 320;
        PreviewColumn.Width = visible ? new GridLength(previewWidth) : new GridLength(0);
        PreviewHost.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        PreviewButton.IsChecked = visible;
    }

    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        SetPreviewVisible(PreviewColumn.Width.Value == 0);
        SaveSettings();
    }

    private void RenderedToggle_Click(object sender, RoutedEventArgs e)
    {
        _settings.RenderMarkdownHtml = RenderedToggle.IsChecked == true;
        SaveSettings();
        UpdatePreview(_previewItem);
    }
}
