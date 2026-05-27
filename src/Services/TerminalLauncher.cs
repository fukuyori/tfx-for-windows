using System.Diagnostics;

namespace Tfx;

/// <summary>
/// Launches an external terminal at the given working directory.
/// Honors user-configured command / arguments and falls back to a sensible default.
/// </summary>
internal static class TerminalLauncher
{
    /// <summary>
    /// Token inside the arguments template that is replaced by the working directory.
    /// Wrapping it in quotes in the template (e.g. <c>-d "{path}"</c>) is supported.
    /// </summary>
    public const string PathToken = "{path}";

    public static bool Launch(string workingDirectory, string command, string arguments, out string? error)
    {
        error = null;

        var (exe, args) = ResolveCommand(command, arguments, workingDirectory);

        try
        {
            var psi = new ProcessStartInfo(exe)
            {
                WorkingDirectory = workingDirectory,
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
        }

        // Fallback so the user is never stuck with a broken config.
        try
        {
            Process.Start(new ProcessStartInfo("powershell.exe")
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception ex2)
        {
            error = ex2.Message;
            return false;
        }
    }

    /// <summary>
    /// Resolves the user-configured terminal command, or falls back to the auto-detected default.
    /// Returns (executable, arguments) ready to hand to <see cref="ProcessStartInfo"/>.
    /// </summary>
    public static (string Executable, string Arguments) ResolveCommand(string command, string arguments, string workingDirectory)
    {
        var trimmedCommand = (command ?? string.Empty).Trim();
        var trimmedArgs = (arguments ?? string.Empty).Trim();

        string exe;
        string args;

        if (trimmedCommand.Length == 0)
        {
            // Auto-detect: prefer Windows Terminal when present, else PowerShell.
            exe = Environment.GetEnvironmentVariable("WT_SESSION") is not null ? "wt.exe" : "powershell.exe";
            args = trimmedArgs;
        }
        else
        {
            exe = Environment.ExpandEnvironmentVariables(trimmedCommand);
            args = Environment.ExpandEnvironmentVariables(trimmedArgs);
        }

        if (!string.IsNullOrEmpty(args) && args.Contains(PathToken, StringComparison.Ordinal))
        {
            // Always quote the substituted working directory so a folder name
            // containing spaces, quotes, or `&` / `|` can't break out of the
            // argument and be re-interpreted as additional arguments or
            // commands. Escape any embedded `"` first (Windows command-line
            // rules: double the quote).
            var safe = "\"" + (workingDirectory ?? string.Empty).Replace("\"", "\"\"") + "\"";
            // If the template already wraps {path} in quotes, strip those so we
            // don't end up with nested ""..."" pairs.
            args = args
                .Replace("\"" + PathToken + "\"", safe, StringComparison.Ordinal)
                .Replace(PathToken, safe, StringComparison.Ordinal);
        }

        return (exe, args);
    }
}
