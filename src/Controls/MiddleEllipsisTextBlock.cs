using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace Tfx;

public class MiddleEllipsisTextBlock : TextBlock
{
    private const string Ellipsis = "...";
    private const double Slack = 4.0;

    public static readonly DependencyProperty FullTextProperty =
        DependencyProperty.Register(
            nameof(FullText),
            typeof(string),
            typeof(MiddleEllipsisTextBlock),
            new FrameworkPropertyMetadata(default(string), OnFullTextChanged));

    public string? FullText
    {
        get => (string?)GetValue(FullTextProperty);
        set => SetValue(FullTextProperty, value);
    }

    private FrameworkElement? _widthSource;
    private string? _lastAppliedFullText;
    private double _lastAppliedWidth = double.NaN;

    public MiddleEllipsisTextBlock()
    {
        TextTrimming = TextTrimming.CharacterEllipsis;
        TextWrapping = TextWrapping.NoWrap;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private static void OnFullTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var t = (MiddleEllipsisTextBlock)d;
        t._lastAppliedFullText = null;
        t.ApplyTrim();
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        DetachWidthSource();
        _widthSource = FindWidthSource();
        if (_widthSource != null)
        {
            _widthSource.SizeChanged += OnWidthSourceSizeChanged;
            BindMaxWidth(_widthSource);
        }
        ApplyTrim();
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        DetachWidthSource();
    }

    private void DetachWidthSource()
    {
        if (_widthSource != null)
        {
            _widthSource.SizeChanged -= OnWidthSourceSizeChanged;
            _widthSource = null;
        }
        BindingOperations.ClearBinding(this, MaxWidthProperty);
    }

    private void BindMaxWidth(FrameworkElement source)
    {
        var binding = new Binding(nameof(ActualWidth))
        {
            Source = source,
            Mode = BindingMode.OneWay,
            Converter = new SubtractConverter(),
            ConverterParameter = Slack,
        };
        SetBinding(MaxWidthProperty, binding);
    }

    private void OnWidthSourceSizeChanged(object sender, SizeChangedEventArgs e) => ApplyTrim();

    private FrameworkElement? FindWidthSource()
    {
        DependencyObject? d = this;
        while (d != null)
        {
            d = VisualTreeHelper.GetParent(d);
            if (d is ContentPresenter cp)
            {
                return cp;
            }
            if (d is ListBoxItem lbi)
            {
                return lbi;
            }
        }
        return null;
    }

    private void ApplyTrim()
    {
        var full = FullText ?? string.Empty;
        var availableWidth = _widthSource is { ActualWidth: var w } && w > 0 ? w - Slack : double.NaN;

        if (double.IsNaN(availableWidth) || availableWidth <= 0)
        {
            if (Text != full)
            {
                Text = full;
            }
            _lastAppliedFullText = full;
            _lastAppliedWidth = availableWidth;
            return;
        }

        if (_lastAppliedFullText == full &&
            !double.IsNaN(_lastAppliedWidth) &&
            Math.Abs(_lastAppliedWidth - availableWidth) < 0.5)
        {
            return;
        }

        var trimmed = BuildTrimmed(full, availableWidth);
        if (Text != trimmed)
        {
            Text = trimmed;
        }
        _lastAppliedFullText = full;
        _lastAppliedWidth = availableWidth;
    }

    private string BuildTrimmed(string full, double width)
    {
        if (string.IsNullOrEmpty(full))
        {
            return full;
        }

        var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        if (MeasureWidth(full, typeface, pixelsPerDip) <= width)
        {
            return full;
        }

        if (MeasureWidth(Ellipsis, typeface, pixelsPerDip) > width)
        {
            return Ellipsis;
        }

        var n = full.Length;
        int lo = 0;
        int hi = n - 1;
        var best = Ellipsis;
        while (lo <= hi)
        {
            var keep = (lo + hi) / 2;
            var left = (keep + 1) / 2;
            var right = keep - left;
            var candidate = full[..left] + Ellipsis + full[^right..];
            if (MeasureWidth(candidate, typeface, pixelsPerDip) <= width)
            {
                best = candidate;
                lo = keep + 1;
            }
            else
            {
                hi = keep - 1;
            }
        }
        return best;
    }

    private double MeasureWidth(string text, Typeface typeface, double pixelsPerDip)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection,
            typeface,
            FontSize,
            Foreground,
            pixelsPerDip);
        return formatted.WidthIncludingTrailingWhitespace;
    }

    private sealed class SubtractConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double w && parameter is double sub)
            {
                return Math.Max(0.0, w - sub);
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }
}
