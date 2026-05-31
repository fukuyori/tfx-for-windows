using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace Tfx;

public partial class MainWindow
{
    private ConPty? _terminalPty;
    private bool _terminalStarted;
    private bool _terminalWebReady;        // xterm page loaded, waiting for init
    private bool _terminalInitSent;        // init message dispatched
    private bool _terminalPaneOpen;        // pane currently shown (gates PTY spawn)
    private short _terminalCols = 80;
    private short _terminalRows = 25;
    private Task<bool>? _terminalWebInit;

    /// <summary>
    /// Applies persisted terminal-pane state at startup. The actual xterm.js
    /// page is created lazily the first time the pane is shown.
    /// </summary>
    private void InitializeTerminalPane()
    {
        if (_settings.ShowTerminalPane)
        {
            SetTerminalVisible(true);
        }
        else
        {
            TerminalRow.Height = new GridLength(0);
        }
    }

    private void TerminalPane_Click(object sender, RoutedEventArgs e) => ToggleTerminalPane();

    private void ToggleTerminalPane()
    {
        SetTerminalVisible(TerminalHost.Visibility != Visibility.Visible);
        SaveSettings();
    }

    private void SetTerminalVisible(bool visible)
    {
        if (visible)
        {
            _terminalPaneOpen = true;
            var h = _settings.TerminalPaneHeight;
            TerminalRow.Height = new GridLength(h >= 80 ? h : 220, GridUnitType.Pixel);
            TerminalSplitter.Visibility = Visibility.Visible;
            TerminalHost.Visibility = Visibility.Visible;
            _ = EnsureTerminalStartedAsync();
            Dispatcher.BeginInvoke(() =>
            {
                // Re-opening after a previous close: the WebView/xterm page is
                // still alive but the PTY was torn down. Send "reset" so xterm
                // clears the old session and re-fits; its resize message then
                // spawns a fresh shell via StartOrResizePty.
                if (_terminalWebReady && _terminalPty is null)
                {
                    PostToTerminal(new { type = "reset" });
                }
                Terminal.Focus();
                PostToTerminal(new { type = "focus" });
            }, DispatcherPriority.Input);
        }
        else
        {
            _terminalPaneOpen = false;
            TerminalSplitter.Visibility = Visibility.Collapsed;
            TerminalHost.Visibility = Visibility.Collapsed;
            TerminalRow.Height = new GridLength(0);
            // Closing ends the shell session: tear down the PTY so the next
            // open starts a brand-new shell. The WebView2 page is kept (cheap
            // to reuse); we don't reset xterm here because it's not visible.
            DisposeTerminalIfAny();
        }
        TerminalPaneButton.IsChecked = visible;
    }

    /// <summary>
    /// Initializes the WebView2 (once), loads the bundled xterm.js page, and
    /// starts the shell. The PTY is created after the page reports its initial
    /// size so the pseudo console matches the rendered grid.
    /// </summary>
    private async Task EnsureTerminalStartedAsync()
    {
        if (_terminalStarted)
        {
            return;
        }
        _terminalStarted = true;

        try
        {
            await EnsureTerminalWebViewAsync();
        }
        catch (Exception ex)
        {
            SetStatus(Loc.F("Terminal failed to start: {0}", ex.Message));
            _terminalStarted = false;
        }
    }

    private Task<bool> EnsureTerminalWebViewAsync()
    {
        _terminalWebInit ??= InitTerminalWebViewAsync();
        return _terminalWebInit;
    }

    private async Task<bool> InitTerminalWebViewAsync()
    {
        await Terminal.EnsureCoreWebView2Async();
        var core = Terminal.CoreWebView2;

        var settings = core.Settings;
        settings.AreDevToolsEnabled = false;
        settings.AreDefaultContextMenusEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.IsZoomControlEnabled = false;

        // Serve the bundled xterm assets from a virtual host. Allow lets our own
        // page read its sibling files; the host name isn't real so cross-origin
        // requests can't reach it.
        var assetDir = Path.Combine(AppContext.BaseDirectory, "Assets", "terminal");
        core.SetVirtualHostNameToFolderMapping(
            "tfx.terminal", assetDir, CoreWebView2HostResourceAccessKind.Allow);

        core.WebMessageReceived += Terminal_WebMessageReceived;
        core.NavigationStarting += (_, e) =>
        {
            if (!e.Uri.StartsWith("https://tfx.terminal/", StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
            }
        };

        core.Navigate("https://tfx.terminal/terminal.html");
        return true;
    }

    /// <summary>Messages from the xterm.js page (loaded / ready / input / resize / error).</summary>
    private void Terminal_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string json;
        try { json = e.TryGetWebMessageAsString(); }
        catch { return; }

        TerminalMessage? msg;
        try { msg = JsonSerializer.Deserialize<TerminalMessage>(json); }
        catch { return; }
        if (msg is null)
        {
            return;
        }

        switch (msg.type)
        {
            case "loaded":
                _terminalWebReady = true;
                SendTerminalInit();
                break;
            case "resize":
                _terminalCols = (short)Math.Clamp(msg.cols, 1, 1000);
                _terminalRows = (short)Math.Clamp(msg.rows, 1, 1000);
                StartOrResizePty();
                break;
            case "input":
                if (!string.IsNullOrEmpty(msg.dataB64) && _terminalPty is not null)
                {
                    try
                    {
                        var bytes = Convert.FromBase64String(msg.dataB64);
                        _terminalPty.WriteBytes(bytes);
                    }
                    catch { }
                }
                break;
            case "ready":
                PostToTerminal(new { type = "focus" });
                break;
            case "error":
                SetStatus(Loc.F("Terminal failed to start: {0}", msg.message ?? "script error"));
                break;
        }
    }

    private sealed class TerminalMessage
    {
        public string type { get; set; } = "";
        public string? dataB64 { get; set; }
        public int cols { get; set; }
        public int rows { get; set; }
        public string? message { get; set; }
    }

    /// <summary>Sends the xterm init message with theme / font from config.toml.</summary>
    private void SendTerminalInit()
    {
        if (_terminalInitSent || !_terminalWebReady)
        {
            return;
        }
        _terminalInitSent = true;

        var theme = BuildTerminalTheme();
        var options = new
        {
            fontFamily = string.IsNullOrWhiteSpace(_config.Terminal.Font)
                ? "Cascadia Mono, Consolas, monospace"
                : _config.Terminal.Font,
            fontSize = _config.Terminal.FontSize ?? _settings.TerminalPaneFontSize,
            scrollback = 5000,
            // Transparent unless the user set an explicit background color.
            allowTransparency = !_config.Terminal.Colors.ContainsKey("background"),
            theme
        };
        PostToTerminal(new { type = "init", options });
    }

    /// <summary>
    /// Builds an xterm.js theme object from config.toml [terminal] colors,
    /// falling back to a Campbell-style default. Omitting background keeps it
    /// transparent so the WebView (and the window translucency) shows through.
    /// </summary>
    private Dictionary<string, string> BuildTerminalTheme()
    {
        var c = _config.Terminal.Colors;
        string Hex(string key, string fallback) =>
            c.TryGetValue(key, out var col) ? $"#{col.R:X2}{col.G:X2}{col.B:X2}" : fallback;

        var theme = new Dictionary<string, string>
        {
            ["foreground"] = Hex("foreground", "#CCCCCC"),
            ["cursor"] = Hex("cursor", "#7DD3FC"),
            ["black"] = Hex("black", "#0C0C0C"),
            ["red"] = Hex("red", "#C50F1F"),
            ["green"] = Hex("green", "#13A10E"),
            ["yellow"] = Hex("yellow", "#C19C00"),
            ["blue"] = Hex("blue", "#0037DA"),
            ["magenta"] = Hex("magenta", "#881798"),
            ["cyan"] = Hex("cyan", "#3A96DD"),
            ["white"] = Hex("white", "#CCCCCC"),
            ["brightBlack"] = Hex("brightBlack", "#5A5A5A"),
            ["brightRed"] = Hex("brightRed", "#E74856"),
            ["brightGreen"] = Hex("brightGreen", "#16C60C"),
            ["brightYellow"] = Hex("brightYellow", "#F9F1A5"),
            ["brightBlue"] = Hex("brightBlue", "#3B78FF"),
            ["brightMagenta"] = Hex("brightMagenta", "#B4009E"),
            ["brightCyan"] = Hex("brightCyan", "#61D6D6"),
            ["brightWhite"] = Hex("brightWhite", "#F2F2F2"),
        };
        // Background: explicit color → opaque; omitted → transparent so the
        // window's translucency shows through. xterm.js rejects 8-digit hex,
        // so transparency uses rgba() and the page sets allowTransparency.
        theme["background"] = c.ContainsKey("background")
            ? Hex("background", "#0C0C0C")
            : "rgba(0,0,0,0)";
        return theme;
    }

    private void PostToTerminal(object message)
    {
        if (Terminal.CoreWebView2 is null)
        {
            return;
        }
        try
        {
            Terminal.CoreWebView2.PostWebMessageAsString(JsonSerializer.Serialize(message));
        }
        catch { }
    }

    /// <summary>
    /// Starts the pseudo console once we know the grid size, or resizes the
    /// existing one. Called on every "resize" message from the page.
    /// </summary>
    private void StartOrResizePty()
    {
        if (_terminalPty is not null)
        {
            _terminalPty.Resize(_terminalCols, _terminalRows);
            return;
        }

        // Don't spawn a shell for resize messages that arrive while the pane is
        // closed (e.g. xterm re-fitting after a reset). A new shell is created
        // only when the pane is actually open.
        if (!_terminalPaneOpen)
        {
            return;
        }

        var cwd = ResolveTerminalCwd();
        var commandLine = ResolveTerminalShell();
        try
        {
            _terminalPty = new ConPty();
            _terminalPty.OutputReceived += OnTerminalPtyOutput;
            _terminalPty.Exited += OnTerminalExited;
            _terminalPty.Start(commandLine, cwd, _terminalCols, _terminalRows);
        }
        catch (Exception ex)
        {
            SetStatus(Loc.F("Terminal failed to start: {0}", ex.Message));
            _terminalPty?.Dispose();
            _terminalPty = null;
        }
    }

    /// <summary>PTY → xterm. Raw bytes are base64-encoded so they survive intact.</summary>
    private void OnTerminalPtyOutput(byte[] data)
    {
        var b64 = Convert.ToBase64String(data);
        Dispatcher.BeginInvoke(() => PostToTerminal(new { type = "output", dataB64 = b64 }));
    }

    private void OnTerminalExited()
    {
        Dispatcher.BeginInvoke(() =>
        {
            DisposeTerminalIfAny();
            SetTerminalVisible(false);
            _settings.ShowTerminalPane = false;
            SaveSettings();
        });
    }

    private string ResolveTerminalCwd()
    {
        var path = GetCurrentPath(_activeGrid);
        if (string.IsNullOrEmpty(path) || ArchivePath.Contains(path) || !Directory.Exists(path))
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        return path;
    }

    /// <summary>
    /// Shell command line for the built-in pane. Priority: config.toml
    /// [terminal] shell → PowerShell → %ComSpec% / cmd.
    /// </summary>
    private string ResolveTerminalShell()
    {
        var configured = _config.Terminal.Shell;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }
        var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var powershell = Path.Combine(system32, "WindowsPowerShell", "v1.0", "powershell.exe");
        if (File.Exists(powershell))
        {
            return powershell + " -NoLogo";
        }
        var comspec = Environment.GetEnvironmentVariable("ComSpec");
        return string.IsNullOrEmpty(comspec) ? "cmd.exe" : comspec;
    }

    /// <summary>
    /// Tears down the PTY (shell) only. The WebView2 page and its readiness
    /// flags are intentionally kept so the same xterm instance is reused on the
    /// next open; the caller sends a "reset" message to clear the screen and a
    /// fresh PTY is created from the page's next resize message.
    /// </summary>
    private void DisposeTerminalIfAny()
    {
        if (_terminalPty is not null)
        {
            _terminalPty.OutputReceived -= OnTerminalPtyOutput;
            _terminalPty.Exited -= OnTerminalExited;
            _terminalPty.Dispose();
            _terminalPty = null;
        }
    }

    /// <summary>Full teardown including WebView lifecycle flags (window close).</summary>
    private void ShutdownTerminal()
    {
        DisposeTerminalIfAny();
        _terminalStarted = false;
        _terminalWebReady = false;
        _terminalInitSent = false;
    }

    /// <summary>Persists the current terminal-pane layout into settings.</summary>
    private void CaptureTerminalSettings()
    {
        _settings.ShowTerminalPane = TerminalHost.Visibility == Visibility.Visible;
        if (TerminalRow.Height.IsAbsolute && TerminalRow.Height.Value >= 80)
        {
            _settings.TerminalPaneHeight = TerminalRow.Height.Value;
        }
    }

    private void TerminalClose_Click(object sender, RoutedEventArgs e)
    {
        SetTerminalVisible(false);
        _settings.ShowTerminalPane = false;
        SaveSettings();
    }
}
