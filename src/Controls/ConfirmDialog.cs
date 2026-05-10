using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Tfx;

public sealed class ConfirmDialog : Window
{
    public ConfirmDialog(string title, string message, string confirmText)
    {
        Title = title;
        Owner = Application.Current.MainWindow;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        MinWidth = 420;
        MaxWidth = 560;
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
            Foreground = new SolidColorBrush(Color.FromRgb(125, 211, 252))
        };
        body.Children.Add(mark);

        body.Children.Add(new TextBlock
        {
            Text = message,
            MaxWidth = 460,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Foreground
        });

        Grid.SetRow(body, 0);
        root.Children.Add(body);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var ok = new Button
        {
            Content = confirmText,
            IsDefault = true,
            MinWidth = 92,
            Margin = new Thickness(0, 0, 8, 0)
        };
        ok.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(ok);

        var cancel = new Button
        {
            Content = Loc.T("Cancel"),
            IsCancel = true,
            MinWidth = 92
        };
        buttons.Children.Add(cancel);

        Grid.SetRow(buttons, 1);
        root.Children.Add(buttons);
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
