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
    private bool _suppressShellActivate;   // pane opened to run a command in Output
    private bool _shellRequested;          // the interactive shell is actually wanted
    private Task<bool>? _terminalWebInit;
    private Task<CoreWebView2Environment>? _webView2EnvTask;

    /// <summary>
    /// Shared WebView2 environment for both the terminal and the preview. The
    /// user-data folder is pinned to a writable location under %LOCALAPPDATA%;
    /// the default (next to the executable) fails with 0x80070003 when tfx is
    /// installed somewhere read-only such as Program Files.
    /// </summary>
    private Task<CoreWebView2Environment> EnsureWebView2EnvironmentAsync()
    {
        _webView2EnvTask ??= CreateWebView2EnvironmentAsync();
        return _webView2EnvTask;

        static Task<CoreWebView2Environment> CreateWebView2EnvironmentAsync()
        {
            var userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "tfx", "WebView2");
            Directory.CreateDirectory(userData);
            return CoreWebView2Environment.CreateAsync(null, userData);
        }
    }

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
            if (!IsWebView2RuntimeAvailable())
            {
                return;
            }
            // Warm up the WebView2 + xterm.js page in the background once the UI
            // is idle. The first open is otherwise slow because creating the
            // WebView2 runtime process and loading/parsing xterm happens only on
            // demand. Pre-loading it here means opening the pane just has to spawn
            // the shell. The _terminalPaneOpen guard keeps the warm-up resize from
            // starting a shell before the user actually opens the pane.
            Dispatcher.BeginInvoke(
                () => _ = EnsureTerminalWebViewAsync(),
                DispatcherPriority.ApplicationIdle);
        }
    }

    private void TerminalPane_Click(object sender, RoutedEventArgs e) => ToggleTerminalPane();

    private void ToggleTerminalPane()
    {
        // A user-driven open/close is a normal interactive session — clear the
        // command-output-only flag so the Shell tab is focused and its shell can
        // start as usual.
        _suppressShellActivate = false;
        SetTerminalVisible(TerminalHost.Visibility != Visibility.Visible);
        SaveSettings();
    }

    private void SetTerminalVisible(bool visible)
    {
        if (visible)
        {
            // The terminal renders with xterm.js inside WebView2. If the Edge
            // WebView2 Runtime isn't installed (common on a fresh Windows 10),
            // the page can't load and the shell never appears. Detect this up
            // front and tell the user how to fix it instead of showing a blank
            // pane.
            if (!IsWebView2RuntimeAvailable())
            {
                SetStatus(Loc.T("Built-in terminal needs the Microsoft Edge WebView2 Runtime. Install it from https://go.microsoft.com/fwlink/p/?LinkId=2124703"));
                return;
            }

            _terminalPaneOpen = true;
            // A normal open (toolbar / Ctrl+J / context menu) wants the
            // interactive shell. Opening only to show a command's Output tab sets
            // _suppressShellActivate, in which case no shell is started until the
            // user actually views the Shell tab.
            if (!_suppressShellActivate)
            {
                _shellRequested = true;
            }
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
                // When the pane was opened to run a command in the Output tab,
                // don't steal focus back to the Shell tab.
                if (!_suppressShellActivate)
                {
                    PostToTerminal(new { type = "focus" });
                }
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

    /// <summary>
    /// Writes the embedded xterm.js assets (terminal.html, xterm.js, css, addons)
    /// to a writable per-user folder and returns its path. Single-file publish
    /// can't expose them as loose files next to the exe, so they ship as
    /// <c>EmbeddedResource</c> (logical name <c>TfxTerminal.*</c>) and are
    /// extracted here. Files are rewritten when missing or a different size, so a
    /// new app version refreshes them.
    /// </summary>
    private static string ExtractTerminalAssets()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "tfx", "terminal");
        Directory.CreateDirectory(dir);

        var asm = typeof(MainWindow).Assembly;
        const string prefix = "TfxTerminal.";
        foreach (var resource in asm.GetManifestResourceNames())
        {
            if (!resource.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }
            var fileName = resource[prefix.Length..];
            var target = Path.Combine(dir, fileName);

            using var stream = asm.GetManifestResourceStream(resource);
            if (stream is null)
            {
                continue;
            }
            // Skip rewrite when the on-disk file already matches the embedded size.
            if (File.Exists(target) && new FileInfo(target).Length == stream.Length)
            {
                continue;
            }
            using var file = File.Create(target);
            stream.CopyTo(file);
        }
        return dir;
    }

    /// <summary>
    /// True if the Edge WebView2 Runtime is installed. Preinstalled on Windows
    /// 11; may be absent on a clean Windows 10. Without it the terminal (and the
    /// Markdown / HTML preview) can't render.
    /// </summary>
    private static bool IsWebView2RuntimeAvailable()
    {
        try
        {
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            return !string.IsNullOrEmpty(version);
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> InitTerminalWebViewAsync()
    {
        CoreWebView2Environment env;
        try { env = await EnsureWebView2EnvironmentAsync(); }
        catch (Exception ex) { throw new InvalidOperationException($"[env] {ex.Message}", ex); }

        try { await Terminal.EnsureCoreWebView2Async(env); }
        catch (Exception ex) { throw new InvalidOperationException($"[ensure] {ex.Message}", ex); }

        var core = Terminal.CoreWebView2;

        var settings = core.Settings;
        settings.AreDevToolsEnabled = false;
        settings.AreDefaultContextMenusEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.IsZoomControlEnabled = false;

        // Serve the bundled xterm assets from a virtual host. The assets are
        // embedded in the executable (single-file friendly) and extracted to a
        // writable per-user folder on demand. Allow lets our own page read its
        // sibling files; the host name isn't real so cross-origin requests can't
        // reach it.
        var assetDir = ExtractTerminalAssets();
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
        // Allow the xterm page to read the clipboard for Ctrl+V paste. Only the
        // clipboard-read permission is granted; everything else stays default.
        core.PermissionRequested += (_, e) =>
        {
            if (e.PermissionKind == CoreWebView2PermissionKind.ClipboardRead)
            {
                e.State = CoreWebView2PermissionState.Allow;
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
                StartOrResizePty("resize");
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
                // Don't force the Shell tab when the pane was opened to show a
                // command's Output (the page applies its own pendingActivate).
                if (!_suppressShellActivate)
                {
                    PostToTerminal(new { type = "focus" });
                }
                break;
            case "shellActivated":
                // The user clicked the Shell tab. Start the shell lazily if it
                // isn't running yet (the pane may have opened only for Output) and
                // stop suppressing Shell-tab focus.
                _suppressShellActivate = false;
                _shellRequested = true;
                _terminalCols = (short)Math.Clamp(msg.cols, 1, 1000);
                _terminalRows = (short)Math.Clamp(msg.rows, 1, 1000);
                StartOrResizePty("shellActivated");
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
    private void StartOrResizePty(string reason = "?")
    {
        if (_terminalPty is not null)
        {
            _terminalPty.Resize(_terminalCols, _terminalRows);
            return;
        }

        // Only spawn a shell when one is actually wanted — the pane is open AND
        // the interactive Shell tab has been requested. Opening the pane just to
        // show a command's Output tab must not start a shell (it would be an
        // unused ConPTY session). The shell starts lazily when the user views the
        // Shell tab (see "shellActivated" from the page).
        if (!_terminalPaneOpen || !_shellRequested)
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
        // The shell is gone; the next open must explicitly request it again (so
        // reopening only for command Output doesn't resurrect a shell).
        _shellRequested = false;
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

    /// <summary>
    /// Sends an interrupt (Ctrl+C / ETX, 0x03) to the running shell. Exposed as a
    /// header button because the keyboard Ctrl+C inside the WebView2 terminal is
    /// not reliably delivered on all setups.
    /// </summary>
    private void TerminalInterrupt_Click(object sender, RoutedEventArgs e)
    {
        _terminalPty?.WriteBytes([0x03]);
        Terminal.Focus();
        PostToTerminal(new { type = "focus" });
    }

    // ─── Drag & drop: files dropped onto the terminal insert their paths ─────
    // The WebView2's own AllowExternalDrop is off (set in XAML) so the drop
    // reaches WPF here rather than the web page — the page (a browser context)
    // can't read full paths for security reasons, but the WPF FileDrop data can.

    /// <summary>
    /// Shows / hides the drop overlay Popup that sits on top of the WebView2
    /// during a drag. The Popup has its own HWND (a same-HWND WPF element can't
    /// receive a drop over the native WebView2 child window). Only shown when the
    /// pane is open and a shell is running, and sized to match the WebView2.
    /// </summary>
    private void ShowTerminalDropOverlay(bool show)
    {
        if (show && _terminalPaneOpen && _terminalPty is not null
            && Terminal.ActualWidth > 0 && Terminal.ActualHeight > 0)
        {
            TerminalDropOverlayBorder.Width = Terminal.ActualWidth;
            TerminalDropOverlayBorder.Height = Terminal.ActualHeight;
            TerminalDropOverlay.IsOpen = true;
        }
        else
        {
            TerminalDropOverlay.IsOpen = false;
        }
    }

    private void Terminal_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>
    /// Writes the dropped files' full paths to the shell as if typed at the
    /// prompt. Paths containing spaces are double-quoted (valid for both cmd and
    /// PowerShell); multiple paths are space-separated with a trailing space.
    /// </summary>
    private void Terminal_FileDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        TerminalDropOverlay.IsOpen = false;
        if (_terminalPty is null || !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
        {
            return;
        }

        var text = string.Join(" ", paths.Select(QuoteShellPath)) + " ";
        _terminalPty.Write(text);
        Terminal.Focus();
        PostToTerminal(new { type = "focus" });
    }

    /// <summary>Double-quotes a path if it contains whitespace.</summary>
    private static string QuoteShellPath(string path) =>
        path.Any(char.IsWhiteSpace) ? $"\"{path}\"" : path;

    /// <summary>
    /// Runs a user-defined command (with <c>terminal = true</c>) and streams its
    /// stdout / stderr into the terminal pane's read-only **Output** tab. Opens
    /// the pane and activates the Output tab; the command runs with redirected
    /// output (no separate console window).
    /// </summary>
    private void RunCommandInTerminal(UserCommand command, IReadOnlyList<FileItem> selection, string cwd)
    {
        if (!IsWebView2RuntimeAvailable())
        {
            SetStatus(Loc.T("Built-in terminal needs the Microsoft Edge WebView2 Runtime. Install it from https://go.microsoft.com/fwlink/p/?LinkId=2124703"));
            return;
        }

        // Opening the pane normally focuses the Shell tab (and a reopen sends a
        // "reset" that re-activates Shell). Suppress that here so the Output tab
        // we switch to below isn't immediately overridden.
        _suppressShellActivate = true;
        if (TerminalHost.Visibility != Visibility.Visible)
        {
            SetTerminalVisible(true);
            SaveSettings();
        }

        // The xterm page loads asynchronously; activate / output messages sent
        // before it signals "ready" are dropped. Wait for readiness, then switch
        // to the Output tab and stream the command's output there.
        RunWhenTerminalReady(() => RunCommandCaptured(command, selection, cwd));
    }

    private void RunCommandCaptured(UserCommand command, IReadOnlyList<FileItem> selection, string cwd)
    {
        // Keep _suppressShellActivate set while showing command output so a late
        // "ready" focus message can't pull focus back to the Shell tab. It is
        // cleared on the next user-driven open (ToggleTerminalPane) or when the
        // user clicks the Shell tab.
        PostToTerminal(new { type = "activate", target = "output" });

        // Header line so successive runs are distinguishable in the Output tab.
        WriteOutputTab($"[90m$ {command.Name}[0m");

        var ok = CommandRunner.RunCaptured(
            command, selection, cwd, ScriptsDirectory(), ResolveTerminalShell(),
            onLine: l => Dispatcher.BeginInvoke(() => WriteOutputTab(l)),
            onExit: () => Dispatcher.BeginInvoke(() => WriteOutputTab("")),
            out var error);

        if (!ok)
        {
            SetStatus(Loc.F("Command failed: {0}", error ?? command.Name));
        }
    }

    /// <summary>
    /// Runs <paramref name="action"/> once the xterm page has signalled it is
    /// ready (so posted messages aren't dropped). Ensures the WebView is starting,
    /// then polls a short-interval timer; gives up after a few seconds.
    /// </summary>
    private void RunWhenTerminalReady(Action action, int attempt = 0)
    {
        _ = EnsureTerminalWebViewAsync();
        if (_terminalWebReady)
        {
            action();
            return;
        }
        if (attempt > 100) // ~5s at 50ms.
        {
            return;
        }
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            RunWhenTerminalReady(action, attempt + 1);
        };
        timer.Start();
    }

    /// <summary>Writes one line (+ CRLF) to the read-only Output tab of the pane.</summary>
    private void WriteOutputTab(string line)
    {
        var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(line + "\r\n"));
        PostToTerminal(new { type = "output", target = "output", dataB64 = b64 });
    }
}
