using System.IO;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Input;
using Path = System.IO.Path;

namespace Tfx;

public partial class MainWindow
{
    private static readonly string _appVersion = LoadAppVersion();

    private static string LoadAppVersion()
    {
        var attr = typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        var version = attr?.InformationalVersion ?? "";
        var plus = version.IndexOf('+');
        return plus >= 0 ? version[..plus] : version;
    }

    private void UpdatePathText()
    {
        if (!LeftPathBar.IsEditing)
        {
            LeftPathBar.Path = _leftPath;
        }

        if (!RightPathBar.IsEditing)
        {
            RightPathBar.Path = _rightPath;
        }

        var prefix = string.IsNullOrEmpty(_appVersion) ? "tfx" : $"tfx {_appVersion}";
        Title = $"{prefix} - {GetCurrentPath(_activeGrid)}";
        VersionStatusText.Text = _appVersion;

        // Dim the Back / Forward arrows when the active tab has no history in
        // that direction. The ToolbarButton style renders IsEnabled=false at
        // Opacity 0.38, giving the muted look without hiding the buttons.
        var tab = ActiveTab(ActivePane);
        BackButton.IsEnabled = tab.Back.Count > 0;
        ForwardButton.IsEnabled = tab.Forward.Count > 0;
    }

    private PathBar GetActivePathBar() => _activeGrid == LeftGrid ? LeftPathBar : RightPathBar;

    private void LeftPathBar_PathRequested(object? sender, string path) => HandlePathRequest(LeftGrid, path);

    private void RightPathBar_PathRequested(object? sender, string path) => HandlePathRequest(RightGrid, path);

    private void HandlePathRequest(DataGrid grid, string requested)
    {
        var expanded = Environment.ExpandEnvironmentVariables(requested);

        try
        {
            expanded = Path.GetFullPath(expanded);
        }
        catch
        {
            SetStatus(Loc.F("Invalid path: {0}", requested));
            return;
        }

        if (!Directory.Exists(expanded))
        {
            SetStatus(Loc.F("Folder not found: {0}", expanded));
            return;
        }

        Navigate(grid, expanded, true);
        grid.Focus();
    }
}
