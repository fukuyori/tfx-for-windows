using System.IO;
using System.Runtime.InteropServices;
using Path = System.IO.Path;

namespace Tfx;

internal static class FsHelpers
{
    public static void CreateShortcut(string sourcePath, string lnkPath)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("WScript.Shell is unavailable");

        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(lnkPath);

        try
        {
            shortcut.TargetPath = sourcePath;

            var workingDir = Directory.Exists(sourcePath)
                ? sourcePath
                : Path.GetDirectoryName(sourcePath);
            if (!string.IsNullOrEmpty(workingDir))
            {
                shortcut.WorkingDirectory = workingDir;
            }

            shortcut.Save();
        }
        finally
        {
            Marshal.FinalReleaseComObject(shortcut);
            Marshal.FinalReleaseComObject(shell);
        }
    }

    public static IEnumerable<string> SafeEnumerateDirectories(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path);
        }
        catch
        {
            return [];
        }
    }

    public static IEnumerable<string> SafeEnumerateFiles(string path)
    {
        try
        {
            return Directory.EnumerateFiles(path);
        }
        catch
        {
            return [];
        }
    }

    public static bool IsHidden(string path)
    {
        try
        {
            return File.GetAttributes(path).HasFlag(FileAttributes.Hidden);
        }
        catch
        {
            return false;
        }
    }

    public static string NextAvailablePath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path) ?? "";
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        for (var i = 2; ; i++)
        {
            var candidate = Path.Combine(directory, $"{name} ({i}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    public static bool SamePath(string left, string right)
    {
        try
        {
            var normalizedLeft = Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedRight = Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static bool IsImage(string extension) =>
        extension is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".tif" or ".tiff";

    public static bool IsText(string extension, string path)
    {
        if (extension is ".txt" or ".md" or ".json" or ".xml" or ".xaml" or ".cs" or ".ps1" or ".bat" or ".cmd" or ".log" or ".csv" or ".tsv" or ".html" or ".css" or ".js")
        {
            return true;
        }

        try
        {
            Span<byte> bytes = stackalloc byte[512];
            using var stream = File.OpenRead(path);
            var read = stream.Read(bytes);
            return !bytes[..read].Contains((byte)0);
        }
        catch
        {
            return false;
        }
    }
}
