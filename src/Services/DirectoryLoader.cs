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

        foreach (var directory in FsHelpers.SafeEnumerateDirectories(path).OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!options.ShowHidden && FsHelpers.IsHidden(directory))
            {
                continue;
            }

            items.Add(FileItem.FromDirectory(directory, options.LoadSmallIcons, options.LoadLargeIcons, options.IncludeOwner));
        }

        foreach (var file in FsHelpers.SafeEnumerateFiles(path).OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!options.ShowHidden && FsHelpers.IsHidden(file))
            {
                continue;
            }

            items.Add(FileItem.FromFile(file, options.LoadSmallIcons, options.LoadLargeIcons, options.IncludeOwner));
        }

        return items;
    }
}
