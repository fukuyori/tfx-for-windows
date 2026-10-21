using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;

namespace Tfx;

// yazi-style quick preview: Space opens a bordered panel laid over the file
// panes showing the selected item (text, image, or a folder's entries). It is
// independent of the preview pane. It is tied to the item it was opened for:
// PageUp / PageDown scroll the panel; Space, Esc, Enter, Tab, or anything that
// moves the selection to another item (Up / Down, Home / End, a search or
// reload), changes the folder, tab or active pane closes it. Moving focus to the
// terminal or the search box does not. Clicking the text selects it (Ctrl+C
// copies). Space is ignored while a text field has focus and during the
// type-ahead window, so typing never triggers it.
public partial class MainWindow
{
    private const int QuickPreviewFolderEntryCap = 500;
    private const int QuickPreviewFolderEnumerationCap = 5000;

    private CancellationTokenSource? _quickPreviewCts;

    // Full path of the item the open panel shows; null while closed.
    private string? _quickPreviewPath;

    private bool IsQuickPreviewOpen => QuickPreviewOverlay.Visibility == Visibility.Visible;

    private bool TypeAheadIdle() =>
        _typeAheadPrefix.Length == 0 || DateTime.UtcNow - _typeAheadLastKeystroke > TypeAheadTimeout;

    private void OpenQuickPreview()
    {
        if (ActiveListingSelectedItem() is not FileItem item || item.IsParent)
        {
            return;
        }

        _quickPreviewPath = item.FullPath;
        QuickPreviewOverlay.Visibility = Visibility.Visible;
        ShowQuickPreview(item);
    }

    private bool IsFocusInQuickPreviewText() =>
        IsInside(Keyboard.FocusedElement as DependencyObject, QuickPreviewText);

    private void CloseQuickPreview()
    {
        if (!IsQuickPreviewOpen)
        {
            return;
        }

        // Clicking the text moves focus into it (to allow selecting / copying);
        // hand focus back to the listing so keyboard navigation continues.
        var restoreFocus = IsFocusInQuickPreviewText();
        _quickPreviewCts?.Cancel();
        _quickPreviewPath = null;
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

            if (e.Key == Key.Tab
                || IsShortcut("rename", e) || IsShortcut("newFile", e) || IsShortcut("newFolder", e))
            {
                // Rename / new item start inline editing in the listing
                // underneath; the editor would be hidden behind the opaque panel.
                // Tab moves focus away from the item (to the other pane, or to
                // another control in single-pane layout), which would leave the
                // panel open with focus somewhere out of sight. The key itself
                // still runs.
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
                // Back to the listing, then move the selection: the selection
                // leaving the item closes the panel.
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

    /// <summary>
    /// Closes the panel unless <paramref name="selection"/> is still exactly the
    /// item it was opened for. The panel is tied to that one item: an empty
    /// selection or a move to another item (Up / Down, Home / End, a search or
    /// reload replacing the rows) closes it and returns to the listing.
    /// </summary>
    private void CloseQuickPreviewIfSelectionMoved(IReadOnlyList<FileItem> selection)
    {
        if (!IsQuickPreviewOpen)
        {
            return;
        }

        if (selection.Count == 1
            && string.Equals(selection[0].FullPath, _quickPreviewPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        CloseQuickPreview();
    }

    private async void ShowQuickPreview(FileItem item)
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
