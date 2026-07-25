using System.Windows;
using System.Windows.Controls;

namespace Tfx;

/// <summary>
/// Generic "command + arguments" settings dialog. Used for the external
/// terminal (Terminal Settings...) and the config editor (Editor Settings...);
/// the caller supplies the title, field labels, and hint lines.
/// </summary>
public sealed class CommandSettingsDialog : ThemedDialog
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
        MinWidth = 520;

        var root = new Grid { Margin = new Thickness(16) };
        for (var i = 0; i < 5 + hints.Count; i++)
        {
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        var row = 0;

        var commandLabelBlock = MakeLabel(commandLabel);
        Grid.SetRow(commandLabelBlock, row++);
        root.Children.Add(commandLabelBlock);

        _commandBox = MakeTextBox(initialCommand, 480);
        Grid.SetRow(_commandBox, row++);
        root.Children.Add(_commandBox);

        var argsLabel = MakeLabel(argumentsLabel);
        argsLabel.Margin = new Thickness(0, 14, 0, 6);
        Grid.SetRow(argsLabel, row++);
        root.Children.Add(argsLabel);

        _argsBox = MakeTextBox(initialArguments, 480);
        Grid.SetRow(_argsBox, row++);
        root.Children.Add(_argsBox);

        var firstHint = true;
        foreach (var hint in hints)
        {
            var hintBlock = MakeHint(hint, firstHint ? 10 : 4);
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
    }

    public string Command => _commandBox.Text?.Trim() ?? string.Empty;
    public string Arguments => _argsBox.Text?.Trim() ?? string.Empty;
}
