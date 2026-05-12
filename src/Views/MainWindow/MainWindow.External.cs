using System.Diagnostics;
using System.Windows;

namespace Tfx;

public partial class MainWindow
{
    private void OpenTerminal()
    {
        var path = GetCurrentPath(_activeGrid);
        try
        {
            var terminal = Environment.GetEnvironmentVariable("WT_SESSION") is not null ? "wt.exe" : "powershell.exe";
            Process.Start(new ProcessStartInfo(terminal)
            {
                WorkingDirectory = path,
                UseShellExecute = true
            });
        }
        catch
        {
            Process.Start(new ProcessStartInfo("powershell.exe")
            {
                WorkingDirectory = path,
                UseShellExecute = true
            });
        }
    }

    private void RevealInExplorer()
    {
        var selected = SelectedItems(_activeGrid).FirstOrDefault(i => !i.IsParent);
        var currentPath = GetCurrentPath(_activeGrid);

        string argument;
        if (selected is not null && ArchivePath.TryParse(selected.FullPath, out var selArchive, out _))
        {
            argument = $"/select,\"{selArchive}\"";
        }
        else if (selected is not null)
        {
            argument = $"/select,\"{selected.FullPath}\"";
        }
        else if (ArchivePath.TryParse(currentPath, out var curArchive, out _))
        {
            argument = $"/select,\"{curArchive}\"";
        }
        else
        {
            argument = currentPath;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", argument) { UseShellExecute = true });
    }

    private void ToggleHidden()
    {
        ShowHidden = !ShowHidden;
        HiddenButton.IsChecked = ShowHidden;
        Reload(LeftGrid);
        Reload(RightGrid);
        LoadDrives();
        QueueFolderTreeSyncToActivePane();
        SetStatus(ShowHidden ? Loc.T("Hidden files visible") : Loc.T("Hidden files hidden"));
    }

    private void Terminal_Click(object sender, RoutedEventArgs e) => OpenTerminal();

    private void Explorer_Click(object sender, RoutedEventArgs e) => RevealInExplorer();

    private void Hidden_Click(object sender, RoutedEventArgs e) => ToggleHidden();

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.ViewMode == ViewMode.Icons)
        {
            (_activeGrid == LeftGrid ? LeftIconView : RightIconView).SelectAll();
        }
        else
        {
            _activeGrid.SelectAll();
        }
    }

    private void CopySelectedPath(IReadOnlyList<FileItem> selection)
    {
        if (selection.Count != 1)
        {
            return;
        }

        Clipboard.SetText(selection[0].FullPath);
        SetStatus(Loc.F("Copied path: {0}", selection[0].FullPath));
    }
}
