using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Tfx;

/// <summary>
/// Base for the small code-built dialogs (confirm / prompt / message /
/// command settings). Colors come from the Tfx* theme resources — via
/// SetResourceReference so a [colors] theme (and a live config.toml reload)
/// restyles the dialogs too — instead of the hardcoded dark palette each
/// dialog used to carry. Also applies the DWM title-bar theme and the shared
/// Escape-to-cancel behavior.
/// </summary>
public abstract class ThemedDialog : Window
{
    protected ThemedDialog()
    {
        Owner = Application.Current.MainWindow;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        FontFamily = new FontFamily("Consolas, Yu Gothic UI");
        SetResourceReference(BackgroundProperty, "TfxPanel");
        SetResourceReference(ForegroundProperty, "TfxForeground");
        SourceInitialized += (_, _) => WindowTheme.Apply(this);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
            }
        };
    }

    protected static TextBlock MakeLabel(string text)
    {
        var block = new TextBlock
        {
            Text = text,
            Margin = new Thickness(0, 0, 0, 6)
        };
        block.SetResourceReference(TextBlock.ForegroundProperty, "TfxForeground");
        return block;
    }

    protected static TextBlock MakeHint(string text, double topMargin = 4)
    {
        var block = new TextBlock
        {
            Text = text,
            Margin = new Thickness(0, topMargin, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 480
        };
        block.SetResourceReference(TextBlock.ForegroundProperty, "TfxMuted");
        return block;
    }

    protected static TextBox MakeTextBox(string text, double minWidth)
    {
        var box = new TextBox
        {
            Text = text ?? string.Empty,
            MinWidth = minWidth,
            Padding = new Thickness(6, 3, 6, 3)
        };
        box.SetResourceReference(Control.BackgroundProperty, "TfxInput");
        box.SetResourceReference(Control.ForegroundProperty, "TfxForeground");
        box.SetResourceReference(TextBox.CaretBrushProperty, "TfxAccent");
        box.SetResourceReference(Control.BorderBrushProperty, "TfxBorder");
        return box;
    }

    /// <summary>The "!" badge used by the confirm / message dialogs.</summary>
    protected static TextBlock MakeMark(string foregroundResource)
    {
        var mark = new TextBlock
        {
            Text = "!",
            Width = 24,
            Height = 24,
            Margin = new Thickness(0, 1, 12, 0),
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            FontWeight = FontWeights.Bold
        };
        mark.SetResourceReference(TextBlock.BackgroundProperty, "TfxBorder");
        mark.SetResourceReference(TextBlock.ForegroundProperty, foregroundResource);
        return mark;
    }
}
