using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Tfx;

/// <summary>
/// Simple OK-only message dialog. Used to report config.toml errors, where a
/// status-bar line is easy to miss while the user is editing the file in an
/// external editor.
/// </summary>
public sealed class MessageDialog : ThemedDialog
{
    public MessageDialog(string title, string message)
    {
        Title = title;
        MinWidth = 420;
        MaxWidth = 640;

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var body = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 16)
        };

        // Error-red badge. There is no error token in the theme palette, so
        // this stays a fixed red rather than borrowing an unrelated resource.
        var mark = MakeMark("TfxAccent");
        mark.Foreground = new SolidColorBrush(Color.FromRgb(252, 165, 165));
        body.Children.Add(mark);

        var text = new TextBlock
        {
            Text = message,
            MaxWidth = 540,
            TextWrapping = TextWrapping.Wrap
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TfxForeground");

        // Scroll instead of growing past the screen when the config has many
        // errors.
        body.Children.Add(new ScrollViewer
        {
            MaxHeight = 320,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = text
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
    }
}
