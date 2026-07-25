using System.Windows;
using System.Windows.Controls;

namespace Tfx;

public sealed class NamePromptDialog : ThemedDialog
{
    private readonly TextBox _textBox;

    public NamePromptDialog(string title, string label, string defaultValue)
    {
        Title = title;
        MinWidth = 380;

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var labelBlock = MakeLabel(label);
        labelBlock.Margin = new Thickness(0, 0, 0, 8);
        Grid.SetRow(labelBlock, 0);
        root.Children.Add(labelBlock);

        _textBox = MakeTextBox(defaultValue, 340);
        Grid.SetRow(_textBox, 1);
        root.Children.Add(_textBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };

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

        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);
        Content = root;

        Loaded += (_, _) =>
        {
            _textBox.Focus();
            _textBox.SelectAll();
        };
    }

    public string EnteredText => _textBox.Text;
}
