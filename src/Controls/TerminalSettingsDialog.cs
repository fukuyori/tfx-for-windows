using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Tfx;

public sealed class TerminalSettingsDialog : Window
{
    private readonly TextBox _commandBox;
    private readonly TextBox _argsBox;

    public TerminalSettingsDialog(string initialCommand, string initialArguments)
    {
        Title = Loc.T("Terminal Settings");
        Owner = Application.Current.MainWindow;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        MinWidth = 520;
        Background = new SolidColorBrush(Color.FromRgb(15, 19, 23));
        Foreground = new SolidColorBrush(Color.FromRgb(222, 230, 236));
        FontFamily = new FontFamily("Consolas, Yu Gothic UI");

        var root = new Grid { Margin = new Thickness(16) };
        for (var i = 0; i < 7; i++)
        {
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        var commandLabel = MakeLabel(Loc.T("Command (leave blank to auto-detect)"));
        Grid.SetRow(commandLabel, 0);
        root.Children.Add(commandLabel);

        _commandBox = MakeTextBox(initialCommand);
        Grid.SetRow(_commandBox, 1);
        root.Children.Add(_commandBox);

        var argsLabel = MakeLabel(Loc.T("Arguments ({path} expands to the current folder)"));
        argsLabel.Margin = new Thickness(0, 14, 0, 6);
        Grid.SetRow(argsLabel, 2);
        root.Children.Add(argsLabel);

        _argsBox = MakeTextBox(initialArguments);
        Grid.SetRow(_argsBox, 3);
        root.Children.Add(_argsBox);

        var hint = new TextBlock
        {
            Text = Loc.T("Examples: wt.exe / pwsh.exe -NoLogo / \"C:\\Program Files\\Git\\bin\\bash.exe\" --login -i"),
            Foreground = new SolidColorBrush(Color.FromRgb(143, 155, 168)),
            Margin = new Thickness(0, 10, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 480
        };
        Grid.SetRow(hint, 4);
        root.Children.Add(hint);

        var envHint = new TextBlock
        {
            Text = Loc.T("Environment variables (e.g. %ProgramFiles%) are expanded."),
            Foreground = new SolidColorBrush(Color.FromRgb(143, 155, 168)),
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 480
        };
        Grid.SetRow(envHint, 5);
        root.Children.Add(envHint);

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

        Grid.SetRow(buttons, 6);
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
