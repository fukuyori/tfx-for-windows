using System.Diagnostics;
using System.IO;
using System.Text;
using Path = System.IO.Path;

namespace Tfx;

internal static class GitStatusReader
{
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
        try
        {
            using var process = new Process();
            process.StartInfo.FileName = "git";
            process.StartInfo.WorkingDirectory = root;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.StandardOutputEncoding = Encoding.UTF8;
            process.StartInfo.StandardErrorEncoding = Encoding.UTF8;
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
                if (!waitTask.Wait(TimeSpan.FromSeconds(8)))
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                    return null;
                }
            }
            catch (AggregateException ae) when (ae.InnerException is OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                cancellationToken.ThrowIfCancellationRequested();
            }

            cancellationToken.ThrowIfCancellationRequested();

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
