using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;

namespace Tfx;

internal static class PdfPreviewRenderer
{
    public static BitmapSource? TryRenderFirstPage(string path, int size, CancellationToken cancellationToken, out string? error)
    {
        error = null;
        cancellationToken.ThrowIfCancellationRequested();

        var pdftoppm = FindPdftoppm();
        if (pdftoppm is not null)
        {
            var rendered = TryRenderWithPdftoppm(pdftoppm, path, size, cancellationToken, out error);
            if (rendered is not null)
            {
                return rendered;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        var shellPreview = ShellThumbnail.TryGetThumbnail(path, size);
        if (shellPreview is not null)
        {
            error = null;
            return shellPreview;
        }

        error ??= Loc.T("No PDF renderer or thumbnail provider is available.");
        return null;
    }

    private static BitmapSource? TryRenderWithPdftoppm(string executable, string path, int size, CancellationToken cancellationToken, out string? error)
    {
        error = null;
        var tempDirectory = Path.Combine(Path.GetTempPath(), "tfx-pdf-preview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var outputBase = Path.Combine(tempDirectory, "page");
        var outputPng = outputBase + ".png";

        try
        {
            using var process = new Process();
            process.StartInfo.FileName = executable;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.ArgumentList.Add("-f");
            process.StartInfo.ArgumentList.Add("1");
            process.StartInfo.ArgumentList.Add("-l");
            process.StartInfo.ArgumentList.Add("1");
            process.StartInfo.ArgumentList.Add("-singlefile");
            process.StartInfo.ArgumentList.Add("-png");
            process.StartInfo.ArgumentList.Add("-scale-to");
            process.StartInfo.ArgumentList.Add(size.ToString());
            process.StartInfo.ArgumentList.Add(path);
            process.StartInfo.ArgumentList.Add(outputBase);

            process.Start();
            var deadline = DateTime.UtcNow.AddSeconds(8);
            while (!process.WaitForExit(100))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    process.Kill(entireProcessTree: true);
                    cancellationToken.ThrowIfCancellationRequested();
                }

                if (DateTime.UtcNow >= deadline)
                {
                    process.Kill(entireProcessTree: true);
                    error = Loc.T("PDF renderer timed out.");
                    return null;
                }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (process.ExitCode != 0 || !File.Exists(outputPng))
            {
                var stderr = process.StandardError.ReadToEnd().Trim();
                error = string.IsNullOrEmpty(stderr) ? Loc.F("PDF renderer exited with code {0}.", process.ExitCode) : stderr;
                return null;
            }

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(outputPng);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    private static string? FindPdftoppm()
    {
        var pathEntry = FindOnPath("pdftoppm.exe");
        if (pathEntry is not null)
        {
            return pathEntry;
        }

        foreach (var candidate in CommonPdftoppmPaths())
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? FindOnPath(string executable)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim(), executable);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
            }
        }

        return null;
    }

    private static IEnumerable<string> CommonPdftoppmPaths()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        if (!string.IsNullOrEmpty(programFiles))
        {
            yield return Path.Combine(programFiles, "Calibre2", "app", "bin", "pdftoppm.exe");
        }

        if (!string.IsNullOrEmpty(programFilesX86))
        {
            yield return Path.Combine(programFilesX86, "Calibre2", "app", "bin", "pdftoppm.exe");
        }
    }
}
