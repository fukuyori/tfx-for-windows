using System.Windows;
using System.Windows.Input;

namespace Tfx;

public partial class MainWindow
{
    private const string ChevronExpanded = "\uE70D";  // ChevronDown
    private const string ChevronCollapsed = "\uE76C"; // ChevronRight

    /// <summary>
    /// Applies the persisted collapse state of the PINNED / DISKS sidebar
    /// sections (toggled by clicking their headers) to the lists and chevrons.
    /// </summary>
    private void ApplySidebarSections()
    {
        PinnedList.Visibility = _settings.PinnedCollapsed ? Visibility.Collapsed : Visibility.Visible;
        PinnedChevron.Text = _settings.PinnedCollapsed ? ChevronCollapsed : ChevronExpanded;
        DisksList.Visibility = _settings.DisksCollapsed ? Visibility.Collapsed : Visibility.Visible;
        DisksChevron.Text = _settings.DisksCollapsed ? ChevronCollapsed : ChevronExpanded;
    }

    private void PinnedHeader_Click(object sender, MouseButtonEventArgs e)
    {
        _settings.PinnedCollapsed = !_settings.PinnedCollapsed;
        ApplySidebarSections();
        SaveSettings();
    }

    private void DisksHeader_Click(object sender, MouseButtonEventArgs e)
    {
        _settings.DisksCollapsed = !_settings.DisksCollapsed;
        ApplySidebarSections();
        SaveSettings();
    }
}
