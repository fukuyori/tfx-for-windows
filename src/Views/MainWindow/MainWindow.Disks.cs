using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Tfx;

/// <summary>
/// A drive row in the sidebar DISKS section. <see cref="UsedStar"/> /
/// <see cref="FreeStar"/> feed star-sized grid columns so the usage bar
/// scales with the sidebar width without a converter.
/// </summary>
public sealed record DiskEntry(string Path, string Label, string ToolTip, double UsedRatio)
{
    public GridLength UsedStar => new(Math.Clamp(UsedRatio, 0, 1), GridUnitType.Star);
    public GridLength FreeStar => new(Math.Clamp(1 - UsedRatio, 0, 1), GridUnitType.Star);
}

public partial class MainWindow
{
    /// <summary>
    /// Populates the sidebar DISKS section (drive letter + usage bar). Runs
    /// alongside <see cref="LoadDrives"/>, so startup, hidden-files toggles,
    /// and WM_DEVICECHANGE (USB plug / unplug) all refresh it.
    /// </summary>
    private async void LoadDisks()
    {
        List<DiskEntry> entries;
        try
        {
            entries = await Task.Run(() =>
                DriveInfo.GetDrives()
                    .Where(d => d.IsReady)
                    .Select(d =>
                    {
                        try
                        {
                            var root = d.RootDirectory.FullName;
                            var total = d.TotalSize;
                            var free = d.AvailableFreeSpace;
                            var used = total > 0 ? (double)(total - free) / total : 0;
                            string volume;
                            try { volume = d.VolumeLabel; } catch { volume = ""; }
                            var tipName = string.IsNullOrWhiteSpace(volume) ? root : $"{volume} ({root})";
                            var tip = Loc.F("{0}  {1} free of {2}",
                                tipName, FileItem.FormatSize(free), FileItem.FormatSize(total));
                            return new DiskEntry(root, root, tip, used);
                        }
                        catch
                        {
                            return null;
                        }
                    })
                    .OfType<DiskEntry>()
                    .OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
                    .ToList());
        }
        catch
        {
            return;
        }

        DisksList.ItemsSource = entries;
        SyncDiskSelectionToActivePane();
    }

    // Set true while SyncDiskSelectionToActivePane rewrites the selection so
    // DisksList_SelectionChanged doesn't ricochet back into Navigate().
    private bool _syncingDiskSelection;

    private void DisksList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingDiskSelection)
        {
            return;
        }
        if (DisksList.SelectedItem is DiskEntry disk && Directory.Exists(disk.Path))
        {
            Navigate(_activeGrid, disk.Path, true);
        }
    }

    private void DisksList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // SelectionChanged doesn't fire when the clicked disk is already the
        // highlighted one (same rationale as the pinned list): navigate from
        // the completed click itself.
        if (FindVisualAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is { Content: DiskEntry disk }
            && !FsHelpers.SamePath(GetCurrentPath(_activeGrid), disk.Path)
            && Directory.Exists(disk.Path))
        {
            Navigate(_activeGrid, disk.Path, true);
        }
    }

    /// <summary>
    /// Highlights the disk whose root the active pane is currently showing
    /// (exact root match, mirroring the pinned list's behavior).
    /// </summary>
    private void SyncDiskSelectionToActivePane()
    {
        if (DisksList.ItemsSource is not IEnumerable<DiskEntry> entries)
        {
            return;
        }

        var activePath = GetCurrentPath(_activeGrid);
        DiskEntry? match = null;
        foreach (var entry in entries)
        {
            if (FsHelpers.SamePath(entry.Path, activePath))
            {
                match = entry;
                break;
            }
        }

        _syncingDiskSelection = true;
        try
        {
            DisksList.SelectedItem = match;
        }
        finally
        {
            _syncingDiskSelection = false;
        }
    }
}
