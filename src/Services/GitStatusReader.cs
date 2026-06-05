using System.Diagnostics;
using System.IO;
using System.Text;
using Path = System.IO.Path;

namespace Tfx;

internal static class GitStatusReader
{
    // Resolve `git.exe` once via PATH at first use, cache the absolute path.
    // Beyond this point we always invoke that pinned path, so a later
    // PATH-shadowing `git.exe` in the user's CWD or a writable PATH entry
    // can't hijack the call. Set to a sentinel "" if not found.
    private static string? _resolvedGitPath;
    private static readonly object GitPathLock = new();

    private static string? ResolveGitExe()
    {
        lock (GitPathLock)
        {
            if (_resolvedGitPath is not null)
            {
                return _resolvedGitPath.Length == 0 ? null : _resolvedGitPath;
            }
            try
            {
                var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
                foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                {
                    var candidate = Path.Combine(dir.Trim(), "git.exe");
                    if (File.Exists(candidate))
                    {
                        _resolvedGitPath = candidate;
                        return candidate;
                    }
                }
            }
            catch
            {
            }
            _resolvedGitPath = "";
            return null;
        }
    }

    /// <summary>
    /// Walks up from <paramref name="path"/> looking for a <c>.git</c> entry
    /// (directory or file). Returns the directory that contains <c>.git</c>,
    /// or <c>null</c> if not inside a Git working copy.
    /// </summary>
    public static string? FindRoot(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }
        if (ArchivePath.Contains(path))
        {
            return null;
        }

        try
        {
            var dir = new DirectoryInfo(path);
            while (dir is not null)
            {
                var git = Path.Combine(dir.FullName, ".git");
                if (Directory.Exists(git) || File.Exists(git))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
        }
        catch
        {
        }
        return null;
    }

    public static async Task<GitWorkingCopyStatus?> ReadAsync(string root, CancellationToken cancellationToken)
    {
        using var _ = PerformanceTrace.Begin($"git status({Path.GetFileName(root)})");
        var gitExe = ResolveGitExe();
        if (gitExe is null)
        {
            return null;
        }
        try
        {
            using var process = new Process();
            process.StartInfo.FileName = gitExe;
            process.StartInfo.WorkingDirectory = root;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.StandardOutputEncoding = Encoding.UTF8;
            process.StartInfo.StandardErrorEncoding = Encoding.UTF8;
            // Belt-and-braces hardening against malicious repository configs
            // (CVE-2022-24765 class). Disable filesystem-monitor hooks, all
            // hooks, file:// protocol fetches and prevent any executable lookup
            // from inside the repo via override config.
            process.StartInfo.ArgumentList.Add("-c");
            process.StartInfo.ArgumentList.Add("core.fsmonitor=");
            process.StartInfo.ArgumentList.Add("-c");
            process.StartInfo.ArgumentList.Add("core.hooksPath=NUL");
            process.StartInfo.ArgumentList.Add("-c");
            process.StartInfo.ArgumentList.Add("protocol.file.allow=never");
            process.StartInfo.ArgumentList.Add("-c");
            process.StartInfo.ArgumentList.Add("core.sshCommand=");
            process.StartInfo.ArgumentList.Add("-c");
            process.StartInfo.ArgumentList.Add("core.pager=cat");
            // -c core.quotepath=off avoids \xxx octal escapes for non-ASCII paths.
            process.StartInfo.ArgumentList.Add("-c");
            process.StartInfo.ArgumentList.Add("core.quotepath=off");
            process.StartInfo.ArgumentList.Add("status");
            process.StartInfo.ArgumentList.Add("--porcelain=v2");
            process.StartInfo.ArgumentList.Add("--branch");
            process.StartInfo.ArgumentList.Add("--untracked-files=normal");
            process.StartInfo.ArgumentList.Add("--no-renames");

            try
            {
                process.Start();
            }
            catch
            {
                // git not on PATH — silently disable Git features.
                return null;
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var waitTask = process.WaitForExitAsync(cancellationToken);

            try
            {
                // Await (never block) the exit, with an 8s cap. Blocking here with
                // waitTask.Wait(...) would stall the calling thread for the whole
                // duration of `git status`; since this is awaited from the UI
                // thread, that froze the window on slow/large repos.
                await waitTask.WaitAsync(TimeSpan.FromSeconds(8), cancellationToken);
            }
            catch (TimeoutException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return null;
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw;
            }

            if (process.ExitCode != 0)
            {
                return null;
            }

            var output = await stdoutTask;
            return GitStatusParser.Parse(root, output);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }
}
