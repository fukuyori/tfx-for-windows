using System.Windows;

namespace Tfx;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var options = StartupOptions.Parse(e.Args);
        if (options.ShowHelp || options.HasError)
        {
            options.WriteHelp();
            Shutdown(options.HasError ? 1 : 0);
            return;
        }

        new MainWindow(options).Show();
    }
}

