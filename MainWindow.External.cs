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
        var argument = selected is null ? GetCurrentPath(_activeGrid) : $"/select,\"{selected.FullPath}\"";
        Process.Start(new ProcessStartInfo("explorer.exe", argument) { UseShellExecute = true });
    }

    private void ToggleHidden()
    {
        ShowHidden = !ShowHidden;
        Reload(LeftGrid);
        Reload(RightGrid);
        SetStatus(ShowHidden ? "Hidden files visible" : "Hidden files hidden");
    }

    private void Terminal_Click(object sender, RoutedEventArgs e) => OpenTerminal();

    private void Explorer_Click(object sender, RoutedEventArgs e) => RevealInExplorer();

    private void Hidden_Click(object sender, RoutedEventArgs e) => ToggleHidden();
}
