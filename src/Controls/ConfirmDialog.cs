using System.Windows;
using System.Windows.Controls;

namespace Tfx;

public sealed class ConfirmDialog : ThemedDialog
{
    /// <param name="defaultToCancel">
    /// For irreversible actions (permanent delete): Cancel becomes the default
    /// and focused button, so a reflexive Enter right after the shortcut
    /// doesn't confirm the destruction. Confirming takes an explicit Tab/click.
    /// </param>
    public ConfirmDialog(string title, string message, string confirmText, bool defaultToCancel = false)
    {
        Title = title;
        MinWidth = 420;
        MaxWidth = 560;

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var body = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 16)
        };

        body.Children.Add(MakeMark("TfxAccent"));

        var text = new TextBlock
        {
            Text = message,
            MaxWidth = 460,
            TextWrapping = TextWrapping.Wrap
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TfxForeground");
        body.Children.Add(text);

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
            IsDefault = !defaultToCancel,
            MinWidth = 92,
            Margin = new Thickness(0, 0, 8, 0)
        };
        ok.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(ok);

        var cancel = new Button
        {
            Content = Loc.T("Cancel"),
            IsCancel = true,
            IsDefault = defaultToCancel,
            MinWidth = 92
        };
        buttons.Children.Add(cancel);

        Loaded += (_, _) => (defaultToCancel ? cancel : ok).Focus();

        Grid.SetRow(buttons, 1);
        root.Children.Add(buttons);
        Content = root;
    }
}
