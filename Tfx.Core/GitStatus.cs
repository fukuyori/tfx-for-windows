namespace Tfx;

/// <summary>
/// Aggregate worktree status badge for a single file as displayed in tfx.
/// Combines the two characters from <c>git status --porcelain=v2</c> XY into
/// one user-visible code: prefers the worktree column over the index column.
/// </summary>
public enum GitFileStatus
{
    Unmodified,
    Modified,    // M
    Added,       // A (staged new file)
    Deleted,     // D
    Renamed,     // R
    Copied,      // C
    Untracked,   // ?
    Ignored,     // !
    Conflicted,  // U
}

public sealed record GitWorkingCopyStatus(
    string Root,
    string? Branch,
    IReadOnlyDictionary<string, GitFileStatus> Files);

/// <summary>
/// Line-oriented parser for <c>git status --porcelain=v2 --untracked-files=normal</c>.
/// </summary>
public static class GitStatusParser
{
    public static GitWorkingCopyStatus Parse(string root, string output)
    {
        var files = new Dictionary<string, GitFileStatus>(StringComparer.OrdinalIgnoreCase);
        string? branch = null;

        if (string.IsNullOrEmpty(output))
        {
            return new GitWorkingCopyStatus(root, null, files);
        }

        var lines = output.Split('\n');
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;

            // Branch header lines: "# branch.head main"
            if (line.StartsWith("# branch.head ", StringComparison.Ordinal))
            {
                var name = line["# branch.head ".Length..].Trim();
                if (!string.IsNullOrEmpty(name) && !name.Equals("(detached)", StringComparison.OrdinalIgnoreCase))
                {
                    branch = name;
                }
                continue;
            }

            if (line[0] == '#') continue;

            // Ordinary changed:    "1 XY sub mH mI mW hH hI path"
            // Renamed / copied:    "2 XY sub mH mI mW hH hI Xnnn path\torig"
            // Unmerged:            "u XY sub m1 m2 m3 mW h1 h2 h3 path"
            // Untracked:           "? path"
            // Ignored:             "! path"
            var type = line[0];
            switch (type)
            {
                case '?':
                {
                    var p = TakePath(line, after: 2);
                    if (p is not null) files[p] = GitFileStatus.Untracked;
                    break;
                }
                case '!':
                {
                    var p = TakePath(line, after: 2);
                    if (p is not null) files[p] = GitFileStatus.Ignored;
                    break;
                }
                case 'u':
                {
                    var p = TakePathFromOrdinaryFormat(line, fieldCountBeforePath: 10);
                    if (p is not null) files[p] = GitFileStatus.Conflicted;
                    break;
                }
                case '1':
                {
                    var xy = ExtractXy(line);
                    var path = TakePathFromOrdinaryFormat(line, fieldCountBeforePath: 8);
                    if (path is not null && xy is { } pair)
                    {
                        files[path] = AggregateXy(pair.X, pair.Y);
                    }
                    break;
                }
                case '2':
                {
                    var xy = ExtractXy(line);
                    // Type 2 has one extra field (Xnnn = R100 etc.) before path,
                    // and the path is "<path>\t<orig>" separated by a TAB.
                    var pathField = TakePathFromOrdinaryFormat(line, fieldCountBeforePath: 9);
                    if (pathField is null || xy is null) break;
                    var tab = pathField.IndexOf('\t');
                    var newPath = tab < 0 ? pathField : pathField[..tab];
                    if (!string.IsNullOrEmpty(newPath))
                    {
                        files[newPath] = AggregateXy(xy.Value.X, xy.Value.Y);
                    }
                    break;
                }
            }
        }

        return new GitWorkingCopyStatus(root, branch, files);
    }

    private static (char X, char Y)? ExtractXy(string line)
    {
        // "1 XY sub ..." or "u XY sub ..." — characters at positions 2 and 3.
        if (line.Length < 4) return null;
        return (line[2], line[3]);
    }

    private static string? TakePath(string line, int after)
    {
        if (line.Length <= after) return null;
        return line[after..];
    }

    private static string? TakePathFromOrdinaryFormat(string line, int fieldCountBeforePath)
    {
        // Skip `fieldCountBeforePath` space-separated tokens, then return the
        // remainder (which is the path; may contain spaces).
        var pos = 0;
        for (var i = 0; i < fieldCountBeforePath; i++)
        {
            var sp = line.IndexOf(' ', pos);
            if (sp < 0) return null;
            pos = sp + 1;
        }
        if (pos >= line.Length) return null;
        return line[pos..];
    }

    private static GitFileStatus AggregateXy(char x, char y)
    {
        // Prefer worktree status; fall back to index status.
        var pick = y != '.' && y != ' ' ? y : x;
        return pick switch
        {
            'M' => GitFileStatus.Modified,
            'A' => GitFileStatus.Added,
            'D' => GitFileStatus.Deleted,
            'R' => GitFileStatus.Renamed,
            'C' => GitFileStatus.Copied,
            'U' => GitFileStatus.Conflicted,
            _ => GitFileStatus.Unmodified,
        };
    }

    public static string Badge(GitFileStatus status) => status switch
    {
        GitFileStatus.Modified => "M",
        GitFileStatus.Added => "A",
        GitFileStatus.Deleted => "D",
        GitFileStatus.Renamed => "R",
        GitFileStatus.Copied => "C",
        GitFileStatus.Untracked => "?",
        GitFileStatus.Ignored => "!",
        GitFileStatus.Conflicted => "U",
        _ => "",
    };
}
