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
            args = args.Replace(PathToken, workingDirectory, StringComparison.Ordinal);
        }

        return (exe, args);
    }
}
