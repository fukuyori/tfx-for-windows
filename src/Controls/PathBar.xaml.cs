using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Tfx;

public partial class PathBar : UserControl
{
    public event EventHandler<string>? PathRequested;

    private string _path = "";

    public PathBar()
    {
        InitializeComponent();
        ToolTip = Loc.T("Double-click or press Ctrl+L to edit path");
    }

    public string Path
    {
        get => _path;
        set
        {
            _path = value ?? "";
            RebuildCrumbs();
        }
    }

    public bool IsEditing => EditBox.Visibility == Visibility.Visible;

    public void EnterEditMode()
    {
        EditBox.Text = _path;
        CrumbHost.Visibility = Visibility.Collapsed;
        EditBox.Visibility = Visibility.Visible;
        EditBox.Focus();
        EditBox.SelectAll();
    }

    private void ExitEditMode()
    {
        EditBox.Visibility = Visibility.Collapsed;
        CrumbHost.Visibility = Visibility.Visible;
    }

    private void RebuildCrumbs()
    {
        CrumbPanel.Children.Clear();
        if (string.IsNullOrEmpty(_path))
        {
            return;
        }

        var crumbStyle = (Style)Resources["CrumbButtonStyle"];
        var mutedBrush = (Brush)Application.Current.Resources["TfxMuted"];

        var segments = SplitPath(_path);
        var accumulated = "";
        for (var i = 0; i < segments.Count; i++)
        {
            var (label, segment) = segments[i];
            accumulated = i == 0
                ? segment
                : System.IO.Path.Combine(accumulated, segment);

            var fullPath = accumulated;
            var button = new Button
            {
                Content = label,
                Style = crumbStyle,
                Tag = fullPath
            };
            button.Click += (_, _) => PathRequested?.Invoke(this, fullPath);
            CrumbPanel.Children.Add(button);

            if (i < segments.Count - 1)
            {
                CrumbPanel.Children.Add(new TextBlock
                {
                    Text = "›",
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = mutedBrush,
                    Margin = new Thickness(2, 0, 2, 0)
                });
            }
        }

        ShowPathEnd();
    }

    private static List<(string Label, string Segment)> SplitPath(string path)
    {
        var result = new List<(string, string)>();
        var root = System.IO.Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root))
        {
            return result;
        }

        var rootLabel = root.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        if (string.IsNullOrEmpty(rootLabel))
        {
            rootLabel = root;
        }
        result.Add((rootLabel, root));

        var rest = path.Length > root.Length ? path.Substring(root.Length) : "";
        var parts = rest.Split(
            new[] { System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            result.Add((part, part));
        }

        return result;
    }

    private void ClickCatcher_MouseDown(object sender, MouseButtonEventArgs e)
    {
        EnterEditMode();
        e.Handled = true;
    }

    private void CrumbScroll_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ShowPathEnd();
    }

    private void ShowPathEnd()
    {
        CrumbScroll.Dispatcher.BeginInvoke(
            () => CrumbScroll.ScrollToRightEnd(),
            DispatcherPriority.Loaded);
    }

    private void PathBar_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2 || IsEditing)
        {
            return;
        }

        EnterEditMode();
        e.Handled = true;
    }

    private void EditBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var text = (EditBox.Text ?? "").Trim();
            ExitEditMode();
            if (!string.IsNullOrEmpty(text))
            {
                PathRequested?.Invoke(this, text);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ExitEditMode();
            e.Handled = true;
        }
    }

    private void EditBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        ExitEditMode();
    }
}
