using System.Diagnostics;
using System.Windows;

namespace Tfx;

public partial class MainWindow
{
    private void OpenTerminal()
    {
        var path = GetCurrentPath(_activeGrid);
        if (!TerminalLauncher.Launch(path, _settings.TerminalCommand, _settings.TerminalArguments, out var error))
        {
            SetStatus(Loc.F("Failed to launch terminal: {0}", error ?? string.Empty));
        }
    }

    private void OpenTerminalSettings()
    {
        var dialog = new TerminalSettingsDialog(_settings.TerminalCommand, _settings.TerminalArguments);
        if (dialog.ShowDialog() == true)
        {
            _settings.TerminalCommand = dialog.Command;
            _settings.TerminalArguments = dialog.Arguments;
            SaveSettings();
            SetStatus(Loc.T("Terminal settings updated"));
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
            IconViewOf(ActivePane).SelectAll();
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
