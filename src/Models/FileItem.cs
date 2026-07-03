using System.ComponentModel;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Windows.Media;

namespace Tfx;

public sealed class FileItem : INotifyPropertyChanged
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required string Kind { get; init; }
    public bool IsDirectory { get; init; }
    public bool IsParent { get; init; }
    public DateTime Created { get; init; }
    public string CreatedText { get; init; } = "";
    public ImageSource? Icon { get; init; }
    public ImageSource? LargeIcon { get; init; }

    /// <summary>
    /// Monochrome, theme-colored glyph (Segoe MDL2 Assets / Segoe Fluent Icons)
    /// shown in the file list and icon view instead of the Windows shell bitmap.
    /// Chosen by file kind so folders / text / code / media share one outline
    /// visual language.
    /// </summary>
    public string IconGlyph { get; init; } = DocumentGlyph;

    // These five change when a file is modified externally. They use backing
    // fields with INPC so the DataGrid re-renders the row when DiffApply detects
    // a metadata change and calls UpdateMutableFrom.
    private long _size;
    private DateTime _modified;
    private string _sizeText = "";
    private string _modifiedText = "";
    private string _ownerText = "";
    private string _attributeText = "";

    public long Size
    {
        get => _size;
        set { if (_size != value) { _size = value; Raise(nameof(Size)); } }
    }
    public DateTime Modified
    {
        get => _modified;
        set { if (_modified != value) { _modified = value; Raise(nameof(Modified)); } }
    }
    public string SizeText
    {
        get => _sizeText;
        set { if (_sizeText != value) { _sizeText = value; Raise(nameof(SizeText)); } }
    }
    public string ModifiedText
    {
        get => _modifiedText;
        set { if (_modifiedText != value) { _modifiedText = value; Raise(nameof(ModifiedText)); } }
    }
    public string OwnerText
    {
        get => _ownerText;
        set { if (_ownerText != value) { _ownerText = value; Raise(nameof(OwnerText)); } }
    }
    public string AttributeText
    {
        get => _attributeText;
        set { if (_attributeText != value) { _attributeText = value; Raise(nameof(AttributeText)); } }
    }

    private void Raise(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>
    /// Copies metadata that can change externally (size, modified time,
    /// attributes, owner) from <paramref name="source"/> into this instance,
    /// raising <see cref="PropertyChanged"/> for any field that actually
    /// differs. Called by <c>DiffApply</c> when a same-named row is seen with
    /// new file-system metadata, so the DataGrid re-renders that single row
    /// without losing selection / focus.
    /// </summary>
    public void UpdateMutableFrom(FileItem source)
    {
        Size = source.Size;
        Modified = source.Modified;
        SizeText = source.SizeText;
        ModifiedText = source.ModifiedText;
        OwnerText = source.OwnerText;
        AttributeText = source.AttributeText;
    }

    /// <summary>
    /// True when this row's externally-mutable metadata differs from
    /// <paramref name="other"/>'s. Drives the decision in
    /// <c>DiffApply</c> to call <see cref="UpdateMutableFrom"/>.
    /// </summary>
    public bool HasMutableDifferenceFrom(FileItem other) =>
        Size != other.Size
        || Modified != other.Modified
        || !string.Equals(SizeText, other.SizeText, StringComparison.Ordinal)
        || !string.Equals(ModifiedText, other.ModifiedText, StringComparison.Ordinal)
        || !string.Equals(OwnerText, other.OwnerText, StringComparison.Ordinal)
        || !string.Equals(AttributeText, other.AttributeText, StringComparison.Ordinal);

    private ImageSource? _thumbnail;

    /// <summary>
    /// Shell thumbnail shown by the icon view for image files. Loaded lazily
    /// (and evicted under memory pressure) by the thumbnail loader when the
    /// tile is realized; null shows the font glyph instead.
    /// </summary>
    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        set
        {
            if (!ReferenceEquals(_thumbnail, value))
            {
                _thumbnail = value;
                Raise(nameof(Thumbnail));
            }
        }
    }

    /// <summary>Set once a thumbnail load has been queued, so re-realizing the
    /// tile (scrolling back and forth) doesn't queue duplicates.</summary>
    internal bool ThumbnailRequested;

    private bool _isDropTarget;

    /// <summary>
    /// True while a drag is hovering this folder's name and a drop would move/copy
    /// into it. Bound by the Name column to highlight the folder. Transient UI
    /// state — not persisted.
    /// </summary>
    public bool IsDropTarget
    {
        get => _isDropTarget;
        set { if (_isDropTarget != value) { _isDropTarget = value; Raise(nameof(IsDropTarget)); } }
    }

    private string _gitStatusText = "";

    /// <summary>
    /// One-character Git status badge (e.g. "M", "?", "A") — set externally
    /// after a `git status` run; bound by the Git column in the file list.
    /// </summary>
    public string GitStatusText
    {
        get => _gitStatusText;
        set
        {
            if (_gitStatusText == value)
            {
                return;
            }
            _gitStatusText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GitStatusText)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // Segoe MDL2 Assets / Segoe Fluent Icons glyphs for the monochrome file-list
    // icons. Code points exist in both fonts so they render on Windows 10 and 11.
    private const string FolderGlyph = "";    // Folder
    private const string DocumentGlyph = "";  // Document (default file / text)

    /// <summary>Picks a glyph for a file name by extension category.</summary>
    internal static string GlyphForName(string name, bool isDirectory)
    {
        if (isDirectory)
        {
            return FolderGlyph;
        }

        var ext = Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
        return ext switch
        {
            "txt" or "md" or "markdown" or "log" or "rtf" or "csv" or "tsv"
                or "ini" or "cfg" or "conf" or "toml" or "yaml" or "yml"
                or "json" or "xml" or "doc" or "docx" or "odt" or "pdf" => DocumentGlyph,
            "cs" or "js" or "mjs" or "ts" or "jsx" or "tsx" or "py" or "java"
                or "c" or "cc" or "cpp" or "h" or "hpp" or "go" or "rs" or "rb"
                or "php" or "lua" or "sh" or "ps1" or "psm1" or "bat" or "cmd"
                or "html" or "htm" or "css" or "scss" or "sql" or "vb" => "",   // Code
            "png" or "jpg" or "jpeg" or "gif" or "bmp" or "webp" or "svg"
                or "ico" or "tif" or "tiff" or "heic" or "heif" => "",          // Photo2
            "mp3" or "wav" or "flac" or "aac" or "ogg" or "oga" or "m4a"
                or "wma" or "mid" or "midi" => "",                              // MusicNote
            "mp4" or "mkv" or "mov" or "avi" or "wmv" or "webm" or "flv"
                or "m4v" or "mpg" or "mpeg" => "",                              // Video
            "zip" or "7z" or "rar" or "gz" or "tar" or "tgz" or "bz2" or "xz" => "",  // ZipFolder
            _ => DocumentGlyph,
        };
    }

    public static FileItem Parent(string path, bool loadSmallIcon, bool loadLargeIcon) => new()
    {
        Name = "..",
        FullPath = path,
        Kind = Loc.T("Parent folder"),
        IsDirectory = true,
        IsParent = true,
        AttributeText = Loc.T("Directory"),
        IconGlyph = FolderGlyph,
        Icon = loadSmallIcon ? IconCache.GetFolderIcon() : null,
        LargeIcon = loadLargeIcon ? IconCache.GetFolderIconLarge() : null
    };

    public static FileItem FromDirectory(string path, bool loadSmallIcon, bool loadLargeIcon, bool includeOwner) =>
        FromDirectory(new DirectoryInfo(path), loadSmallIcon, loadLargeIcon, includeOwner);

    /// <summary>
    /// Builds a row from a <see cref="DirectoryInfo"/> that already carries its
    /// metadata (attributes, timestamps) — e.g. one produced by directory
    /// enumeration — so no extra file-system round trip happens per entry.
    /// </summary>
    public static FileItem FromDirectory(DirectoryInfo info, bool loadSmallIcon, bool loadLargeIcon, bool includeOwner)
    {
        var modified = SafeWriteTime(info);
        var created = SafeCreationTime(info);
        return new FileItem
        {
            Name = info.Name,
            FullPath = info.FullName,
            Kind = Loc.T("File folder"),
            IsDirectory = true,
            Modified = modified,
            Created = created,
            ModifiedText = FormatDate(modified),
            CreatedText = FormatDate(created),
            OwnerText = includeOwner ? SafeOwner(info) : "",
            AttributeText = FormatAttributes(info.Attributes),
            IconGlyph = FolderGlyph,
            Icon = loadSmallIcon ? IconCache.GetFolderIcon() : null,
            LargeIcon = loadLargeIcon ? IconCache.GetFolderIconLarge() : null
        };
    }

    public static FileItem FromArchiveEntry(
        string archiveFile,
        string entryPath,
        string name,
        bool isDirectory,
        long size,
        DateTime modified,
        bool loadSmallIcon,
        bool loadLargeIcon)
    {
        var fullPath = ArchivePath.Combine(archiveFile, entryPath);
        var ext = isDirectory ? "" : Path.GetExtension(name);
        var kind = isDirectory
            ? Loc.T("File folder")
            : string.IsNullOrWhiteSpace(ext)
                ? Loc.T("File")
                : Loc.F("{0} File", ext.TrimStart('.').ToUpperInvariant());
        return new FileItem
        {
            Name = name,
            FullPath = fullPath,
            Kind = kind,
            IsDirectory = isDirectory,
            Size = size,
            SizeText = isDirectory ? "" : FormatSize(size),
            Modified = modified,
            Created = DateTime.MinValue,
            ModifiedText = FormatDate(modified),
            CreatedText = "",
            OwnerText = "",
            AttributeText = isDirectory ? Loc.T("Directory") : "",
            IconGlyph = GlyphForName(name, isDirectory),
            Icon = isDirectory
                ? (loadSmallIcon ? IconCache.GetFolderIcon() : null)
                : (loadSmallIcon ? IconCache.GetFileIcon(name) : null),
            LargeIcon = isDirectory
                ? (loadLargeIcon ? IconCache.GetFolderIconLarge() : null)
                : (loadLargeIcon ? IconCache.GetFileIconLarge(name) : null)
        };
    }

    public static FileItem FromFile(string path, bool loadSmallIcon, bool loadLargeIcon, bool includeOwner) =>
        FromFile(new FileInfo(path), loadSmallIcon, loadLargeIcon, includeOwner);

    /// <summary>
    /// Builds a row from a <see cref="FileInfo"/> that already carries its
    /// metadata (size, attributes, timestamps) — e.g. one produced by directory
    /// enumeration — so no extra file-system round trip happens per entry.
    /// </summary>
    public static FileItem FromFile(FileInfo info, bool loadSmallIcon, bool loadLargeIcon, bool includeOwner)
    {
        var modified = SafeWriteTime(info);
        var created = SafeCreationTime(info);
        return new FileItem
        {
            Name = info.Name,
            FullPath = info.FullName,
            Kind = string.IsNullOrWhiteSpace(info.Extension)
                ? Loc.T("File")
                : Loc.F("{0} File", info.Extension.TrimStart('.').ToUpperInvariant()),
            Size = info.Length,
            SizeText = FormatSize(info.Length),
            Modified = modified,
            Created = created,
            ModifiedText = FormatDate(modified),
            CreatedText = FormatDate(created),
            OwnerText = includeOwner ? SafeOwner(info) : "",
            AttributeText = FormatAttributes(info.Attributes),
            IconGlyph = GlyphForName(info.Name, false),
            Icon = loadSmallIcon ? IconCache.GetFileIcon(info.FullName) : null,
            LargeIcon = loadLargeIcon ? IconCache.GetFileIconLarge(info.FullName) : null
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
