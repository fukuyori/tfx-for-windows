using System.IO;
using System.Runtime.InteropServices;
using Path = System.IO.Path;

namespace Tfx;

internal static class FsHelpers
{
    public static void CreateShortcut(string sourcePath, string lnkPath)
    {
        // Reject anything that isn't a real existing file or directory before
        // we hand it to WScript.Shell. Without this check, a malicious source
        // (e.g. a forged FileDrop from another process) could persist arbitrary
        // command strings like `cmd.exe /c calc & ...` into the saved .lnk —
        // any user later opening the shortcut would run those.
        if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
        {
            throw new FileNotFoundException("Shortcut target does not exist.", sourcePath);
        }

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
            return File.GetAttributes(path).HasFlag(FileAttributes.Hidden) || IsDotHidden(path);
        }
        catch
        {
            return IsDotHidden(path);
        }
    }

    /// <summary>
    /// Hidden check for an entry produced by directory enumeration, whose
    /// attributes are already populated — no file-system round trip.
    /// </summary>
    public static bool IsHidden(FileSystemInfo info)
    {
        try
        {
            if ((info.Attributes & FileAttributes.Hidden) != 0)
            {
                return true;
            }
        }
        catch
        {
        }
        return info.Name.Length > 1 && info.Name.StartsWith('.');
    }

    private static bool IsDotHidden(string path)
    {
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return name.Length > 1 && name.StartsWith('.');
    }

    private static readonly string[] ReservedDeviceNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    /// <summary>
    /// Validates a user-entered file name as a single path component. Rejects
    /// names Windows can't create (invalid characters, reserved device names,
    /// trailing period/space) and — because separators are invalid characters —
    /// anything like <c>..\x</c> or <c>sub\x</c> that would silently escape the
    /// folder once handed to <see cref="Path.Combine(string, string)"/>.
    /// </summary>
    public static bool IsValidFileName(string name, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(name))
        {
            error = Loc.T("Name is empty");
            return false;
        }
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            error = Loc.T("Names can't contain any of \\ / : * ? \" < > |");
            return false;
        }
        if (name[^1] is '.' or ' ')
        {
            error = Loc.T("Names can't end with a period or space");
            return false;
        }
        var stem = name.Split('.', 2)[0].TrimEnd(' ');
        if (ReservedDeviceNames.Contains(stem, StringComparer.OrdinalIgnoreCase))
        {
            error = Loc.F("\"{0}\" is a reserved device name", stem);
            return false;
        }
        return true;
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

    /// <summary>
    /// Reads the file's Zone.Identifier alternate data stream (Mark of the
    /// Web), or null when absent / unreadable (e.g. non-NTFS volume).
    /// </summary>
    public static string? ReadZoneIdentifier(string path)
    {
        try
        {
            var text = File.ReadAllText(path + ":Zone.Identifier");
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Explorer-style Mark-of-the-Web propagation: a file extracted from a
    /// downloaded archive inherits the archive's zone, so SmartScreen and the
    /// "file from the internet" checks still apply when it is run. Best
    /// effort — ADS requires NTFS.
    /// </summary>
    public static void WriteZoneIdentifier(string path, string zoneContent)
    {
        try
        {
            File.WriteAllText(path + ":Zone.Identifier", zoneContent);
        }
        catch
        {
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

    public static bool IsPdf(string extension) => extension is ".pdf";

    public static bool IsText(string extension, string path)
    {
        if (extension is ".txt" or ".md" or ".json" or ".xml" or ".xaml" or ".cs" or ".ps1" or ".bat" or ".cmd" or ".log"
            or ".csv" or ".tsv" or ".html" or ".htm" or ".css" or ".js" or ".ts" or ".tsx" or ".jsx"
            or ".toml" or ".yaml" or ".yml" or ".ini" or ".cfg" or ".conf" or ".env"
            or ".py" or ".rb" or ".go" or ".rs" or ".java" or ".kt" or ".swift" or ".sql" or ".sh" or ".gitignore")
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
