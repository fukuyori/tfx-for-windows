using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Windows.Media;

namespace Tfx;

public sealed class FileItem
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required string Kind { get; init; }
    public bool IsDirectory { get; init; }
    public bool IsParent { get; init; }
    public long Size { get; init; }
    public DateTime Modified { get; init; }
    public DateTime Created { get; init; }
    public string SizeText { get; init; } = "";
    public string ModifiedText { get; init; } = "";
    public string CreatedText { get; init; } = "";
    public string OwnerText { get; init; } = "";
    public string AttributeText { get; init; } = "";
    public ImageSource? Icon { get; init; }
    public ImageSource? LargeIcon { get; init; }

    public static FileItem Parent(string path) => new()
    {
        Name = "..",
        FullPath = path,
        Kind = "Parent folder",
        IsDirectory = true,
        IsParent = true,
        AttributeText = "Directory",
        Icon = IconCache.GetFolderIcon(),
        LargeIcon = IconCache.GetFolderIconLarge()
    };

    public static FileItem FromDirectory(string path)
    {
        var info = new DirectoryInfo(path);
        var modified = SafeWriteTime(info);
        var created = SafeCreationTime(info);
        return new FileItem
        {
            Name = info.Name,
            FullPath = info.FullName,
            Kind = "File folder",
            IsDirectory = true,
            Modified = modified,
            Created = created,
            ModifiedText = FormatDate(modified),
            CreatedText = FormatDate(created),
            OwnerText = SafeOwner(info),
            AttributeText = FormatAttributes(info.Attributes),
            Icon = IconCache.GetFolderIcon(),
            LargeIcon = IconCache.GetFolderIconLarge()
        };
    }

    public static FileItem FromFile(string path)
    {
        var info = new FileInfo(path);
        var modified = SafeWriteTime(info);
        var created = SafeCreationTime(info);
        return new FileItem
        {
            Name = info.Name,
            FullPath = info.FullName,
            Kind = string.IsNullOrWhiteSpace(info.Extension) ? "File" : $"{info.Extension.TrimStart('.').ToUpperInvariant()} File",
            Size = info.Length,
            SizeText = FormatSize(info.Length),
            Modified = modified,
            Created = created,
            ModifiedText = FormatDate(modified),
            CreatedText = FormatDate(created),
            OwnerText = SafeOwner(info),
            AttributeText = FormatAttributes(info.Attributes),
            Icon = IconCache.GetFileIcon(info.FullName),
            LargeIcon = IconCache.GetFileIconLarge(info.FullName)
        };
    }

    public static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var size = (double)bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{size:0.##} {units[unit]}";
    }

    private static string FormatDate(DateTime value) =>
        value == DateTime.MinValue ? "" : value.ToString("yyyy-MM-dd HH:mm:ss");

    private static DateTime SafeWriteTime(FileSystemInfo info)
    {
        try { return info.LastWriteTime; } catch { return DateTime.MinValue; }
    }

    private static DateTime SafeCreationTime(FileSystemInfo info)
    {
        try { return info.CreationTime; } catch { return DateTime.MinValue; }
    }

    private static string SafeOwner(FileSystemInfo info)
    {
        try
        {
            FileSystemSecurity security = info is DirectoryInfo directory
                ? directory.GetAccessControl()
                : ((FileInfo)info).GetAccessControl();
            return security.GetOwner(typeof(NTAccount))?.Value ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static string FormatAttributes(FileAttributes attributes)
    {
        var kind = attributes.HasFlag(FileAttributes.Directory) ? 'd' : '-';
        var readable = attributes.HasFlag(FileAttributes.Hidden) || attributes.HasFlag(FileAttributes.System) ? '-' : 'r';
        var writable = attributes.HasFlag(FileAttributes.ReadOnly) ? '-' : 'w';
        var executable = attributes.HasFlag(FileAttributes.Directory) ? 'x' : '-';
        return $"{kind}{readable}{writable}{executable}{readable}{writable}{executable}";
    }
}
