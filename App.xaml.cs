using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace Tfx;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        InstallCrashLogging();

        var options = StartupOptions.Parse(e.Args);
        if (options.ShowHelp || options.HasError)
        {
            options.WriteHelp();
            Shutdown(options.HasError ? 1 : 0);
            return;
        }

        new MainWindow(options).Show();
    }

    /// <summary>
    /// Logs unhandled exceptions to <c>%APPDATA%\tfx\crash.log</c> so crashes that
    /// otherwise terminate silently can be diagnosed. Covers UI-thread exceptions,
    /// fatal/native exceptions (AccessViolation etc. via the AppDomain hook), and
    /// unobserved task exceptions.
    /// </summary>
    private void InstallCrashLogging()
    {
        DispatcherUnhandledException += (_, args) =>
            LogCrash("DispatcherUnhandledException", args.Exception);

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            LogCrash("AppDomain.UnhandledException (terminating=" + args.IsTerminating + ")",
                args.ExceptionObject as Exception);

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
            LogCrash("UnobservedTaskException", args.Exception);
    }

    private static void LogCrash(string source, Exception? ex)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "tfx");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "crash.log");

            var entry =
                $"==== {DateTime.Now:yyyy-MM-dd HH:mm:ss} | {source} ===={Environment.NewLine}" +
                (ex?.ToString() ?? "(no exception object)") + Environment.NewLine + Environment.NewLine;

            File.AppendAllText(path, entry);
        }
        catch
        {
            // Never let the logger itself throw.
        }
    }
}
