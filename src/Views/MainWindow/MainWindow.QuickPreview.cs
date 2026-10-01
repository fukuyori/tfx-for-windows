using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;

namespace Tfx;

// yazi-style quick preview: Space opens a bordered panel laid over the file
// panes showing the selected item (text, image, or a folder's entries). It is
// independent of the preview pane. While open, the selection keeps following
// the listing (Up / Down), PageUp / PageDown scroll the panel, and Space, Esc,
// or Enter closes it. Clicking the text selects it (Ctrl+C copies). Space is ignored while a text field has focus and
// during the type-ahead window, so typing never triggers it.
public partial class MainWindow
{
    private const int QuickPreviewFolderEntryCap = 500;
    private const int QuickPreviewFolderEnumerationCap = 5000;

    private CancellationTokenSource? _quickPreviewCts;

    private bool IsQuickPreviewOpen => QuickPreviewOverlay.Visibility == Visibility.Visible;

    private bool TypeAheadIdle() =>
        _typeAheadPrefix.Length == 0 || DateTime.UtcNow - _typeAheadLastKeystroke > TypeAheadTimeout;

    private void OpenQuickPreview()
    {
        if (ActiveListingSelectedItem() is not FileItem item || item.IsParent)
        {
            return;
        }

        QuickPreviewOverlay.Visibility = Visibility.Visible;
        UpdateQuickPreview(new[] { item });
    }

    private bool IsFocusInQuickPreviewText() =>
        IsInside(Keyboard.FocusedElement as DependencyObject, QuickPreviewText);

    private void CloseQuickPreview()
    {
        // Clicking the text moves focus into it (to allow selecting / copying);
        // hand focus back to the listing so keyboard navigation continues.
        var restoreFocus = IsFocusInQuickPreviewText();
        _quickPreviewCts?.Cancel();
        QuickPreviewOverlay.Visibility = Visibility.Collapsed;
        QuickPreviewImage.Source = null;
        QuickPreviewText.Text = "";
        if (restoreFocus)
        {
            FocusActiveListing();
        }
    }

    private void ScrollQuickPreviewPage(bool down)
    {
        var page = Math.Max(24, QuickPreviewScroll.ViewportHeight - 24);
        QuickPreviewScroll.ScrollToVerticalOffset(QuickPreviewScroll.VerticalOffset + (down ? page : -page));
    }

    /// <summary>
    /// Key handling while the panel is open or when Space should open it.
    /// Returns true when the key was consumed. Called from the tunneling pass.
    /// </summary>
    private bool HandleQuickPreviewKey(KeyEventArgs e, bool inTextBox)
    {
        // The panel's own text box is a TextBox, but it must not block the
        // panel's keys (it only exists so text can be selected and copied).
        var inPanelText = IsQuickPreviewOpen && IsFocusInQuickPreviewText();
        if (inTextBox && !inPanelText)
        {
            return false;
        }

        if (IsQuickPreviewOpen)
        {
            if (e.Key is Key.Escape or Key.Enter || IsShortcut("quickPreview", e))
            {
                CloseQuickPreview();
                return true;
            }

            if (IsShortcut("rename", e) || IsShortcut("newFile", e) || IsShortcut("newFolder", e))
            {
                // These start inline editing in the listing underneath; the
                // editor would be hidden behind the opaque panel.
                CloseQuickPreview();
                return false;
            }

            if (e.Key is Key.PageUp or Key.PageDown)
            {
                ScrollQuickPreviewPage(e.Key == Key.PageDown);
                return true;
            }

            if (inPanelText && e.Key is Key.Up or Key.Down && Keyboard.Modifiers == ModifierKeys.None)
            {
                // Back to the listing so the panel keeps following the selection.
                FocusActiveListing();
                MoveActiveListingSelection(e.Key, false);
                return true;
            }

            return false;
        }

        if (IsShortcut("quickPreview", e) && IsFocusInActiveListing() && TypeAheadIdle())
        {
            OpenQuickPreview();
            return true;
        }

        return false;
    }

    private async void UpdateQuickPreview(IReadOnlyList<FileItem> selection)
    {
        _quickPreviewCts?.Cancel();
        var cts = new CancellationTokenSource();
        _quickPreviewCts = cts;
        var token = cts.Token;

        QuickPreviewImage.Visibility = Visibility.Collapsed;
        QuickPreviewImage.Source = null;
        QuickPreviewText.Text = "";
        QuickPreviewScroll.Visibility = Visibility.Visible;
        QuickPreviewScroll.ScrollToTop();
        QuickPreviewScroll.ScrollToLeftEnd();

        if (selection.Count == 0)
        {
            QuickPreviewTitle.Text = "";
            QuickPreviewMeta.Text = "";
            QuickPreviewText.Text = Loc.T("Nothing selected.");
            return;
        }

        if (selection.Count > 1)
        {
            QuickPreviewTitle.Text = Loc.F("{0} items selected", selection.Count);
            QuickPreviewMeta.Text = "";
            QuickPreviewText.Text = BuildMultiSelectionSummary(selection);
            return;
        }

        var item = selection[0];
        QuickPreviewTitle.Text = item.Name;
        QuickPreviewMeta.Text = item.IsDirectory
            ? $"{item.Kind}  {item.ModifiedText}"
            : $"{item.SizeText}  {item.ModifiedText}";

        if (ArchivePath.Contains(item.FullPath))
        {
            QuickPreviewText.Text = Loc.T("No preview inside archives.");
            return;
        }

        try
        {
            var path = item.FullPath;
            if (item.IsDirectory)
            {
                var listing = await Task.Run(() => BuildFolderListing(path, token), token);
                if (!token.IsCancellationRequested)
                {
                    QuickPreviewText.Text = listing;
                }
                return;
            }

            var security = BuildPreviewSecurityOptions();
            var preview = await Task.Run(() => PreviewLoader.Load(path, security, token), token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            if (preview.Kind == PreviewKind.Image && preview.Image is not null)
            {
                QuickPreviewScroll.Visibility = Visibility.Collapsed;
                QuickPreviewImage.Source = preview.Image;
                QuickPreviewImage.Visibility = Visibility.Visible;
            }
            else if (preview.Kind == PreviewKind.Text)
            {
                QuickPreviewText.Text = preview.Text ?? "";
                if (!string.IsNullOrEmpty(preview.ExtraInfo))
                {
                    QuickPreviewMeta.Text += "  " + preview.ExtraInfo.Replace('\n', ' ');
                }
            }
            else
            {
                QuickPreviewText.Text = preview.ExtraInfo ?? Loc.T("No preview available.");
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested)
            {
                QuickPreviewText.Text = Loc.F("Preview error: {0}", ex.Message);
            }
        }
    }

    private static string BuildFolderListing(string path, CancellationToken token)
    {
        // Enumerate with a hard cap and cancellation checks BEFORE sorting, so a
        // huge folder or a slow share neither buffers everything nor keeps
        // running after the panel is closed.
        var collected = new List<FileSystemInfo>();
        var enumerationCapped = false;
        foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos())
        {
            token.ThrowIfCancellationRequested();
            if (collected.Count >= QuickPreviewFolderEnumerationCap)
            {
                enumerationCapped = true;
                break;
            }
            collected.Add(entry);
        }

        if (collected.Count == 0)
        {
            return Loc.T("(empty folder)");
        }

        var shown = collected
            .OrderByDescending(e => e is DirectoryInfo)
            .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(QuickPreviewFolderEntryCap)
            .ToList();
        var builder = new StringBuilder();
        foreach (var entry in shown)
        {
            builder.Append(entry is DirectoryInfo ? entry.Name + Path.DirectorySeparatorChar : entry.Name).Append('\n');
        }

        if (enumerationCapped || collected.Count > shown.Count)
        {
            builder.Append(Loc.F("... first {0} entries shown", shown.Count));
        }
        return builder.ToString().TrimEnd('\n');
    }
}
