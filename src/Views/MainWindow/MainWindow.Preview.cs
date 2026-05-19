using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Markdig;
using Path = System.IO.Path;

namespace Tfx;

public partial class MainWindow
{
    private static readonly MarkdownPipeline MarkdownPipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
    private static readonly TimeSpan PreviewDebounce = TimeSpan.FromMilliseconds(120);

    private FileItem? _previewItem;
    private Task<bool>? _webViewInitTask;
    private DispatcherTimer? _previewDebounceTimer;
    private FileItem? _pendingPreviewItem;

    private void SchedulePreviewUpdate(FileItem? item)
    {
        _pendingPreviewItem = item;
        if (_previewDebounceTimer is null)
        {
            _previewDebounceTimer = new DispatcherTimer { Interval = PreviewDebounce };
            _previewDebounceTimer.Tick += (_, _) =>
            {
                _previewDebounceTimer!.Stop();
                UpdatePreview(_pendingPreviewItem);
            };
        }
        _previewDebounceTimer.Stop();
        _previewDebounceTimer.Start();
    }

    private async void UpdatePreview(FileItem? item)
    {
        _previewItem = item;
        var cts = ReplacePreviewToken();
        ImagePreview.Visibility = Visibility.Collapsed;
        TextPreview.Visibility = Visibility.Collapsed;
        TextPreview.Text = "";
        ImagePreview.Source = null;
        HideHtmlPreview();
        HideCsvPreview();
        UpdateRenderedToggleVisibility(item);

        if (item is null)
        {
            InfoPreview.Text = "";
            return;
        }

        InfoPreview.Text = $"{item.Name}\n{item.FullPath}\n{item.Kind}\n{item.SizeText}\n{Loc.F("Modified: {0}", item.ModifiedText)}\n{Loc.F("Created: {0}", item.CreatedText)}\n{Loc.F("Owner: {0}", item.OwnerText)}\n{Loc.F("Attributes: {0}", item.AttributeText)}";

        if (item.IsDirectory || item.IsParent)
        {
            return;
        }

        // Skip the File.Exists check on the UI thread: it can stall on slow
        // network shares. PreviewLoader will throw if the file actually went
        // away, and the catch below surfaces the error.

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
        extension is ".md" or ".html" or ".htm" or ".csv" or ".tsv" or ".json";

    private static bool IsHtmlLike(string extension) =>
        extension is ".md" or ".html" or ".htm";

    private static bool IsCsvLike(string extension) =>
        extension is ".csv" or ".tsv";

    private void UpdateRenderedToggleVisibility(FileItem? item)
    {
        // Decide visibility from the FileItem alone — never call File.Exists
        // here. The item came from a recent directory enumeration and may sit
        // on a slow network share; the toggle is harmless even if the file
        // has since been removed.
        var visible = item is { IsDirectory: false, IsParent: false } &&
                      IsRenderable(Path.GetExtension(item.FullPath).ToLowerInvariant());
        RenderedToggle.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        RenderedToggle.IsChecked = _settings.RenderMarkdownHtml;
    }

    private async Task ShowRenderedAsync(string path, string extension, string text, CancellationTokenSource cts)
    {
        if (IsCsvLike(extension))
        {
            await ShowCsvPreviewAsync(text, extension, cts);
            return;
        }

        if (extension == ".json")
        {
            string? pretty;
            try
            {
                pretty = await Task.Run(() => JsonPrettyPrinter.TryPrettyPrint(text), cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            if (cts.IsCancellationRequested)
            {
                return;
            }
            TextPreview.Text = pretty ?? text;
            TextPreview.Visibility = Visibility.Visible;
            return;
        }

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
                string fullHtml;
                try
                {
                    fullHtml = await Task.Run(
                        () => BuildMarkdownDocument(Markdown.ToHtml(text, MarkdownPipeline)),
                        cts.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                if (cts.IsCancellationRequested)
                {
                    return;
                }
                HtmlPreview.NavigateToString(fullHtml);
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

    private const int CsvPreviewRowCap = 2000;
    private const int CsvPreviewColumnCap = 64;

    private async Task ShowCsvPreviewAsync(string text, string extension, CancellationTokenSource cts)
    {
        try
        {
            var delimiter = CsvParser.DetectDelimiter(extension);
            var (header, dataRows, totalDataRows) = await Task.Run(() =>
            {
                var rows = CsvParser.Parse(text, delimiter);
                if (rows.Count == 0)
                {
                    return (Header: (IReadOnlyList<string>)Array.Empty<string>(),
                            Rows: (IReadOnlyList<string[]>)Array.Empty<string[]>(),
                            Total: 0);
                }
                var headerRow = rows[0];
                var take = Math.Min(rows.Count - 1, CsvPreviewRowCap);
                var sampled = new string[take][];
                for (var i = 0; i < take; i++)
                {
                    sampled[i] = rows[i + 1].ToArray();
                }
                return (Header: (IReadOnlyList<string>)headerRow,
                        Rows: (IReadOnlyList<string[]>)sampled,
                        Total: rows.Count - 1);
            }, cts.Token);

            if (cts.IsCancellationRequested)
            {
                return;
            }

            CsvPreview.Columns.Clear();
            if (header.Count == 0)
            {
                CsvPreview.ItemsSource = null;
                PreviewScroll.Visibility = Visibility.Collapsed;
                CsvPreview.Visibility = Visibility.Visible;
                return;
            }

            var columnCount = Math.Min(header.Count, CsvPreviewColumnCap);
            for (var i = 0; i < columnCount; i++)
            {
                CsvPreview.Columns.Add(new DataGridTextColumn
                {
                    Header = header[i],
                    Binding = new Binding($"[{i}]"),
                });
            }

            CsvPreview.ItemsSource = dataRows;
            PreviewScroll.Visibility = Visibility.Collapsed;
            CsvPreview.Visibility = Visibility.Visible;

            if (totalDataRows > dataRows.Count || header.Count > columnCount)
            {
                var msg = totalDataRows > dataRows.Count
                    ? Loc.F("Showing {0} of {1} rows", dataRows.Count, totalDataRows)
                    : "";
                if (header.Count > columnCount)
                {
                    msg = string.IsNullOrEmpty(msg)
                        ? Loc.F("Showing {0} of {1} columns", columnCount, header.Count)
                        : msg + " / " + Loc.F("Showing {0} of {1} columns", columnCount, header.Count);
                }
                InfoPreview.Text += $"\n{msg}";
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            TextPreview.Text = text;
            TextPreview.Visibility = Visibility.Visible;
            InfoPreview.Text += $"\n{Loc.F("Preview error: {0}", ex.Message)}";
        }
    }

    private void HideCsvPreview()
    {
        CsvPreview.Visibility = Visibility.Collapsed;
        CsvPreview.ItemsSource = null;
        CsvPreview.Columns.Clear();
        if (HtmlPreview.Visibility == Visibility.Collapsed)
        {
            PreviewScroll.Visibility = Visibility.Visible;
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
        var wasVisible = HtmlPreview.Visibility == Visibility.Visible;
        HtmlPreview.Visibility = Visibility.Collapsed;
        PreviewScroll.Visibility = Visibility.Visible;
        // Only churn WebView2 when it was actually showing something; navigating
        // to about:blank on every selection change otherwise floods the WebView2
        // navigation queue.
        if (wasVisible && HtmlPreview.CoreWebView2 != null)
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

    private void Preview_Click(object sender, RoutedEventArgs e) => TogglePreview();

    private void TogglePreview()
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
