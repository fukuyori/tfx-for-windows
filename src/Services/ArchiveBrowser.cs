using System.IO;
using System.IO.Compression;
using Path = System.IO.Path;

namespace Tfx;

internal static class ArchiveBrowser
{
    public static List<FileItem> Load(string archiveFile, string innerPath, DirectoryLoadOptions options, CancellationToken cancellationToken)
    {
        var items = new List<FileItem>();
        var combined = ArchivePath.Combine(archiveFile, innerPath);
        var parent = ArchivePath.GetParent(combined);
        if (!string.IsNullOrEmpty(parent))
        {
            items.Add(FileItem.Parent(parent, options.LoadSmallIcons, options.LoadLargeIcons));
        }

        var prefix = string.IsNullOrEmpty(innerPath) ? "" : innerPath.TrimEnd('/') + "/";

        var subDirs = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        var files = new List<(string Name, string EntryName, long Size, DateTime Modified)>();

        using (var archive = ZipFile.OpenRead(archiveFile))
        {
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fullName = entry.FullName.Replace('\\', '/');
                if (!fullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var relative = fullName[prefix.Length..];
                if (string.IsNullOrEmpty(relative))
                {
                    continue;
                }

                var slash = relative.IndexOf('/');
                if (slash < 0 && !fullName.EndsWith("/"))
                {
                    files.Add((relative, fullName, entry.Length, entry.LastWriteTime.LocalDateTime));
                }
                else
                {
                    var dirName = slash < 0 ? relative.TrimEnd('/') : relative[..slash];
                    if (string.IsNullOrEmpty(dirName))
                    {
                        continue;
                    }

                    if (subDirs.TryGetValue(dirName, out var existing))
                    {
                        var candidate = entry.LastWriteTime.LocalDateTime;
                        if (candidate > existing)
                        {
                            subDirs[dirName] = candidate;
                        }
                    }
                    else
                    {
                        subDirs[dirName] = entry.LastWriteTime.LocalDateTime;
                    }
                }
            }
        }

        foreach (var pair in subDirs.OrderBy(p => p.Key, StringComparer.CurrentCultureIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entryInner = prefix + pair.Key + "/";
            items.Add(FileItem.FromArchiveEntry(
                archiveFile,
                entryInner,
                pair.Key,
                isDirectory: true,
                size: 0,
                modified: pair.Value,
                options.LoadSmallIcons,
                options.LoadLargeIcons));
        }

        foreach (var file in files.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            items.Add(FileItem.FromArchiveEntry(
                archiveFile,
                file.EntryName,
                file.Name,
                isDirectory: false,
                file.Size,
                file.Modified,
                options.LoadSmallIcons,
                options.LoadLargeIcons));
        }

        return items;
    }

    public static List<string> ExtractEntriesToTemp(string archiveFile, IEnumerable<string> entryPaths, string sessionTempRoot, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(sessionTempRoot);
        var destFolder = Path.Combine(sessionTempRoot, Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(destFolder);

        var result = new List<string>();
        using var archive = ZipFile.OpenRead(archiveFile);

        foreach (var raw in entryPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entryPath = raw.Replace('\\', '/');
            if (entryPath.EndsWith('/'))
            {
                var name = SanitizeName(Path.GetFileName(entryPath.TrimEnd('/')));
                if (string.IsNullOrEmpty(name))
                {
                    name = "folder";
                }

                var rootDir = Path.Combine(destFolder, name);
                Directory.CreateDirectory(rootDir);

                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var full = entry.FullName.Replace('\\', '/');
                    if (!full.StartsWith(entryPath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var rel = full[entryPath.Length..];
                    if (string.IsNullOrEmpty(rel))
                    {
                        continue;
                    }

                    var target = Path.Combine(rootDir, rel.Replace('/', Path.DirectorySeparatorChar));
                    if (full.EndsWith('/'))
                    {
                        Directory.CreateDirectory(target);
                    }
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        entry.ExtractToFile(target, overwrite: true);
                    }
                }

                result.Add(rootDir);
            }
            else
            {
                var entry = archive.GetEntry(entryPath);
                if (entry is null)
                {
                    continue;
                }

                var name = SanitizeName(Path.GetFileName(entryPath));
                if (string.IsNullOrEmpty(name))
                {
                    name = "file";
                }

                var target = Path.Combine(destFolder, name);
                entry.ExtractToFile(target, overwrite: true);
                result.Add(target);
            }
        }

        return result;
    }

    public static string ExtractEntryToTemp(string archiveFile, string entryPath, string sessionTempRoot, CancellationToken cancellationToken)
    {
        var results = ExtractEntriesToTemp(archiveFile, [entryPath], sessionTempRoot, cancellationToken);
        return results.Count > 0 ? results[0] : throw new FileNotFoundException(entryPath);
    }

    private static string SanitizeName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        return name;
    }
}
