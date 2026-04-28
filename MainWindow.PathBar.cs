using System.IO;
using System.Reflection;
using System.Windows.Controls;
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
            SetStatus($"Invalid path: {requested}");
            return;
        }

        if (!Directory.Exists(expanded))
        {
            SetStatus($"Folder not found: {expanded}");
            return;
        }

        Navigate(grid, expanded, true);
        grid.Focus();
    }
}
