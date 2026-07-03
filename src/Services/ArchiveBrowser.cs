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
        // Propagate the archive's Mark of the Web (if any) onto every file we
        // extract — entries opened straight from a downloaded zip keep the
        // SmartScreen / attachment checks Explorer extraction would give them.
        var zone = FsHelpers.ReadZoneIdentifier(archiveFile);
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
                var rootDirFull = Path.GetFullPath(rootDir);

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
                    // Zip-Slip guard: reject entries that try to escape the
                    // per-archive temp folder via `..\` or absolute paths.
                    // Without this check, `..\..\Windows\System32\evil.exe`
                    // inside a zip would be written anywhere the user can.
                    if (!IsPathInside(target, rootDirFull))
                    {
                        continue;
                    }
                    if (full.EndsWith('/'))
                    {
                        Directory.CreateDirectory(target);
                    }
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        entry.ExtractToFile(target, overwrite: true);
                        if (zone is not null)
                        {
                            FsHelpers.WriteZoneIdentifier(target, zone);
                        }
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

                // Single-entry path is also placed under destFolder; SanitizeName
                // strips invalid filename characters, but we still check the final
                // resolved path stays inside the temp directory.
                var target = Path.Combine(destFolder, name);
                if (!IsPathInside(target, Path.GetFullPath(destFolder)))
                {
                    continue;
                }
                entry.ExtractToFile(target, overwrite: true);
                if (zone is not null)
                {
                    FsHelpers.WriteZoneIdentifier(target, zone);
                }
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

    /// <summary>
    /// Returns true iff <paramref name="candidate"/>'s fully-resolved path stays
    /// inside <paramref name="rootFull"/>. Used as a Zip-Slip guard before we
    /// write any extracted entry to disk.
    /// </summary>
    private static bool IsPathInside(string candidate, string rootFull)
    {
        string resolved;
        try
        {
            resolved = Path.GetFullPath(candidate);
        }
        catch
        {
            return false;
        }
        var rootWithSep = rootFull.EndsWith(Path.DirectorySeparatorChar)
            ? rootFull
            : rootFull + Path.DirectorySeparatorChar;
        return resolved.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase);
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
