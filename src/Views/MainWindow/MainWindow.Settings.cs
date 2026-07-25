using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace Tfx;

public partial class MainWindow
{
    private static readonly TimeSpan ConfigReloadDebounce = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan ConfigReloadRetryDelay = TimeSpan.FromMilliseconds(200);
    private const int ConfigReloadMaxRetries = 3;

    private FileSystemWatcher? _configWatcher;
    private DispatcherTimer? _configReloadTimer;
    private int _configReloadRetries;
    private string? _lastConfigText;

    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettingsMenu();

    /// <summary>
    /// Drop-down under the title-bar gear button: the discoverable home for
    /// all settings actions (the file-pane context menu carries them too).
    /// </summary>
    private void OpenSettingsMenu()
    {
        var menu = new ContextMenu
        {
            PlacementTarget = SettingsButton,
            Placement = PlacementMode.Bottom
        };

        var edit = new MenuItem { Header = Loc.T("Edit config file..."), InputGestureText = ShortcutText("editConfig") };
        edit.Click += (_, _) => OpenConfigInEditor();
        menu.Items.Add(edit);

        var editor = new MenuItem { Header = Loc.T("Editor Settings...") };
        editor.Click += (_, _) => OpenEditorSettings();
        menu.Items.Add(editor);

        var terminal = new MenuItem { Header = Loc.T("Terminal Settings...") };
        terminal.Click += (_, _) => OpenTerminalSettings();
        menu.Items.Add(terminal);

        menu.IsOpen = true;
    }

    /// <summary>
    /// Opens config.toml in an editor. Priority: the editor configured via
    /// Editor Settings (settings.json) → the OS .toml association → Notepad.
    /// Saved edits are picked up live by the config watcher, so this is the
    /// app's "settings screen".
    /// </summary>
    private void OpenConfigInEditor()
    {
        // First run: materialize the commented template so the user edits a
        // complete, documented file rather than starting from nothing.
        try { _ = AppConfig.LoadOrCreate(_configPath); } catch { }

        var command = (_settings.EditorCommand ?? string.Empty).Trim();
        if (command.Length > 0)
        {
            var template = string.IsNullOrWhiteSpace(_settings.EditorArguments)
                ? TerminalLauncher.PathToken
                : _settings.EditorArguments;
            var (exe, args) = TerminalLauncher.ResolveCommand(command, template, _configPath);
            try
            {
                var psi = new ProcessStartInfo(exe) { UseShellExecute = true };
                if (!string.IsNullOrEmpty(args))
                {
                    psi.Arguments = args;
                }
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                // No silent fallback here — the user chose this editor, so a
                // broken configuration should be visible, not masked.
                SetStatus(Loc.F("Failed to open editor: {0}", ex.Message));
            }
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(_configPath) { UseShellExecute = true });
            return;
        }
        catch
        {
            // No .toml association — fall through to Notepad.
        }

        try
        {
            var notepad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe");
            Process.Start(new ProcessStartInfo(notepad) { Arguments = $"\"{_configPath}\"", UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SetStatus(Loc.F("Failed to open editor: {0}", ex.Message));
        }
    }

    private void OpenEditorSettings()
    {
        var dialog = new CommandSettingsDialog(
            Loc.T("Editor Settings"),
            Loc.T("Editor command (leave blank to use the OS default / Notepad)"),
            Loc.T("Arguments ({path} expands to the config file)"),
            [
                Loc.T("Examples: code / notepad++ / \"C:\\Program Files\\Vim\\vim91\\gvim.exe\""),
                Loc.T("Environment variables (e.g. %ProgramFiles%) are expanded."),
            ],
            _settings.EditorCommand,
            _settings.EditorArguments);
        if (dialog.ShowDialog() == true)
        {
            _settings.EditorCommand = dialog.Command;
            _settings.EditorArguments = dialog.Arguments;
            SaveSettings();
            SetStatus(Loc.T("Editor settings updated"));
        }
    }

    // ─── config.toml auto-reload ─────────────────────────────────────

    /// <summary>
    /// Watches config.toml and re-applies it as soon as it is saved, so edits
    /// take effect without a restart. Editors save in bursts (truncate+write,
    /// or write-temp + rename), hence the debounce, the Renamed subscription,
    /// and the locked-file retries in <see cref="ReloadConfigAndApply"/>.
    /// </summary>
    private void StartConfigWatcher()
    {
        var dir = Path.GetDirectoryName(_configPath);
        if (string.IsNullOrEmpty(dir))
        {
            return;
        }

        try
        {
            _lastConfigText = File.ReadAllText(_configPath, Encoding.UTF8);
        }
        catch
        {
            _lastConfigText = null;
        }

        var reloadTimer = new DispatcherTimer { Interval = ConfigReloadDebounce };
        reloadTimer.Tick += (_, _) =>
        {
            reloadTimer.Stop();
            // Disposed (window closing) between the queued tick and now — a
            // late watcher event must not reload against a closed window.
            if (_configReloadTimer is not null)
            {
                ReloadConfigAndApply();
            }
        };
        _configReloadTimer = reloadTimer;

        try
        {
            _configWatcher = new FileSystemWatcher(dir)
            {
                Filter = "config.toml",
                NotifyFilter = NotifyFilters.FileName
                    | NotifyFilters.LastWrite
                    | NotifyFilters.Size
                    | NotifyFilters.CreationTime,
            };
            _configWatcher.Changed += OnConfigFileEvent;
            _configWatcher.Created += OnConfigFileEvent;
            _configWatcher.Renamed += OnConfigFileEvent;
            _configWatcher.EnableRaisingEvents = true;
        }
        catch
        {
            _configWatcher = null; // best-effort; the startup load already applied
        }
    }

    private void OnConfigFileEvent(object sender, FileSystemEventArgs e)
    {
        // FileSystemWatcher raises on a worker thread; debounce on the UI thread.
        Dispatcher.BeginInvoke(() =>
        {
            _configReloadRetries = 0;
            if (_configReloadTimer is { } timer)
            {
                timer.Interval = ConfigReloadDebounce;
                timer.Stop();
                timer.Start();
            }
        });
    }

    private void DisposeConfigWatcher()
    {
        // Null the timer BEFORE stopping so a watcher event already queued on
        // the dispatcher (OnConfigFileEvent's BeginInvoke) can't restart it.
        var timer = _configReloadTimer;
        _configReloadTimer = null;
        timer?.Stop();
        if (_configWatcher is not null)
        {
            _configWatcher.EnableRaisingEvents = false;
            _configWatcher.Dispose();
            _configWatcher = null;
        }
    }

    private void ReloadConfigAndApply()
    {
        string text;
        try
        {
            text = File.ReadAllText(_configPath, Encoding.UTF8);
        }
        catch (FileNotFoundException)
        {
            return; // mid-rename during an atomic save; the rename event re-arms the timer
        }
        catch (IOException)
        {
            // The editor may still hold the file. Retry shortly, then give up
            // quietly — the next save fires the watcher again.
            if (_configReloadRetries < ConfigReloadMaxRetries && _configReloadTimer is { } timer)
            {
                _configReloadRetries++;
                timer.Interval = ConfigReloadRetryDelay;
                timer.Start();
            }
            return;
        }
        catch
        {
            return;
        }

        if (string.Equals(text, _lastConfigText, StringComparison.Ordinal))
        {
            return; // touched but unchanged
        }
        _lastConfigText = text;

        try
        {
            _config = AppConfig.Parse(text);
        }
        catch (Exception ex)
        {
            var broken = new AppConfig();
            broken.Errors.Add($"config.toml: {ex.Message}");
            _config = broken;
        }

        ApplyLoadedConfig();

        // Refresh the UI surfaces derived from config values: pane header
        // brushes, toolbar tooltips (embedded shortcut text), and the live
        // terminal theme. Context menus rebuild on open and need no refresh.
        UpdateActivePane(_activeGrid);
        ApplyLocalization();
        SendTerminalOptionsUpdate();

        if (_config.Errors.Count > 0)
        {
            ShowConfigErrors();
        }
        else
        {
            SetStatus(Loc.T("Config reloaded"));
        }
    }

    private const int ConfigErrorDialogMax = 10;
    private bool _configErrorDialogOpen;

    /// <summary>
    /// Reports config.toml problems in a dialog (all of them, capped at
    /// <see cref="ConfigErrorDialogMax"/>) plus the status bar. A status line
    /// alone is easy to miss while the user is editing in an external editor.
    /// </summary>
    private void ShowConfigErrors()
    {
        if (_config.Errors.Count == 0)
        {
            return;
        }

        SetStatus(Loc.F("Config warning: {0}", _config.Errors[0]));

        // Rapid consecutive saves must not stack modal dialogs; the reload that
        // ran behind the open dialog already updated the status bar.
        if (_configErrorDialogOpen)
        {
            return;
        }

        var lines = _config.Errors.Take(ConfigErrorDialogMax).ToList();
        if (_config.Errors.Count > lines.Count)
        {
            lines.Add(Loc.F("(+{0} more)", _config.Errors.Count - lines.Count));
        }

        _configErrorDialogOpen = true;
        try
        {
            new MessageDialog(Loc.T("Configuration errors"), string.Join("\n", lines)).ShowDialog();
        }
        finally
        {
            _configErrorDialogOpen = false;
        }
    }
}
