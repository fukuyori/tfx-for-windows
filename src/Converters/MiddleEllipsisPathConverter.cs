using System.Globalization;
using System.IO;
using System.Windows.Data;
using Path = System.IO.Path;

namespace Tfx;

public sealed class MiddleEllipsisPathConverter : IValueConverter
{
    private const int DefaultMaxLength = 20;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string path || path.Length < DefaultMaxLength)
        {
            return value;
        }

        var maxLength = DefaultMaxLength;
        if (parameter is string raw && int.TryParse(raw, out var parsed) && parsed > 8)
        {
            maxLength = parsed;
        }

        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var root = Path.GetPathRoot(trimmed)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var leaf = Path.GetFileName(trimmed);

        if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(leaf))
        {
            return ShortenMiddle(path, maxLength);
        }

        var separator = @" ... \";
        var leafBudget = maxLength - root.Length - separator.Length;
        if (leafBudget < 4)
        {
            return ShortenMiddle(path, maxLength);
        }

        if (leaf.Length > leafBudget)
        {
            leaf = leaf[..Math.Max(1, leafBudget - 3)] + "...";
        }

        return $"{root}{separator}{leaf}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;

    private static string ShortenMiddle(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        var leftLength = Math.Max(1, (maxLength - 5) / 2);
        var rightLength = Math.Max(1, maxLength - leftLength - 5);
        return $"{value[..leftLength]} ... {value[^rightLength..]}";
    }
}
