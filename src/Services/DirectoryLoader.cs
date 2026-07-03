using System.IO;
using Path = System.IO.Path;

namespace Tfx;

internal sealed record DirectoryLoadOptions(
    bool ShowHidden,
    bool LoadSmallIcons,
    bool LoadLargeIcons,
    bool IncludeOwner);

internal static class DirectoryLoader
{
    public static List<FileItem> Load(string path, DirectoryLoadOptions options, CancellationToken cancellationToken)
    {
        using var _ = PerformanceTrace.Begin($"DirectoryLoader.Load({Path.GetFileName(path)})");

        if (ArchivePath.TryParse(path, out var archiveFile, out var innerPath))
        {
            return ArchiveBrowser.Load(archiveFile, innerPath, options, cancellationToken);
        }

        var items = new List<FileItem>();

        var parent = Directory.GetParent(path);
        if (parent is not null)
        {
            items.Add(FileItem.Parent(parent.FullName, options.LoadSmallIcons, options.LoadLargeIcons));
        }

        // One FindFirstFile-backed pass that surfaces every entry with its
        // attributes, size and timestamps already populated. Building rows from
        // these infos avoids the per-entry stat calls (hidden check + FileInfo
        // metadata) that dominate load time on large folders and SMB shares.
        var directories = new List<DirectoryInfo>();
        var files = new List<FileInfo>();
        try
        {
            var enumerationOptions = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.None,
            };
            foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos("*", enumerationOptions))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!options.ShowHidden && FsHelpers.IsHidden(entry))
                {
                    continue;
                }

                if (entry is DirectoryInfo directory)
                {
                    directories.Add(directory);
                }
                else if (entry is FileInfo file)
                {
                    files.Add(file);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Unreadable / vanished folder: show what we have (usually just "..").
        }

        directories.Sort(static (a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name));
        files.Sort(static (a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name));

        foreach (var directory in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            items.Add(FileItem.FromDirectory(directory, options.LoadSmallIcons, options.LoadLargeIcons, options.IncludeOwner));
        }

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            items.Add(FileItem.FromFile(file, options.LoadSmallIcons, options.LoadLargeIcons, options.IncludeOwner));
        }

        return items;
    }
}
