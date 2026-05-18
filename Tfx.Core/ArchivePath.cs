using Path = System.IO.Path;

namespace Tfx;

public static class ArchivePath
{
    public const string Separator = "::";

    public static bool Contains(string? path) =>
        !string.IsNullOrEmpty(path) && path.Contains(Separator, StringComparison.Ordinal);

    public static bool TryParse(string? path, out string archiveFile, out string innerPath)
    {
        archiveFile = "";
        innerPath = "";
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        var idx = path.IndexOf(Separator, StringComparison.Ordinal);
        if (idx < 0)
        {
            return false;
        }

        archiveFile = path[..idx];
        innerPath = path[(idx + Separator.Length)..].Replace('\\', '/');
        return true;
    }

    public static string Combine(string archiveFile, string innerPath)
    {
        var normalized = (innerPath ?? "").Replace('\\', '/').TrimStart('/');
        return archiveFile + Separator + normalized;
    }

    public static bool IsArchiveRoot(string? path) =>
        TryParse(path, out _, out var inner) && string.IsNullOrEmpty(inner);

    public static string? GetParent(string archivePath)
    {
        if (!TryParse(archivePath, out var file, out var inner))
        {
            return null;
        }

        if (string.IsNullOrEmpty(inner))
        {
            return Path.GetDirectoryName(file);
        }

        var trimmed = inner.TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');
        var parentInner = slash < 0 ? "" : trimmed[..slash] + "/";
        return Combine(file, parentInner);
    }

    public static bool IsZipFile(string path) =>
        Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase);
}
