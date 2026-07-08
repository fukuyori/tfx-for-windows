using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Markdig;
using Path = System.IO.Path;

namespace Tfx;

public partial class MainWindow
{
    // DisableHtml(): strip raw HTML / inline scripts / event-handler attributes from
    // Markdown. Without it a malicious .md could drop a <script> or an
    // <img onerror=...> that the rendered WebView2 would execute. We still allow
    // advanced Markdown features (tables, math, etc.).
    private static readonly MarkdownPipeline MarkdownPipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build();
    private static readonly TimeSpan PreviewDebounce = TimeSpan.FromMilliseconds(120);

    private const int MultiSelectionPreviewCap = 8;

    private FileItem? _previewItem;
    // Allows external (https) images for the *current* preview render only. Reset
    // on every UpdatePreview so the permission is never remembered — the user
    // must press the "Load images" button again for each preview, even when
    // re-selecting the same file.
    private bool _allowExternalImagesOnce;
    private Task<bool>? _webViewInitTask;
    private DispatcherTimer? _previewDebounceTimer;
    private IReadOnlyList<FileItem> _pendingPreviewSelection = Array.Empty<FileItem>();

    private void SchedulePreviewUpdate(FileItem? item) =>
        SchedulePreviewUpdate(item is null ? Array.Empty<FileItem>() : new[] { item });

    private void SchedulePreviewUpdate(IEnumerable<FileItem> selection)
    {
        _pendingPreviewSelection = selection.Where(i => !i.IsParent).ToArray();
        if (_previewDebounceTimer is null)
        {
            _previewDebounceTimer = new DispatcherTimer { Interval = PreviewDebounce };
            _previewDebounceTimer.Tick += (_, _) =>
            {
                _previewDebounceTimer!.Stop();
                UpdatePreview(_pendingPreviewSelection);
            };
        }
        _previewDebounceTimer.Stop();
        _previewDebounceTimer.Start();
    }

    private PreviewSecurityOptions BuildPreviewSecurityOptions() => new(
        EnablePdfPreview: _settings.EnablePdfPreview,
        PdfMaxBytes: _settings.PdfPreviewMaxBytes,
        Pdf: new PdfPreviewOptions(
            UserRendererPath: _settings.PdfRendererPath,
            AllowShellThumbnailGenerate: _settings.AllowShellPdfThumbnail));

    private async void UpdatePreview(IReadOnlyList<FileItem> selection)
    {
        // Capture the token immediately: lambdas and later awaits read it after
        // a newer preview may have disposed `cts` (cts.Token would then throw).
        var cts = ReplacePreviewToken();
        var token = cts.Token;
        // The external-image permission is per-render and never remembered.
        // Reset it here and hide the button; ShowRenderedAsync re-shows the
        // button when it renders HTML-like content. The only way it's on for
        // this render is the one-shot signal from LoadImages_Click.
        _allowExternalImagesOnce = _renderWithExternalImagesNext;
        _renderWithExternalImagesNext = false;
        LoadImagesButton.Visibility = Visibility.Collapsed;
        ImagePreview.Visibility = Visibility.Collapsed;
        TextPreview.Visibility = Visibility.Collapsed;
        TextPreview.Text = "";
        ImagePreview.Source = null;
        HideHtmlPreview();
        HideCsvPreview();

        if (selection.Count == 0)
        {
            _previewItem = null;
            InfoPreview.Text = "";
            UpdateRenderedToggleVisibility(null);
            return;
        }

        if (selection.Count > 1)
        {
            _previewItem = null;
            UpdateRenderedToggleVisibility(null);
            InfoPreview.Text = BuildMultiSelectionSummary(selection);
            return;
        }

        var item = selection[0];
        _previewItem = item;
        UpdateRenderedToggleVisibility(item);

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
            var security = BuildPreviewSecurityOptions();
            var preview = await Task.Run(() => PreviewLoader.Load(path, security, token), token);
            if (token.IsCancellationRequested)
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
                    await ShowRenderedAsync(path, extension, preview.Text ?? "", token);
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

    private static string BuildMultiSelectionSummary(IReadOnlyList<FileItem> selection)
    {
        var totalSize = selection.Where(i => !i.IsDirectory).Sum(i => i.Size);
        var header = Loc.F("{0} items selected ({1})", selection.Count, FileItem.FormatSize(totalSize));

        var sb = new System.Text.StringBuilder();
        sb.Append(header);

        var shown = Math.Min(selection.Count, MultiSelectionPreviewCap);
        for (var i = 0; i < shown; i++)
        {
            var item = selection[i];
            sb.AppendLine();
            sb.Append(item.Name);
            sb.AppendLine();
            sb.Append("  ");
            sb.Append(item.Kind);
            if (!item.IsDirectory && !string.IsNullOrEmpty(item.SizeText))
            {
                sb.Append("  /  ");
                sb.Append(item.SizeText);
            }
            if (!string.IsNullOrEmpty(item.ModifiedText))
            {
                sb.Append("  /  ");
                sb.Append(item.ModifiedText);
            }
        }

        if (selection.Count > shown)
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.Append(Loc.F("(+{0} more)", selection.Count - shown));
        }

        return sb.ToString();
    }

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

    private async Task ShowRenderedAsync(string path, string extension, string text, CancellationToken token)
    {
        if (IsCsvLike(extension))
        {
            await ShowCsvPreviewAsync(text, extension, token);
            return;
        }

        if (extension == ".json")
        {
            string? pretty;
            try
            {
                pretty = await Task.Run(() => JsonPrettyPrinter.TryPrettyPrint(text), token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            if (token.IsCancellationRequested)
            {
                return;
            }
            TextPreview.Text = pretty ?? text;
            TextPreview.Visibility = Visibility.Visible;
            return;
        }

        if (!await EnsureWebViewAsync())
        {
            if (token.IsCancellationRequested)
            {
                return;
            }
            TextPreview.Text = text;
            TextPreview.Visibility = Visibility.Visible;
            InfoPreview.Text += $"\n{Loc.F("WebView2 runtime is unavailable: {0}", "Microsoft Edge WebView2 Runtime")}";
            return;
        }

        if (token.IsCancellationRequested)
        {
            return;
        }

        try
        {
            var baseDir = Path.GetDirectoryName(path) ?? "";
            if (extension == ".md")
            {
                string fullHtml;
                var css = BuildMarkdownCss();
                try
                {
                    fullHtml = await Task.Run(() =>
                    {
                        var bodyHtml = Markdown.ToHtml(text, MarkdownPipeline);
                        if (_allowExternalImagesOnce)
                        {
                            bodyHtml = EmbedLocalImages(bodyHtml, baseDir);
                        }
                        return BuildMarkdownDocument(bodyHtml, css);
                    }, token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                if (token.IsCancellationRequested)
                {
                    return;
                }
                HtmlPreview.NavigateToString(fullHtml);
            }
            else
            {
                // Don't Navigate() to file:// — that would re-enable same-origin
                // file:// fetches inside the page. Load the HTML as a string
                // (script-disabled WebView2 settings, see InitWebViewAsync)
                // instead, with a strict CSP wrapper so external requests and
                // inline scripts can't execute even if WebView2 settings drift.
                // Local <img> sources are resolved and inlined as data: URIs by
                // us (trusted C# code) rather than by relaxing the CSP to allow
                // file: — the WebView itself never gets file:// access.
                var htmlSource = text;
                if (_allowExternalImagesOnce)
                {
                    try
                    {
                        htmlSource = await Task.Run(() => EmbedLocalImages(htmlSource, baseDir), token);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }
                }
                HtmlPreview.NavigateToString(BuildHtmlPreviewDocument(htmlSource));
            }
            PreviewScroll.Visibility = Visibility.Collapsed;
            HtmlPreview.Visibility = Visibility.Visible;
            // Offer the one-shot "load external images" button. When the images
            // are already loaded (button pressed) keep it hidden.
            LoadImagesButton.Visibility = _allowExternalImagesOnce
                ? Visibility.Collapsed
                : Visibility.Visible;
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

    private async Task ShowCsvPreviewAsync(string text, string extension, CancellationToken token)
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
            }, token);

            if (token.IsCancellationRequested)
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
            var env = await EnsureWebView2EnvironmentAsync();
            await HtmlPreview.EnsureCoreWebView2Async(env);
            if (HtmlPreview.CoreWebView2 is { } cw2)
            {
                // Disable JavaScript / form fills / DevTools in the preview
                // WebView2. Markdown rendering doesn't need any of these, and
                // turning them off neutralises an entire class of attacks
                // (XSS in rendered HTML, file:// fetches, drive-by navigation).
                cw2.Settings.IsScriptEnabled = false;
                cw2.Settings.AreDefaultScriptDialogsEnabled = false;
                cw2.Settings.IsWebMessageEnabled = false;
                cw2.Settings.AreDevToolsEnabled = false;
                cw2.Settings.AreHostObjectsAllowed = false;
                cw2.Settings.IsBuiltInErrorPageEnabled = false;
            }
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
                var inputColor = CssColor("TfxInput");
                HtmlPreview.CoreWebView2.NavigateToString($"<html><body style='background:{inputColor};'></body></html>");
            }
            catch
            {
            }
        }
    }

    private string BuildMarkdownCss()
    {
        return $$"""
:root { color-scheme: {{ColorSchemeForCss()}}; }
body { background:{{CssColor("TfxInput")}}; color:{{CssColor("TfxForeground")}}; font-family:'Yu Gothic UI', Consolas, sans-serif; padding:16px; line-height:1.55; word-break:break-word; }
h1,h2,h3,h4,h5,h6 { color:{{CssColor("TfxForeground")}}; border-bottom:1px solid {{CssColor("TfxBorder")}}; padding-bottom:.2em; margin-top:1.2em; }
code { background:{{CssColor("TfxPanel")}}; padding:1px 4px; border-radius:3px; font-family:Consolas, monospace; }
pre { background:{{CssColor("TfxPanel")}}; padding:10px; border-radius:4px; overflow:auto; }
pre code { padding:0; background:transparent; }
a { color:{{CssColor("TfxAccent")}}; }
table { border-collapse:collapse; }
th, td { border:1px solid {{CssColor("TfxBorder")}}; padding:4px 8px; }
blockquote { color:{{CssColor("TfxMuted")}}; border-left:3px solid {{CssColor("TfxBorder")}}; padding-left:10px; margin-left:0; }
hr { border:0; border-top:1px solid {{CssColor("TfxBorder")}}; }
img { max-width:100%; }
""";
    }

    private string BuildMarkdownDocument(string bodyHtml, string css)
    {
        // Defense-in-depth CSP: even though DisableHtml() strips inline scripts
        // and `javascript:` URLs at the Markdig level, set a strict policy in
        // case some future pipeline change re-enables raw HTML. Allows our own
        // inline <style> block but forbids scripts entirely and blocks all
        // network fetches (so a leak-via-image / fetch is impossible).
        var csp = $"default-src 'none'; {ImgSrcDirective()} style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'; frame-ancestors 'none';";
        return $"<!doctype html><html><head><meta charset='utf-8'><meta http-equiv='Content-Security-Policy' content=\"{csp}\"><style>{css}</style></head><body>{bodyHtml}</body></html>";
    }

    /// <summary>
    /// The CSP <c>img-src</c> directive for the preview. Data URIs are always
    /// allowed — this is also what lets local images through, since
    /// <see cref="EmbedLocalImages"/> inlines them as data: URIs before the HTML
    /// ever reaches the WebView; <c>https:</c> is added on top only when the user
    /// pressed "Load images" for the current render (never remembered).
    /// </summary>
    private string ImgSrcDirective() =>
        _allowExternalImagesOnce ? "img-src data: https:;" : "img-src data:;";

    private static readonly Regex ImgSrcRegex =
        new("""(<img\b[^>]*?\bsrc\s*=\s*)("([^"]*)"|'([^']*)')""", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Cap avoids stalling the preview / ballooning memory on an oversized local
    // file; only recognised image extensions are read at all.
    private const long LocalImageMaxBytes = 10 * 1024 * 1024;

    private static readonly HashSet<string> LocalImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".ico", ".svg" };

    /// <summary>
    /// Inlines local (non-http/https/data) &lt;img&gt; sources as data: URIs,
    /// resolved relative to <paramref name="baseDir"/>. Only called for the
    /// current render when the user pressed "Load images" — the WebView itself
    /// never gets file:// access; we read the bytes ourselves in trusted code
    /// and hand the page an already-embedded data: URI.
    /// </summary>
    private static string EmbedLocalImages(string html, string baseDir) =>
        ImgSrcRegex.Replace(html, match =>
        {
            var prefix = match.Groups[1].Value;
            var src = match.Groups[3].Success ? match.Groups[3].Value : match.Groups[4].Value;
            var dataUri = TryLoadLocalImageAsDataUri(src, baseDir);
            if (dataUri is null)
            {
                return match.Value;
            }
            var quote = match.Groups[3].Success ? '"' : '\'';
            return $"{prefix}{quote}{dataUri}{quote}";
        });

    private static string? TryLoadLocalImageAsDataUri(string src, string baseDir)
    {
        if (string.IsNullOrWhiteSpace(src) ||
            src.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            src.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            var decoded = Uri.UnescapeDataString(src);
            string fullPath;
            if (decoded.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                fullPath = new Uri(decoded).LocalPath;
            }
            else if (Path.IsPathRooted(decoded))
            {
                fullPath = decoded;
            }
            else if (!string.IsNullOrEmpty(baseDir))
            {
                fullPath = Path.GetFullPath(Path.Combine(baseDir, decoded));
            }
            else
            {
                return null;
            }

            var extension = Path.GetExtension(fullPath).ToLowerInvariant();
            if (!LocalImageExtensions.Contains(extension) || !File.Exists(fullPath))
            {
                return null;
            }

            var info = new FileInfo(fullPath);
            if (info.Length > LocalImageMaxBytes)
            {
                return null;
            }

            var mime = extension switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                ".webp" => "image/webp",
                ".ico" => "image/x-icon",
                ".svg" => "image/svg+xml",
                _ => "application/octet-stream",
            };
            var bytes = File.ReadAllBytes(fullPath);
            return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
        }
        catch
        {
            return null;
        }
    }

    private string ColorSchemeForCss()
    {
        if (FindResource("TfxInput") is SolidColorBrush brush)
        {
            var luminance = (0.299 * brush.Color.R) + (0.587 * brush.Color.G) + (0.114 * brush.Color.B);
            return luminance > 140 ? "light" : "dark";
        }

        return "dark";
    }

    private string CssColor(string resourceKey)
    {
        if (FindResource(resourceKey) is SolidColorBrush brush)
        {
            return $"#{brush.Color.R:X2}{brush.Color.G:X2}{brush.Color.B:X2}";
        }

        return "#000000";
    }

    /// <summary>
    /// Wraps a user-supplied .html / .htm document with a strict CSP so that any
    /// inline scripts, <c>file://</c> fetches, or third-party resource loads it
    /// tries to perform are blocked. WebView2 also has JavaScript disabled at
    /// the settings level (see <see cref="InitWebViewAsync"/>), so this is a
    /// belt-and-braces measure for the rendered view.
    /// </summary>
    private string BuildHtmlPreviewDocument(string htmlSource)
    {
        var csp = $"default-src 'none'; {ImgSrcDirective()} style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'; frame-ancestors 'none';";
        // Wrap the original document inside our shell — the CSP <meta> in the
        // head we control wins because it appears first.
        return $"<!doctype html><html><head><meta charset='utf-8'><meta http-equiv='Content-Security-Policy' content=\"{csp}\"></head><body>{htmlSource}</body></html>";
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
        ClampPreviewWidth();
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e) => ClampPreviewWidth();

    /// <summary>
    /// Keeps the preview pane inside the window so it never overflows the right
    /// edge — which would clip content and let the WebView2 (a child HWND) cover
    /// the window's right resize grip. Caps the preview column to the space left
    /// after the sidebar and the main pane's minimum width. Safe to run on every
    /// size change: the window is no longer grown when the preview opens, so
    /// there is no window-resize logic to fight, and it only ever shrinks the
    /// column.
    /// </summary>
    private void ClampPreviewWidth()
    {
        if (PreviewColumn.Width.Value <= 0)
        {
            return; // preview hidden
        }

        var available = ActualWidth; // window width (client ≈ window for the borderless chrome)
        if (available <= 0)
        {
            return; // not laid out yet
        }

        var sidebar = SidebarColumn.ActualWidth > 0 ? SidebarColumn.ActualWidth : SidebarColumn.Width.Value;
        var splitters = PreviewSplitterColumn.Width.Value + 5; // preview splitter + sidebar splitter
        var maxPreview = available - sidebar - splitters - MainPaneColumn.MinWidth;
        if (maxPreview < PreviewColumn.MinWidth)
        {
            maxPreview = PreviewColumn.MinWidth;
        }

        if (PreviewColumn.Width.Value > maxPreview)
        {
            PreviewColumn.Width = new GridLength(maxPreview);
        }
    }

    private void Preview_Click(object sender, RoutedEventArgs e) => TogglePreview();

    private void TogglePreview()
    {
        // Fixed window, split width: toggling the preview only shows/hides the
        // preview column. The window position and size never change; the main
        // file pane (the star column) gives up / reclaims the width.
        var willBeVisible = PreviewColumn.Width.Value == 0;
        SetPreviewVisible(willBeVisible);
        SaveSettings();
    }

    private void RenderedToggle_Click(object sender, RoutedEventArgs e)
    {
        _settings.RenderMarkdownHtml = RenderedToggle.IsChecked == true;
        SaveSettings();
        UpdatePreview(_previewItem is null ? Array.Empty<FileItem>() : new[] { _previewItem });
    }

    /// <summary>
    /// Re-renders the current preview with external (https) images allowed, for
    /// this render only. UpdatePreview resets the flag, so selecting any file
    /// (including re-selecting this one) requires pressing the button again.
    /// </summary>
    private void LoadImages_Click(object sender, RoutedEventArgs e)
    {
        if (_previewItem is null)
        {
            return;
        }
        // Re-run the preview pipeline (resets the flag), then turn it on and
        // re-render this item. A direct flag set + re-render is enough because
        // UpdatePreview reads _allowExternalImagesOnce when it builds the CSP.
        _renderWithExternalImagesNext = true;
        UpdatePreview(new[] { _previewItem });
    }

    // One-shot signal from LoadImages_Click to UpdatePreview: render the upcoming
    // (single) preview with external images enabled. Consumed immediately.
    private bool _renderWithExternalImagesNext;
}
