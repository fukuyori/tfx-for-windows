using System.Windows.Threading;

namespace Tfx;

public partial class MainWindow
{
    private InstanceLinkServer? _instanceLink;

    /// <summary>
    /// Starts listening for folders sent by a tfx launched from this window's
    /// built-in terminal (see <see cref="InstanceLink"/>).
    /// </summary>
    private void StartInstanceLink()
    {
        _instanceLink = new InstanceLinkServer(folder =>
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, () => OnInstanceLinkFolder(folder)));
    }

    private void DisposeInstanceLink()
    {
        _instanceLink?.Dispose();
        _instanceLink = null;
    }

    /// <summary>
    /// Points the active file pane at the folder the terminal-launched tfx sent.
    /// Focus is left where it is (normally the terminal that issued the command).
    /// </summary>
    private void OnInstanceLinkFolder(string folder)
    {
        if (!TryResolveStartupFolder(folder, out var full) || ArchivePath.Contains(full))
        {
            SetStatus(Loc.F("Folder not found: {0}", folder));
            return;
        }

        if (!string.Equals(GetCurrentPath(_activeGrid), full, StringComparison.OrdinalIgnoreCase))
        {
            // No selection name: the load then skips ApplyPendingSelection's
            // focus step, which would pull focus from the terminal into the list.
            Navigate(_activeGrid, full, true, selectName: null);
        }
        SetStatus(Loc.F("Synced to {0}", full));
    }
}
