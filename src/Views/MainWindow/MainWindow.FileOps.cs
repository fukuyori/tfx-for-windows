using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualBasic.FileIO;
using VbFileSystem = Microsoft.VisualBasic.FileIO.FileSystem;
using Path = System.IO.Path;

namespace Tfx;

public partial class MainWindow
{
    private void OpenItem(DataGrid grid, FileItem item)
    {
        if (item.IsParent)
        {
            var current = GetCurrentPath(grid);
            string selectName;
            if (ArchivePath.TryParse(current, out var archive, out var inner))
            {
                selectName = string.IsNullOrEmpty(inner)
                    ? Path.GetFileName(archive)
                    : (inner.TrimEnd('/').Split('/').LastOrDefault() ?? "");
            }
            else
            {
                selectName = Path.GetFileName(current.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }
            Navigate(grid, item.FullPath, true, selectName);
            return;
        }

        if (item.IsDirectory)
        {
            Navigate(grid, item.FullPath, true, "..");
            return;
        }

        if (ArchivePath.TryParse(item.FullPath, out var archiveFile, out var entryPath))
        {
            try
            {
                var realPath = ArchiveBrowser.ExtractEntryToTemp(archiveFile, entryPath, EnsureArchiveTempRoot(), CancellationToken.None);
                Process.Start(new ProcessStartInfo(realPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
            }
            return;
        }

        if (ArchivePath.IsZipFile(item.FullPath) && File.Exists(item.FullPath))
        {
            Navigate(grid, ArchivePath.Combine(item.FullPath, ""), true, "..");
            return;
        }

        if (TryOpenWithConfiguredApp(item.FullPath))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(item.FullPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private bool TryOpenWithConfiguredApp(string path)
    {
        var extension = AppConfig.NormalizeExtension(Path.GetExtension(path));
        if (extension.Length == 0 || !_config.OpenWith.TryGetValue(extension, out var app) || string.IsNullOrWhiteSpace(app))
        {
            return false;
        }

        try
        {
            var expandedApp = Environment.ExpandEnvironmentVariables(AppConfig.ExpandUserPath(app));
            var safePath = "\"" + path.Replace("\"", "\"\"") + "\"";
            Process.Start(new ProcessStartInfo(expandedApp, safePath) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            SetStatus(Loc.F("Open with failed: {0}", ex.Message));
            return true;
        }
    }

    private string EnsureArchiveTempRoot()
    {
        if (!string.IsNullOrEmpty(_archiveTempRoot))
        {
            return _archiveTempRoot!;
        }
        // Before creating this session's folder, opportunistically sweep
        // leftovers from previous tfx runs that crashed before they could
        // delete their temp folders. Best-effort: anything currently held
        // open by another tfx process is silently skipped.
        try
        {
            var parent = Path.Combine(Path.GetTempPath(), "tfx");
            if (Directory.Exists(parent))
            {
                foreach (var stale in Directory.EnumerateDirectories(parent, "archive-*"))
                {
                    try { Directory.Delete(stale, recursive: true); } catch { }
                }
            }
        }
        catch
        {
        }
        var root = Path.Combine(Path.GetTempPath(), "tfx", "archive-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        _archiveTempRoot = root;
        return root;
    }

    private void CopySelection(bool cut)
    {
        var paths = ActiveSelectedItems().Where(i => !i.IsParent).Select(i => i.FullPath).ToArray();
        if (paths.Length == 0)
        {
            return;
        }

        var collection = new StringCollection();
        collection.AddRange(paths);
        Clipboard.SetFileDropList(collection);
        _cutBuffer = cut ? paths : [];
        SetStatus(cut ? Loc.F("Cut {0} item(s)", paths.Length) : Loc.F("Copied {0} item(s)", paths.Length));
    }

    private void PasteIntoActivePane()
    {
        // Files on the clipboard → copy/move them (the usual case). Otherwise fall
        // back to creating a file from the clipboard content (image / CSV / text).
        if (Clipboard.ContainsFileDropList())
        {
            PasteFilesFromClipboard();
        }
        else
        {
            PasteClipboardContentAsFile();
        }
    }

    private void PasteFilesFromClipboard()
    {
        var destination = GetCurrentPath(_activeGrid);
        var files = Clipboard.GetFileDropList().Cast<string>().ToArray();
        if (files.Length == 0)
        {
            return;
        }

        // Cut + paste = move; plain copy = copy. The clipboard set is uniformly one
        // or the other (set by Cut/Copy), so decide once for the whole batch.
        var move = files.All(f => _cutBuffer.Contains(f, StringComparer.OrdinalIgnoreCase));

        // Skip items already in the destination folder for a move (self-move).
        var sources = files
            .Where(f => !(move && FsHelpers.SamePath(Path.GetDirectoryName(f) ?? "", destination)))
            .ToArray();

        _cutBuffer = [];
        if (sources.Length == 0)
        {
            return;
        }

        // A copy whose sources all live in the destination folder is an in-place
        // copy → auto-rename to "name - Copy" (Explorer behavior) instead of the
        // shell's "source and destination are the same" skip/cancel error.
        var sameFolderCopy = !move &&
            sources.All(f => FsHelpers.SamePath(Path.GetDirectoryName(f) ?? "", destination));

        // Run through the same shell-operation path as drag-and-drop (a dedicated
        // STA thread). That reliably shows the Windows standard progress and
        // name-collision dialogs (replace / skip / keep both); doing it inline on
        // the UI thread did not surface the collision dialog. Reload + status are
        // handled by CopyOrMoveWithProgress.
        CopyOrMoveWithProgress(sources, destination, move, sameFolderCopy);
    }

    /// <summary>
    /// Creates a file in the active folder from the clipboard's content when it
    /// holds no files: spreadsheet/CSV → .csv, an image → .png, plain text → .txt.
    /// The new file lands in inline rename so the user can name it.
    /// </summary>
    private void PasteClipboardContentAsFile()
    {
        var destination = GetCurrentPath(_activeGrid);
        if (string.IsNullOrEmpty(destination) || ArchivePath.Contains(destination))
        {
            return;
        }

        try
        {
            string? created = null;

            if (TryGetClipboardCsv(out var csvBytes))
            {
                created = WriteClipboardFile(destination, Loc.T("Pasted data") + ".csv", csvBytes);
            }
            else if (TryGetClipboardPng(out var pngBytes))
            {
                created = WriteClipboardFile(destination, Loc.T("Pasted image") + ".png", pngBytes);
            }
            else
            {
                var hasText = Clipboard.ContainsText();
                var text = hasText ? Clipboard.GetText() : string.Empty;

                if (hasText && TryGetUrl(text, out var url))
                {
                    // Internet shortcut (.url): double-clicking opens it in the
                    // default browser.
                    var content = $"[InternetShortcut]\r\nURL={url}\r\n";
                    created = WriteClipboardFile(destination, UrlShortcutName(url) + ".url",
                        new UTF8Encoding(false).GetBytes(content));
                }
                else if (TryGetClipboardRtf(out var rtfBytes))
                {
                    // Rich text (e.g. Word) → .rtf. Use Ctrl+Shift+V or the context
                    // menu's "Paste as text" to paste it as plain text instead.
                    created = WriteClipboardFile(destination, Loc.T("Pasted rich text") + ".rtf", rtfBytes);
                }
                else if (hasText)
                {
                    created = WriteClipboardFile(destination, Loc.T("Pasted text") + ".txt",
                        new UTF8Encoding(false).GetBytes(text));
                }
            }

            if (created is not null)
            {
                BeginInlineCreate(Path.GetFileName(created));
            }
            else
            {
                SetStatus(Loc.T("Clipboard has no pasteable content"));
            }
        }
        catch (Exception ex)
        {
            SetStatus(Loc.F("Paste failed: {0}", ex.Message));
        }
    }

    private static string WriteClipboardFile(string directory, string defaultName, byte[] bytes)
    {
        var path = FsHelpers.NextAvailablePath(Path.Combine(directory, defaultName));
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>Creates a file from the given bytes in the active folder and opens inline rename.</summary>
    private void CreateClipboardFile(byte[] bytes, string defaultName)
    {
        var destination = GetCurrentPath(_activeGrid);
        if (string.IsNullOrEmpty(destination) || ArchivePath.Contains(destination))
        {
            return;
        }
        try
        {
            var created = WriteClipboardFile(destination, defaultName, bytes);
            BeginInlineCreate(Path.GetFileName(created));
        }
        catch (Exception ex)
        {
            SetStatus(Loc.F("Paste failed: {0}", ex.Message));
        }
    }

    /// <summary>Pastes the clipboard's text as a plain-text (.txt) file.</summary>
    private void PasteAsPlainText()
    {
        if (!Clipboard.ContainsText())
        {
            SetStatus(Loc.T("Clipboard has no text"));
            return;
        }
        var text = Clipboard.GetText();
        CreateClipboardFile(new UTF8Encoding(false).GetBytes(text), Loc.T("Pasted text") + ".txt");
    }

    /// <summary>Pastes the clipboard's rich text as an .rtf file.</summary>
    private void PasteAsRichText()
    {
        if (!TryGetClipboardRtf(out var bytes))
        {
            SetStatus(Loc.T("Clipboard has no rich text"));
            return;
        }
        CreateClipboardFile(bytes, Loc.T("Pasted rich text") + ".rtf");
    }

    private static bool TryGetClipboardRtf(out byte[] bytes)
    {
        bytes = [];
        if (!Clipboard.ContainsData(DataFormats.Rtf))
        {
            return false;
        }
        var data = Clipboard.GetData(DataFormats.Rtf);
        bytes = data switch
        {
            MemoryStream ms => ms.ToArray(),
            byte[] raw => raw,
            string s => new UTF8Encoding(false).GetBytes(s),
            _ => [],
        };
        return bytes.Length > 0;
    }

    // ---- "Paste special": create a file from a specific clipboard format ----

    private void PasteAsCsv()
    {
        if (!TryGetClipboardCsv(out var bytes))
        {
            SetStatus(Loc.T("Clipboard has no CSV"));
            return;
        }
        CreateClipboardFile(bytes, Loc.T("Pasted data") + ".csv");
    }

    private void PasteAsImage()
    {
        if (!TryGetClipboardPng(out var bytes))
        {
            SetStatus(Loc.T("Clipboard has no image"));
            return;
        }
        CreateClipboardFile(bytes, Loc.T("Pasted image") + ".png");
    }

    private void PasteAsUrl()
    {
        if (!Clipboard.ContainsText() || !TryGetUrl(Clipboard.GetText(), out var url))
        {
            SetStatus(Loc.T("Clipboard has no URL"));
            return;
        }
        var content = $"[InternetShortcut]\r\nURL={url}\r\n";
        CreateClipboardFile(new UTF8Encoding(false).GetBytes(content), UrlShortcutName(url) + ".url");
    }

    private void PasteAsHtml()
    {
        if (!TryGetClipboardHtml(out var html))
        {
            SetStatus(Loc.T("Clipboard has no HTML"));
            return;
        }
        CreateClipboardFile(new UTF8Encoding(false).GetBytes(html), Loc.T("Pasted HTML") + ".html");
    }

    /// <summary>Extracts the HTML document from the clipboard's CF_HTML payload (strips the header).</summary>
    private static bool TryGetClipboardHtml(out string html)
    {
        html = "";
        if (!Clipboard.ContainsData(DataFormats.Html))
        {
            return false;
        }

        var data = Clipboard.GetData(DataFormats.Html);
        var cf = data switch
        {
            string s => s,
            MemoryStream ms => Encoding.UTF8.GetString(ms.ToArray()),
            byte[] raw => Encoding.UTF8.GetString(raw),
            _ => "",
        };
        if (string.IsNullOrEmpty(cf))
        {
            return false;
        }

        // CF_HTML's StartHTML / EndHTML are byte offsets into the UTF-8 buffer.
        var bytes = Encoding.UTF8.GetBytes(cf);
        var start = ReadCfHtmlOffset(cf, "StartHTML:");
        var end = ReadCfHtmlOffset(cf, "EndHTML:");
        if (start >= 0 && end > start && end <= bytes.Length)
        {
            html = Encoding.UTF8.GetString(bytes, start, end - start);
            return html.Length > 0;
        }

        // Fallback: from the first <html>/<!doctype> marker.
        var idx = cf.IndexOf("<!doctype", StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            idx = cf.IndexOf("<html", StringComparison.OrdinalIgnoreCase);
        }
        html = idx >= 0 ? cf[idx..] : cf;
        return html.Length > 0;
    }

    private static int ReadCfHtmlOffset(string cf, string key)
    {
        var i = cf.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (i < 0)
        {
            return -1;
        }
        i += key.Length;
        var j = i;
        while (j < cf.Length && char.IsDigit(cf[j]))
        {
            j++;
        }
        return int.TryParse(cf[i..j], out var value) ? value : -1;
    }

    private static bool ClipboardHasFormat(string name)
    {
        try
        {
            return Clipboard.GetDataObject()?.GetFormats(false)
                .Any(f => f.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? false;
        }
        catch
        {
            return false;
        }
    }

    private static bool ClipboardHasCsv() => ClipboardHasFormat("CSV");

    private static bool ClipboardHasImage() =>
        Clipboard.ContainsImage()
        || ClipboardHasFormat("PNG")
        || ClipboardHasFormat("DeviceIndependentBitmap")
        || ClipboardHasFormat("Format17");

    private static bool ClipboardHasUrl() =>
        Clipboard.ContainsText() && TryGetUrl(Clipboard.GetText(), out _);

    /// <summary>
    /// Spreadsheet apps (Excel, LibreOffice) put a CSV rendering on the clipboard.
    /// The format is enumerated case-insensitively (Excel registers it as "Csv",
    /// WPF's DataFormats.CommaSeparatedValue is "CSV", and a case-sensitive match
    /// misses it). Falls back to converting the tab-separated Unicode text to CSV
    /// when a spreadsheet is the source but no CSV blob is available.
    /// </summary>
    private static bool TryGetClipboardCsv(out byte[] bytes)
    {
        bytes = [];

        IDataObject? data;
        try { data = Clipboard.GetDataObject(); }
        catch { return false; }
        if (data is null)
        {
            return false;
        }

        string[] formats;
        try { formats = data.GetFormats(false); }
        catch { formats = []; }

        // 1) Native CSV blob.
        var csvFormat = formats.FirstOrDefault(f => f.Equals("CSV", StringComparison.OrdinalIgnoreCase));
        if (csvFormat is not null)
        {
            object? raw = null;
            try { raw = data.GetData(csvFormat, false); }
            catch { }
            if (TryExtractBytes(raw, out bytes) && bytes.Length > 0)
            {
                return true;
            }
        }

        // 2) Spreadsheet source without a usable CSV blob → convert the
        //    tab-separated Unicode text to CSV (UTF-8 with BOM so Excel re-opens
        //    it with the right encoding).
        var looksLikeSpreadsheet = formats.Any(f =>
            f.Contains("CSV", StringComparison.OrdinalIgnoreCase) ||
            f.Contains("Biff", StringComparison.OrdinalIgnoreCase) ||
            f.Contains("XML Spreadsheet", StringComparison.OrdinalIgnoreCase) ||
            f.Contains("SYLK", StringComparison.OrdinalIgnoreCase));
        if (looksLikeSpreadsheet && Clipboard.ContainsText())
        {
            var tsv = Clipboard.GetText();
            if (!string.IsNullOrEmpty(tsv) && tsv.Contains('\t'))
            {
                var csv = TsvToCsv(tsv);
                bytes = [.. new UTF8Encoding(true).GetPreamble(), .. new UTF8Encoding(false).GetBytes(csv)];
                return true;
            }
        }

        return false;
    }

    private static bool TryExtractBytes(object? data, out byte[] bytes)
    {
        bytes = data switch
        {
            MemoryStream ms => ms.ToArray(),
            byte[] raw => raw,
            string s => Encoding.Default.GetBytes(s),
            _ => [],
        };
        return bytes.Length > 0;
    }

    private static string TsvToCsv(string tsv)
    {
        var sb = new StringBuilder();
        var rows = tsv.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        foreach (var row in rows)
        {
            var cells = row.Split('\t');
            for (var i = 0; i < cells.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }
                sb.Append(CsvQuote(cells[i]));
            }
            sb.Append("\r\n");
        }
        return sb.ToString();
    }

    private static string CsvQuote(string field) =>
        field.IndexOfAny([',', '"', '\n', '\r']) >= 0
            ? "\"" + field.Replace("\"", "\"\"") + "\""
            : field;

    /// <summary>True when the clipboard text is a single absolute http(s) URL.</summary>
    private static bool TryGetUrl(string? text, out string url)
    {
        url = (text ?? "").Trim();
        if (url.Length == 0 || url.IndexOfAny([' ', '\t', '\r', '\n']) >= 0)
        {
            return false;
        }
        return (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            && Uri.TryCreate(url, UriKind.Absolute, out _);
    }

    /// <summary>A sensible file name for a URL shortcut (last path segment or host).</summary>
    private string UrlShortcutName(string url)
    {
        try
        {
            var uri = new Uri(url);
            var segment = uri.Segments.Length > 0 ? uri.Segments[^1].Trim('/') : "";
            var name = string.IsNullOrEmpty(segment) ? uri.Host : Uri.UnescapeDataString(segment);
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            name = name.Trim();
            return string.IsNullOrEmpty(name) ? Loc.T("Pasted link") : name;
        }
        catch
        {
            return Loc.T("Pasted link");
        }
    }

    private static bool TryGetClipboardPng(out byte[] bytes)
    {
        bytes = [];

        // 1) Raw PNG payload (best — preserves transparency).
        if (Clipboard.ContainsData("PNG") && Clipboard.GetData("PNG") is MemoryStream pngStream)
        {
            bytes = pngStream.ToArray();
            if (bytes.Length > 0)
            {
                return true;
            }
        }

        // 2) Standard CF_BITMAP / CF_DIB via WPF.
        BitmapSource? image = null;
        try { image = Clipboard.GetImage(); }
        catch { }
        if (image is not null && EncodePng(image, out bytes))
        {
            return true;
        }

        // 3) DIBV5 / DIB that WPF's GetImage()/ContainsImage() doesn't recognize —
        //    common for PDF viewers, scanners and some browsers. Wrap the raw DIB
        //    in a BMP file header so it can be decoded, then encode as PNG.
        foreach (var format in new[] { "Format17" /* CF_DIBV5 */, DataFormats.Dib })
        {
            object? data = null;
            try { data = Clipboard.GetData(format); }
            catch { }
            if (data is MemoryStream dib && DibToBitmap(dib.ToArray()) is { } src && EncodePng(src, out bytes))
            {
                return true;
            }
        }

        return false;
    }

    private static bool EncodePng(BitmapSource source, out byte[] bytes)
    {
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var output = new MemoryStream();
            encoder.Save(output);
            bytes = output.ToArray();
            return bytes.Length > 0;
        }
        catch
        {
            bytes = [];
            return false;
        }
    }

    /// <summary>
    /// Decodes a packed DIB (BITMAPINFOHEADER / V4 / V5 + pixels, no file header)
    /// by prepending a BMP file header. Handles the formats PDF viewers / scanners
    /// place on the clipboard that <see cref="Clipboard.GetImage"/> can't read.
    /// </summary>
    private static BitmapSource? DibToBitmap(byte[] dib)
    {
        if (dib.Length < 40)
        {
            return null;
        }

        try
        {
            var headerSize = BitConverter.ToInt32(dib, 0);
            var bitCount = BitConverter.ToInt16(dib, 14);
            var compression = BitConverter.ToInt32(dib, 16);
            var clrUsed = BitConverter.ToInt32(dib, 32);

            var paletteBytes = bitCount <= 8 ? (clrUsed != 0 ? clrUsed : 1 << bitCount) * 4 : 0;
            // BI_BITFIELDS masks follow a plain BITMAPINFOHEADER; V4/V5 embed them.
            var maskBytes = compression == 3 && headerSize == 40 ? 12 : 0;
            var offBits = 14 + headerSize + maskBytes + paletteBytes;
            var fileSize = 14 + dib.Length;

            var bmp = new byte[fileSize];
            bmp[0] = (byte)'B';
            bmp[1] = (byte)'M';
            BitConverter.GetBytes(fileSize).CopyTo(bmp, 2);
            BitConverter.GetBytes(offBits).CopyTo(bmp, 10);
            Array.Copy(dib, 0, bmp, 14, dib.Length);

            using var ms = new MemoryStream(bmp);
            var decoder = new BmpBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            return decoder.Frames.Count > 0 ? decoder.Frames[0] : null;
        }
        catch
        {
            return null;
        }
    }

    private void MoveSelectionToTrash()
    {
        var items = ActiveSelectedItems().Where(i => !i.IsParent).ToArray();
        if (items.Length == 0)
        {
            return;
        }

        if (!Confirm(Loc.F("Move {0} item(s) to Recycle Bin?", items.Length), Loc.T("Move to Recycle Bin")))
        {
            return;
        }

        var focusIndex = FirstSelectedListingIndex(items);

        foreach (var item in items)
        {
            try
            {
                if (item.IsDirectory)
                {
                    VbFileSystem.DeleteDirectory(item.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                }
                else
                {
                    VbFileSystem.DeleteFile(item.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                }
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
            }
        }

        RefreshActivePaneAfterMutation(focusIndex);
    }

    private void NewFolder()
    {
        try
        {
            var path = FsHelpers.NextAvailablePath(Path.Combine(GetCurrentPath(_activeGrid), Loc.T("New folder")));
            Directory.CreateDirectory(path);
            BeginInlineCreate(Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            SetStatus(Loc.F("New folder failed: {0}", ex.Message));
        }
    }

    private void NewFile()
    {
        try
        {
            var path = FsHelpers.NextAvailablePath(Path.Combine(GetCurrentPath(_activeGrid), Loc.T("New file") + ".txt"));
            File.WriteAllBytes(path, []);
            BeginInlineCreate(Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            SetStatus(Loc.F("New file failed: {0}", ex.Message));
        }
    }

    /// <summary>
    /// After creating a new item with a default name, reloads the active pane and
    /// drops the new row into inline rename (Explorer-style): the user types the
    /// final name and presses Enter (or Esc to keep the default name).
    /// </summary>
    private void BeginInlineCreate(string createdName)
    {
        // Pass the name through Reload's selectName parameter: Reload itself calls
        // SetPendingSelectionName, so setting it beforehand would be overwritten.
        // The rename flag is separate and survives the reload.
        SetPendingRename(ActivePane, true);
        Reload(_activeGrid, createdName);
    }

    private static bool Confirm(string message, string confirmText)
    {
        var dialog = new ConfirmDialog("tfx", message, confirmText);
        return dialog.ShowDialog() == true;
    }

    private void StartRename(DataGrid grid, FileItem item)
    {
        var nameColumn = grid == LeftGrid ? LeftNameColumn : RightNameColumn;
        grid.IsReadOnly = false;
        grid.CurrentCell = new DataGridCellInfo(item, nameColumn);
        grid.BeginEdit();
    }

    private void Grid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (sender is not DataGrid grid)
        {
            return;
        }

        Dispatcher.BeginInvoke(() => grid.IsReadOnly = true, DispatcherPriority.Background);

        if (e.EditAction != DataGridEditAction.Commit)
        {
            return;
        }

        if (e.Row.Item is not FileItem item || item.IsParent)
        {
            return;
        }

        var nameColumn = grid == LeftGrid ? LeftNameColumn : RightNameColumn;
        if (e.Column != nameColumn)
        {
            return;
        }

        var tb = e.EditingElement as TextBox ?? FindVisualChild<TextBox>(e.EditingElement);
        if (tb is null)
        {
            return;
        }

        var newName = (tb.Text ?? "").Trim();
        if (string.IsNullOrEmpty(newName) || newName == item.Name)
        {
            return;
        }

        var directory = Path.GetDirectoryName(item.FullPath) ?? GetCurrentPath(grid);
        var target = FsHelpers.NextAvailablePath(Path.Combine(directory, newName));

        try
        {
            if (item.IsDirectory)
            {
                Directory.Move(item.FullPath, target);
            }
            else
            {
                File.Move(item.FullPath, target);
            }
        }
        catch (Exception ex)
        {
            SetStatus(Loc.F("Rename failed: {0}", ex.Message));
            return;
        }

        // Restore selection on the renamed entry after the reload so the user
        // keeps their place (and so arrow keys keep navigating). The name must be
        // passed through Reload's selectName parameter — Reload itself calls
        // SetPendingSelectionName, so setting it beforehand would be overwritten.
        var renamedName = Path.GetFileName(target);
        var renamedPane = PaneOf(grid);

        Dispatcher.BeginInvoke(() =>
        {
            Reload(LeftGrid, renamedPane == Pane.Left ? renamedName : null);
            Reload(RightGrid, renamedPane == Pane.Right ? renamedName : null);
        }, DispatcherPriority.Background);
    }

    private void RenameTextBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox tb)
        {
            return;
        }

        tb.Focus();
        var text = tb.Text ?? "";

        if (tb.DataContext is FileItem item && !item.IsDirectory)
        {
            var dot = text.LastIndexOf('.');
            if (dot > 0)
            {
                tb.Select(0, dot);
                return;
            }
        }

        tb.SelectAll();
    }

    private void DeletePermanently()
    {
        var items = ActiveSelectedItems().Where(i => !i.IsParent).ToArray();
        if (items.Length == 0)
        {
            return;
        }

        var msg = items.Length == 1
            ? Loc.F("Permanently delete \"{0}\"? This cannot be undone.", items[0].Name)
            : Loc.F("Permanently delete {0} item(s)? This cannot be undone.", items.Length);

        if (!Confirm(msg, Loc.T("Delete permanently")))
        {
            return;
        }

        var focusIndex = FirstSelectedListingIndex(items);

        foreach (var item in items)
        {
            try
            {
                if (item.IsDirectory)
                {
                    Directory.Delete(item.FullPath, recursive: true);
                }
                else
                {
                    File.Delete(item.FullPath);
                }
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
            }
        }

        RefreshActivePaneAfterMutation(focusIndex);
    }

    private void CompressSelection()
    {
        var items = ActiveSelectedItems().Where(i => !i.IsParent).ToArray();
        if (items.Length == 0)
        {
            return;
        }

        var directory = GetCurrentPath(_activeGrid);
        var baseName = items.Length == 1
            ? Path.GetFileNameWithoutExtension(items[0].Name)
            : Loc.T("Archive");
        var zipPath = FsHelpers.NextAvailablePath(Path.Combine(directory, $"{baseName}.zip"));

        try
        {
            using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            foreach (var item in items)
            {
                if (item.IsDirectory)
                {
                    AddDirectoryToArchive(archive, item.FullPath, item.Name);
                }
                else
                {
                    archive.CreateEntryFromFile(item.FullPath, item.Name, CompressionLevel.Optimal);
                }
            }

            Reload(_activeGrid);
            SetStatus(Loc.F("Created {0}", zipPath));
        }
        catch (Exception ex)
        {
            SetStatus(Loc.F("Compress failed: {0}", ex.Message));
            try
            {
                if (File.Exists(zipPath))
                {
                    File.Delete(zipPath);
                }
            }
            catch
            {
            }
        }
    }

    private void ExtractSelectedArchives()
    {
        var archives = ActiveSelectedItems()
            .Where(i => !i.IsParent && !i.IsDirectory && Path.GetExtension(i.FullPath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (archives.Length == 0)
        {
            SetStatus(Loc.T("Select one or more .zip files to extract"));
            return;
        }

        foreach (var archiveItem in archives)
        {
            var destination = FsHelpers.NextAvailablePath(Path.Combine(
                GetCurrentPath(_activeGrid),
                Path.GetFileNameWithoutExtension(archiveItem.Name)));

            try
            {
                Directory.CreateDirectory(destination);
                ZipFile.ExtractToDirectory(archiveItem.FullPath, destination);
            }
            catch (Exception ex)
            {
                SetStatus(Loc.F("Extract failed: {0}", ex.Message));
                return;
            }
        }

        Reload(_activeGrid);
        SetStatus(Loc.F("Extracted {0} archive(s)", archives.Length));
    }

    private static void AddDirectoryToArchive(ZipArchive archive, string sourceDirectory, string entryRoot)
    {
        var files = Directory.EnumerateFiles(sourceDirectory, "*", System.IO.SearchOption.AllDirectories);
        var wroteAnyFile = false;

        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(sourceDirectory, file);
            var entryName = Path.Combine(entryRoot, relative).Replace(Path.DirectorySeparatorChar, '/');
            archive.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
            wroteAnyFile = true;
        }

        if (!wroteAnyFile)
        {
            archive.CreateEntry(entryRoot.TrimEnd('/', '\\') + "/");
        }
    }

    private static T? FindVisualChild<T>(DependencyObject? parent) where T : DependencyObject
    {
        if (parent is null)
        {
            return null;
        }
        if (parent is T match)
        {
            return match;
        }

        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var found = FindVisualChild<T>(VisualTreeHelper.GetChild(parent, i));
            if (found is not null)
            {
                return found;
            }
        }
        return null;
    }

    private static T? FindVisualAncestor<T>(DependencyObject? child) where T : DependencyObject
    {
        var node = child;
        while (node is not null)
        {
            if (node is T match)
            {
                return match;
            }

            node = VisualTreeHelper.GetParent(node);
        }

        return null;
    }

}
