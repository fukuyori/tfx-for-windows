using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Tfx;

/// <summary>
/// Generic "command + arguments" settings dialog. Used for the external
/// terminal (Terminal Settings...) and the config editor (Editor Settings...);
/// the caller supplies the title, field labels, and hint lines.
/// </summary>
public sealed class CommandSettingsDialog : Window
{
    private readonly TextBox _commandBox;
    private readonly TextBox _argsBox;

    public CommandSettingsDialog(
        string title,
        string commandLabel,
        string argumentsLabel,
        IReadOnlyList<string> hints,
        string initialCommand,
        string initialArguments)
    {
        Title = title;
        Owner = Application.Current.MainWindow;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        MinWidth = 520;
        Background = new SolidColorBrush(Color.FromRgb(15, 19, 23));
        Foreground = new SolidColorBrush(Color.FromRgb(222, 230, 236));
        FontFamily = new FontFamily("Consolas, Yu Gothic UI");

        var root = new Grid { Margin = new Thickness(16) };
        for (var i = 0; i < 5 + hints.Count; i++)
        {
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        var row = 0;

        var commandLabelBlock = MakeLabel(commandLabel);
        Grid.SetRow(commandLabelBlock, row++);
        root.Children.Add(commandLabelBlock);

        _commandBox = MakeTextBox(initialCommand);
        Grid.SetRow(_commandBox, row++);
        root.Children.Add(_commandBox);

        var argsLabel = MakeLabel(argumentsLabel);
        argsLabel.Margin = new Thickness(0, 14, 0, 6);
        Grid.SetRow(argsLabel, row++);
        root.Children.Add(argsLabel);

        _argsBox = MakeTextBox(initialArguments);
        Grid.SetRow(_argsBox, row++);
        root.Children.Add(_argsBox);

        var firstHint = true;
        foreach (var hint in hints)
        {
            var hintBlock = new TextBlock
            {
                Text = hint,
                Foreground = new SolidColorBrush(Color.FromRgb(143, 155, 168)),
                Margin = new Thickness(0, firstHint ? 10 : 4, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 480
            };
            firstHint = false;
            Grid.SetRow(hintBlock, row++);
            root.Children.Add(hintBlock);
        }

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };

        var reset = new Button
        {
            Content = Loc.T("Reset"),
            MinWidth = 76,
            Margin = new Thickness(0, 0, 8, 0)
        };
        reset.Click += (_, _) =>
        {
            _commandBox.Text = string.Empty;
            _argsBox.Text = string.Empty;
        };
        buttons.Children.Add(reset);

        var ok = new Button
        {
            Content = Loc.T("OK"),
            IsDefault = true,
            MinWidth = 76,
            Margin = new Thickness(0, 0, 8, 0)
        };
        ok.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(ok);

        var cancel = new Button
        {
            Content = Loc.T("Cancel"),
            IsCancel = true,
            MinWidth = 76
        };
        buttons.Children.Add(cancel);

        Grid.SetRow(buttons, row);
        root.Children.Add(buttons);

        Content = root;

        Loaded += (_, _) =>
        {
            _commandBox.Focus();
            _commandBox.SelectAll();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
            }
        };
    }

    public string Command => _commandBox.Text?.Trim() ?? string.Empty;
    public string Arguments => _argsBox.Text?.Trim() ?? string.Empty;

    private TextBlock MakeLabel(string text) => new()
    {
        Text = text,
        Margin = new Thickness(0, 0, 0, 6),
        Foreground = Foreground
    };

    private TextBox MakeTextBox(string text) => new()
    {
        Text = text ?? string.Empty,
        MinWidth = 480,
        Padding = new Thickness(6, 3, 6, 3),
        Background = new SolidColorBrush(Color.FromRgb(13, 16, 19)),
        Foreground = Foreground,
        CaretBrush = new SolidColorBrush(Color.FromRgb(126, 211, 164)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(47, 58, 67))
    };
}
