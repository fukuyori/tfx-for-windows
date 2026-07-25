using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Tfx;

/// <summary>
/// Simple OK-only message dialog (same look as <see cref="ConfirmDialog"/>).
/// Used to report config.toml errors, where a status-bar line is easy to miss
/// while the user is editing the file in an external editor.
/// </summary>
public sealed class MessageDialog : Window
{
    public MessageDialog(string title, string message)
    {
        Title = title;
        Owner = Application.Current.MainWindow;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        MinWidth = 420;
        MaxWidth = 640;
        Background = new SolidColorBrush(Color.FromRgb(15, 19, 23));
        Foreground = new SolidColorBrush(Color.FromRgb(222, 230, 236));
        FontFamily = new FontFamily("Consolas, Yu Gothic UI");

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var body = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 16)
        };

        var mark = new TextBlock
        {
            Text = "!",
            Width = 24,
            Height = 24,
            Margin = new Thickness(0, 1, 12, 0),
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            FontWeight = FontWeights.Bold,
            Background = new SolidColorBrush(Color.FromRgb(45, 53, 60)),
            Foreground = new SolidColorBrush(Color.FromRgb(252, 165, 165))
        };
        body.Children.Add(mark);

        // Scroll instead of growing past the screen when the config has many
        // errors.
        body.Children.Add(new ScrollViewer
        {
            MaxHeight = 320,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new TextBlock
            {
                Text = message,
                MaxWidth = 540,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Foreground
            }
        });

        Grid.SetRow(body, 0);
        root.Children.Add(body);

        var ok = new Button
        {
            Content = Loc.T("OK"),
            IsDefault = true,
            IsCancel = true,
            MinWidth = 92,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        ok.Click += (_, _) => DialogResult = true;
        Loaded += (_, _) => ok.Focus();

        Grid.SetRow(ok, 1);
        root.Children.Add(ok);
        Content = root;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
            }
        };
    }
}
