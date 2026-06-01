using System.Diagnostics;
using System.IO;
using Path = System.IO.Path;

namespace Tfx;

/// <summary>
/// Evaluates user-defined <see cref="UserCommand"/> entries against the current
/// selection and launches matching ones as external processes (fire-and-forget).
/// This is the shell-script alternative to an embedded scripting runtime: tfx
/// hands the work off to whatever interpreter the user names in <c>run</c>.
/// </summary>
internal static class CommandRunner
{
    /// <summary>
    /// True when <paramref name="command"/> should appear in the current context.
    /// <paramref name="selection"/> excludes the ".." parent row (may be empty).
    /// <paramref name="isGitRepo"/> reports whether the current folder is inside a
    /// Git working copy, for the <c>requireGit</c> filter.
    /// </summary>
    public static bool Matches(UserCommand command, IReadOnlyList<FileItem> selection, bool isGitRepo)
    {
        if (command.RequireGit && !isGitRepo)
        {
            return false;
        }

        // target = "current" ignores the selection and acts on the current folder
        // (e.g. git push from the empty area). It needs no items selected.
        if (command.Target == "current")
        {
            return true;
        }

        if (selection.Count == 0)
        {
            return false;
        }

        // Selection-count filter.
        if (command.Selection == "single" && selection.Count != 1)
        {
            return false;
        }
        if (command.Selection == "multiple" && selection.Count < 2)
        {
            return false;
        }

        foreach (var item in selection)
        {
            // Target filter (file / folder).
            if (command.Target == "file" && item.IsDirectory)
            {
                return false;
            }
            if (command.Target == "folder" && !item.IsDirectory)
            {
                return false;
            }

            // Extension filter (files only; folders have no extension to match).
            if (HasExtensionFilter(command) && !item.IsDirectory)
            {
                var ext = Path.GetExtension(item.Name).TrimStart('.').ToLowerInvariant();
                if (!command.Extensions.Contains(ext))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool HasExtensionFilter(UserCommand command) =>
        command.Extensions.Count > 0 && !command.Extensions.Contains("*");

    /// <summary>
    /// Builds the fully-substituted command line for <paramref name="command"/>.
    /// Tokens (path-like tokens are quoted automatically):
    /// <list type="bullet">
    /// <item><c>{path}</c> — first selected item's full path (or cwd if none).</item>
    /// <item><c>{paths}</c> — all selected items, space-separated.</item>
    /// <item><c>{dir}</c> — the folder the item sits in (parent of the first item).</item>
    /// <item><c>{name}</c> — file name with extension (e.g. <c>report.pdf</c>).</item>
    /// <item><c>{stem}</c> — file name without extension (e.g. <c>report</c>).</item>
    /// <item><c>{ext}</c> — extension without the dot (e.g. <c>pdf</c>); empty for folders.</item>
    /// <item><c>{cwd}</c> — current folder, regardless of selection.</item>
    /// <item><c>{scripts}</c> — the user's scripts folder (substituted raw, unquoted).</item>
    /// </list>
    /// </summary>
    public static string BuildCommandLine(UserCommand command, IReadOnlyList<FileItem> selection, string cwd, string scriptsDir)
    {
        var first = selection.Count > 0 ? selection[0].FullPath : cwd;
        var dir = selection.Count > 0 ? (Path.GetDirectoryName(first) ?? first) : cwd;
        var paths = selection.Count > 0
            ? string.Join(" ", selection.Select(i => Quote(i.FullPath)))
            : Quote(cwd);
        var name = Path.GetFileName(first);
        var stem = Path.GetFileNameWithoutExtension(first);
        var ext = Path.GetExtension(first).TrimStart('.');

        // Substitute {scripts} before env expansion so a scripts path containing
        // a literal '%' can't be misread as an environment variable.
        var commandLine = command.Run
            .Replace("{scripts}", scriptsDir, StringComparison.Ordinal);
        return Environment.ExpandEnvironmentVariables(commandLine)
            .Replace("{paths}", paths, StringComparison.Ordinal)
            .Replace("{path}", Quote(first), StringComparison.Ordinal)
            .Replace("{cwd}", Quote(cwd), StringComparison.Ordinal)
            .Replace("{dir}", Quote(dir), StringComparison.Ordinal)
            .Replace("{name}", Quote(name), StringComparison.Ordinal)
            .Replace("{stem}", Quote(stem), StringComparison.Ordinal)
            .Replace("{ext}", Quote(ext), StringComparison.Ordinal);
    }

    /// <summary>
    /// Launches the command as an external process (fire-and-forget via
    /// ShellExecute). <paramref name="cwd"/> is the current folder, used as the
    /// working directory and for the <c>{cwd}</c> token.
    /// </summary>
    public static bool Run(UserCommand command, IReadOnlyList<FileItem> selection, string cwd, string scriptsDir, string shellCommandLine, out string? error)
    {
        error = null;
        var workDir = WorkingDirectoryFor(selection, cwd);
        var commandLine = BuildCommandLine(command, selection, cwd, scriptsDir);

        // A single-line command is split into executable + arguments; a multi-line
        // command is written to a temp script and run with the command's own
        // `shell` if set, otherwise the configured [terminal] shell.
        var (exe, args) = ResolveInvocation(commandLine, command.Shell ?? shellCommandLine);
        if (string.IsNullOrWhiteSpace(exe))
        {
            error = "empty command";
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo(exe)
            {
                WorkingDirectory = workDir,
                UseShellExecute = true
            };
            if (!string.IsNullOrEmpty(args))
            {
                psi.Arguments = args;
            }
            Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Runs the command with its stdout/stderr redirected, streaming each line to
    /// <paramref name="onLine"/> (already marshalled is the caller's job). Used by
    /// the terminal pane's read-only Output tab. Returns false (with an error) if
    /// the process can't be started. Lines arrive on background threads.
    /// </summary>
    public static bool RunCaptured(
        UserCommand command,
        IReadOnlyList<FileItem> selection,
        string cwd,
        string scriptsDir,
        string shellCommandLine,
        Action<string> onLine,
        Action? onExit,
        out string? error)
    {
        error = null;
        var workDir = WorkingDirectoryFor(selection, cwd);
        var commandLine = BuildCommandLine(command, selection, cwd, scriptsDir);
        var (exe, args) = ResolveInvocation(commandLine, command.Shell ?? shellCommandLine);
        if (string.IsNullOrWhiteSpace(exe))
        {
            error = "empty command";
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo(exe)
            {
                Arguments = args,
                WorkingDirectory = workDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
            };
            var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) onLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) onLine(e.Data); };
            process.Exited += (_, _) =>
            {
                onExit?.Invoke();
                process.Dispose();
            };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Working directory for a command: the first selected item's folder, or the
    /// current folder when nothing is selected; falls back to the user profile if
    /// neither exists (e.g. inside a zip).
    /// </summary>
    private static string WorkingDirectoryFor(IReadOnlyList<FileItem> selection, string cwd)
    {
        var dir = selection.Count > 0
            ? (Path.GetDirectoryName(selection[0].FullPath) ?? cwd)
            : cwd;
        return Directory.Exists(dir) ? dir : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private static string Quote(string value) =>
        "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";

    /// <summary>
    /// Resolves a command body into (executable, arguments). A single-line body
    /// is split into program + args. A multi-line body is treated as a script for
    /// the configured shell (<paramref name="shellCommandLine"/>, e.g. the
    /// <c>[terminal] shell</c>): it's written to a temp file with an extension and
    /// invoked in the form that shell expects (PowerShell → .ps1 + -File, cmd →
    /// .bat + /c, bash → .sh). Tokens are already substituted into the body.
    /// </summary>
    private static (string Executable, string Arguments) ResolveInvocation(string commandBody, string shellCommandLine)
    {
        if (!commandBody.Contains('\n', StringComparison.Ordinal))
        {
            return SplitCommandLine(commandBody);
        }

        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "tfx", "commands");
            Directory.CreateDirectory(dir);

            // The shell command line may carry default args (e.g. "powershell.exe
            // -NoLogo"); take the executable for kind detection.
            var (shellExe, _) = SplitCommandLine(shellCommandLine);
            if (string.IsNullOrWhiteSpace(shellExe))
            {
                shellExe = "powershell.exe";
            }
            var shellName = Path.GetFileNameWithoutExtension(shellExe).ToLowerInvariant();

            string ext, args;
            switch (shellName)
            {
                case "cmd":
                    ext = ".bat";
                    args = "/c \"{0}\"";
                    break;
                case "bash":
                case "sh":
                case "wsl":
                    ext = ".sh";
                    args = "\"{0}\"";
                    break;
                default: // powershell, pwsh, and anything else → PowerShell-style.
                    ext = ".ps1";
                    args = "-NoProfile -ExecutionPolicy Bypass -File \"{0}\"";
                    break;
            }

            // Stable name (overwritten each run) keeps the temp folder bounded.
            var scriptPath = Path.Combine(dir, "command" + ext);
            File.WriteAllText(scriptPath, commandBody, new System.Text.UTF8Encoding(false));
            return (shellExe, string.Format(args, scriptPath));
        }
        catch
        {
            // Fall back to single-line interpretation if the temp write fails.
            return SplitCommandLine(commandBody);
        }
    }

    /// <summary>
    /// Splits a command line into (executable, arguments). A leading double-quote
    /// keeps a spaced executable path intact; otherwise the first whitespace run
    /// separates the two.
    /// </summary>
    private static (string Executable, string Arguments) SplitCommandLine(string commandLine)
    {
        var s = commandLine.TrimStart();
        if (s.Length == 0)
        {
            return ("", "");
        }

        if (s[0] == '"')
        {
            var end = s.IndexOf('"', 1);
            if (end > 0)
            {
                var exe = s[1..end];
                var rest = s[(end + 1)..].TrimStart();
                return (exe, rest);
            }
            return (s.Trim('"'), "");
        }

        var space = s.IndexOf(' ');
        return space < 0 ? (s, "") : (s[..space], s[(space + 1)..].TrimStart());
    }
}
