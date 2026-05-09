using System.Collections;
using System.ComponentModel;

namespace Tfx;

public sealed class FileItemComparer : IComparer
{
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
            nameof(FileItem.Kind) => string.Compare(a.Kind, b.Kind, StringComparison.CurrentCultureIgnoreCase),
            nameof(FileItem.OwnerText) => string.Compare(a.OwnerText, b.OwnerText, StringComparison.CurrentCultureIgnoreCase),
            nameof(FileItem.AttributeText) => string.Compare(a.AttributeText, b.AttributeText, StringComparison.CurrentCultureIgnoreCase),
            _ => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase)
        };

        if (cmp == 0 && _path != nameof(FileItem.Name))
        {
            cmp = string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
        }

        return _direction == ListSortDirection.Ascending ? cmp : -cmp;
    }
}
