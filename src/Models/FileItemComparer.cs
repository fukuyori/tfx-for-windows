using System.Collections;
using System.ComponentModel;
using System.Globalization;

namespace Tfx;

public sealed class FileItemComparer : IComparer
{
    // string.Compare(..., CurrentCultureIgnoreCase) resolves the thread's
    // current culture and its CompareInfo on every call. This comparer runs
    // O(n log n) times per header click / sorted reload, so cache the
    // CompareInfo once — same ordering, less per-call overhead.
    private static readonly CompareInfo Culture = CultureInfo.CurrentCulture.CompareInfo;
    private const CompareOptions IgnoreCase = CompareOptions.IgnoreCase;

    private readonly string _path;
    private readonly ListSortDirection _direction;

    public FileItemComparer(string path, ListSortDirection direction)
    {
        _path = path;
        _direction = direction;
    }

    public int Compare(object? x, object? y)
    {
        if (x is not FileItem a || y is not FileItem b)
        {
            return 0;
        }

        if (a.IsParent && !b.IsParent) return -1;
        if (!a.IsParent && b.IsParent) return 1;
        if (a.IsParent && b.IsParent) return 0;

        if (a.IsDirectory && !b.IsDirectory) return -1;
        if (!a.IsDirectory && b.IsDirectory) return 1;

        var cmp = _path switch
        {
            nameof(FileItem.Modified) => DateTime.Compare(a.Modified, b.Modified),
            nameof(FileItem.Created) => DateTime.Compare(a.Created, b.Created),
            nameof(FileItem.Size) => a.Size.CompareTo(b.Size),
            nameof(FileItem.Kind) => Culture.Compare(a.Kind, b.Kind, IgnoreCase),
            nameof(FileItem.OwnerText) => Culture.Compare(a.OwnerText, b.OwnerText, IgnoreCase),
            nameof(FileItem.AttributeText) => Culture.Compare(a.AttributeText, b.AttributeText, IgnoreCase),
            _ => Culture.Compare(a.Name, b.Name, IgnoreCase)
        };

        if (cmp == 0 && _path != nameof(FileItem.Name))
        {
            cmp = Culture.Compare(a.Name, b.Name, IgnoreCase);
        }

        return _direction == ListSortDirection.Ascending ? cmp : -cmp;
    }
}
