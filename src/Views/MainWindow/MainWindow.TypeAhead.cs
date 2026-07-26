using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Tfx;

// Finder / Explorer-style type-to-select for the file listings. Typing a
// printable character jumps the selection to the first row whose name starts
// with it; further keystrokes within one second extend the prefix. Pressing
// the same single character repeatedly cycles through the rows with that
// initial (wrapping), so files sorted behind the folder block stay reachable.
// A mistyped key that matches nothing keeps the current prefix and selection.
public partial class MainWindow
{
    private static readonly TimeSpan TypeAheadTimeout = TimeSpan.FromSeconds(1);

    private string _typeAheadPrefix = "";
    private DateTime _typeAheadLastKeystroke;
    private DispatcherTimer? _typeAheadStatusTimer;

    private void Window_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        var text = e.Text;
        if (string.IsNullOrEmpty(text) || text.Any(char.IsControl))
        {
            return; // Enter / Esc / Backspace / Ctrl chords — not type-ahead input
        }

        // Text entry fields (search box, inline rename, path bar) and the
        // terminal own their keystrokes; type-ahead only runs while keyboard
        // focus is inside the active file listing.
        if (Keyboard.FocusedElement is TextBox || IsFocusInTerminal() || !IsFocusInActiveListing())
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (now - _typeAheadLastKeystroke > TypeAheadTimeout)
        {
            _typeAheadPrefix = "";
        }
        _typeAheadLastKeystroke = now;

        if (_typeAheadPrefix.Length == 0 && text == " ")
        {
            return; // a lone leading Space keeps its selection meaning
        }

        HandleTypeAhead(text);
        // Consumed even when nothing matched, so the keystroke doesn't fall
        // through to the listing control (system beep / built-in TextSearch).
        e.Handled = true;
    }

    private void HandleTypeAhead(string text)
    {
        var iconView = IconViewOf(ActivePane);
        var items = _settings.ViewMode == ViewMode.Icons ? iconView.Items : _activeGrid.Items;

        int matchIndex;
        if (_typeAheadPrefix.Length == 1 &&
            string.Equals(text, _typeAheadPrefix, StringComparison.CurrentCultureIgnoreCase))
        {
            // Same initial again: cycle to the next row with that initial,
            // wrapping past the end (Explorer behavior).
            var current = _settings.ViewMode == ViewMode.Icons
                ? iconView.SelectedItem
                : _activeGrid.SelectedItem;
            var currentIndex = current is null ? -1 : items.IndexOf(current);
            matchIndex = FindTypeAheadMatch(items, _typeAheadPrefix, currentIndex + 1);
        }
        else
        {
            var candidate = _typeAheadPrefix + text;
            matchIndex = FindTypeAheadMatch(items, candidate, 0);
            if (matchIndex >= 0)
            {
                _typeAheadPrefix = candidate;
            }
            // No match: the prefix and the selection stay at the last hit, so
            // a mistyped key doesn't lose the position.
        }

        if (matchIndex >= 0)
        {
            SelectAndFocusActiveIndex(matchIndex);
            if (items[matchIndex] is FileItem item)
            {
                SchedulePreviewUpdate(item);
            }
        }

        if (_typeAheadPrefix.Length > 0)
        {
            ShowTypeAheadStatus();
        }
    }

    /// <summary>
    /// Index of the first non-parent row at or after <paramref name="startIndex"/>
    /// (wrapping) whose name starts with <paramref name="prefix"/>, or -1.
    /// </summary>
    private static int FindTypeAheadMatch(ItemCollection items, string prefix, int startIndex)
    {
        var count = items.Count;
        for (var offset = 0; offset < count; offset++)
        {
            var i = (startIndex + offset) % count;
            if (items[i] is FileItem { IsParent: false } item &&
                item.Name.StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// Shows the active prefix in the status line while the type-ahead window
    /// is open; the normal status text returns once the window times out.
    /// </summary>
    private void ShowTypeAheadStatus()
    {
        SetStatus(Loc.F("Find: {0}", _typeAheadPrefix));
        if (_typeAheadStatusTimer is null)
        {
            _typeAheadStatusTimer = new DispatcherTimer { Interval = TypeAheadTimeout };
            _typeAheadStatusTimer.Tick += (_, _) => ResetTypeAhead(restoreStatus: true);
        }
        _typeAheadStatusTimer.Stop();
        _typeAheadStatusTimer.Start();
    }

    private void ResetTypeAhead(bool restoreStatus = false)
    {
        _typeAheadStatusTimer?.Stop();
        var hadPrefix = _typeAheadPrefix.Length > 0;
        _typeAheadPrefix = "";
        if (restoreStatus && hadPrefix)
        {
            UpdateStatus();
        }
    }
}
