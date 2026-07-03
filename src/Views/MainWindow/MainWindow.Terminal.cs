using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
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

    // "Sync file list to terminal folder" support: the button sends a built-in
    // cwd-printing command to the shell, and the output is scanned for the
    // marker line to drive the active pane.
    private enum TerminalShellKind { PowerShell, Cmd, Bash }
    // The path is bracketed by a start and end marker so it can be extracted
    // without relying on a trailing newline — terminal multiplexers (tmux/wtmux)
    // redraw via the alternate screen and cursor moves, so the marker line often
    // has no CR/LF after it. Both markers are emitted by a single print, so they
    // stay contiguous in the output regardless of redraws.
    private const string CwdMarker = "[tfx:cwd]";
    private const string CwdMarkerEnd = "[tfx:end]";
    private TerminalShellKind _terminalShellKind = TerminalShellKind.PowerShell;
    private bool _cwdSyncPending;
    private readonly List<byte> _cwdSyncBytes = new();
    private DispatcherTimer? _cwdSyncTimer;

    // Passive cwd tracking via OSC 7 / OSC 9;9 escape sequences emitted by the
    // shell (Windows Terminal integration, oh-my-posh, Starship, …). These are
    // invisible control sequences, so the cwd is captured without printing any
    // command to the prompt. Scanned on the PTY reader thread; the latest value
    // is used by the "sync to terminal folder" button when no multiplexer query
    // applies. State below is touched only on the single reader thread.
    private volatile string? _terminalTrackedCwd;
    private bool _oscInEscape;       // saw ESC, awaiting ]
    private bool _oscCollecting;     // inside an OSC body
    private bool _oscSawEscInBody;   // saw ESC inside body, awaiting \ (ST)
    private readonly List<byte> _oscBytes = new();  // OSC body (no introducer / ST)
    private readonly List<byte> _oscRaw = new();    // full raw OSC, re-emitted if not a cwd one

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
                // Re-opening: when the session is still alive, just re-fit to the
                // restored pane size — this keeps the scrollback and resumes the
                // same shell. Only when there is no session (first open, or after
                // the shell exited) do we "reset" so the page's next resize spawns
                // a fresh shell via StartOrResizePty.
                if (_terminalWebReady)
                {
                    PostToTerminal(_terminalPty is null
                        ? new { type = "reset" }
                        : new { type = "fit" });
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
            // Remember the current (possibly user-dragged) height before collapsing
            // so reopening restores that size instead of the default.
            if (TerminalRow.Height.IsAbsolute && TerminalRow.Height.Value >= 80)
            {
                _settings.TerminalPaneHeight = TerminalRow.Height.Value;
            }
            TerminalSplitter.Visibility = Visibility.Collapsed;
            TerminalHost.Visibility = Visibility.Collapsed;
            TerminalRow.Height = new GridLength(0);
            // Closing only HIDES the pane — the shell session keeps running so the
            // next open resumes it (the WebView2 page and xterm buffer stay alive).
            // The session is torn down only when the shell itself exits (the user
            // types `exit`), handled in OnTerminalExited.
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

        // Cache-bust by the page's byte length so an updated terminal.html is
        // always re-fetched (WebView2 otherwise serves the previously cached page
        // from the virtual host, masking changes across versions).
        var token = GetTerminalAssetToken(assetDir);
        core.Navigate($"https://tfx.terminal/terminal.html?v={token}");
        return true;
    }

    private static string GetTerminalAssetToken(string assetDir)
    {
        try
        {
            return new FileInfo(Path.Combine(assetDir, "terminal.html")).Length.ToString();
        }
        catch
        {
            return "0";
        }
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
            case "focusFiles":
                // The user pressed the focusFilePane shortcut inside the terminal.
                // Move focus out of the WebView and back to the active file list.
                Dispatcher.BeginInvoke(FocusActiveFilePane, DispatcherPriority.Input);
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
        // Terminal font: dedicated [terminal] shell font → [font] terminal → global
        // mono → built-in default. Size: [terminal] fontSize → [font] terminalSize
        // → the saved pane size.
        var terminalFamily = _config.Terminal.Font ?? _config.FontTerminal ?? _config.FontMono;
        var options = new
        {
            fontFamily = string.IsNullOrWhiteSpace(terminalFamily)
                ? "Cascadia Mono, Consolas, monospace"
                : terminalFamily,
            fontSize = _config.Terminal.FontSize ?? _config.FontTerminalSize ?? _settings.TerminalPaneFontSize,
            scrollback = 5000,
            // Transparent unless the user set an explicit background color.
            allowTransparency = !_config.Terminal.Colors.ContainsKey("background"),
            // The shell intercepts this combo and asks the host to focus the
            // file list (config.toml [shortcuts] focusFilePane).
            focusFilesKey = BuildFocusFilesKeySpec(),
            theme
        };
        PostToTerminal(new { type = "init", options });
    }

    /// <summary>
    /// Translates the <c>focusFilePane</c> shortcut into a small {ctrl,shift,alt,key}
    /// object the xterm page can match against a browser KeyboardEvent. Returns null
    /// if the shortcut is unset, so the page leaves the key alone.
    /// </summary>
    private object? BuildFocusFilesKeySpec()
    {
        if (!_shortcuts.TryGetValue("focusFilePane", out var sc))
        {
            return null;
        }
        return new
        {
            ctrl = sc.Modifiers.HasFlag(ModifierKeys.Control),
            shift = sc.Modifiers.HasFlag(ModifierKeys.Shift),
            alt = sc.Modifiers.HasFlag(ModifierKeys.Alt),
            key = JsKeyName(sc.Key)
        };
    }

    /// <summary>Maps a WPF <see cref="Key"/> to the browser KeyboardEvent.key value.</summary>
    private static string JsKeyName(Key key)
    {
        if (key >= Key.D0 && key <= Key.D9)
        {
            return ((char)('0' + (key - Key.D0))).ToString();
        }
        if (key >= Key.NumPad0 && key <= Key.NumPad9)
        {
            return ((char)('0' + (key - Key.NumPad0))).ToString();
        }
        if (key >= Key.A && key <= Key.Z)
        {
            return key.ToString().ToLowerInvariant();
        }
        return key switch
        {
            Key.OemPeriod => ".",
            Key.OemComma => ",",
            Key.OemQuestion => "/",
            Key.OemMinus => "-",
            Key.Escape => "escape",
            _ => key.ToString().ToLowerInvariant()
        };
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
            // While the pane is hidden the WebView collapses to ~0 height and the
            // page can report a 1-row fit. Don't shrink the live session to that;
            // keep its size until the pane is shown again (reopen re-fits it).
            if (_terminalPaneOpen)
            {
                _terminalPty.Resize(_terminalCols, _terminalRows);
            }
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
        _terminalShellKind = DetectShellKind(commandLine);
        commandLine = InjectCwdReporting(commandLine, _terminalShellKind);
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
        // Capture the cwd from OSC 7 / OSC 9;9 and strip those sequences so the
        // terminal never renders them — otherwise shells that emit them (prompt
        // hooks, oh-my-posh, …) leave stray characters in the line during input.
        // Runs on this single reader thread.
        var visible = FilterAndTrackOsc(data);
        if (visible.Length == 0)
        {
            return;
        }

        var b64 = Convert.ToBase64String(visible);
        Dispatcher.BeginInvoke(() =>
        {
            PostToTerminal(new { type = "output", dataB64 = b64 });
            if (_cwdSyncPending)
            {
                TryParseCwdSync(visible);
            }
        });
    }

    /// <summary>
    /// Navigates the active file pane to the terminal's current directory. When a
    /// multiplexer (wtmux/tmux) is running in this pane, its current path is read
    /// directly via <c>wtmux display-message</c> run as a separate process — no
    /// command is typed into the shell. Otherwise a built-in "print current
    /// directory" command is sent to the shell and its marked output is read in
    /// <see cref="OnTerminalPtyOutput"/>.
    /// </summary>
    private void TerminalSyncCwd_Click(object sender, RoutedEventArgs e)
    {
        var pty = _terminalPty;
        if (pty is not { IsRunning: true })
        {
            return;
        }

        // Multiplexer present → query it out-of-band (nothing typed into the shell).
        if (TryGetMultiplexerCwd(out var muxPath))
        {
            NavigateSyncedCwd(muxPath);
            SetStatus(Loc.F("Synced to {0}", muxPath));
            Terminal.Focus();
            return;
        }

        // The active shell may be a sub-shell launched inside the one tfx started
        // (e.g. `cmd` typed at a PowerShell prompt). Re-detect it from the process
        // tree so the right method is used and a stale tracked value isn't trusted.
        var activeKind = DetectActiveShellKind(pty.ProcessId);

        // OSC-tracked cwd is reliable only for a PowerShell foreground (tfx injects
        // the reporter there). A cmd / bash sub-shell doesn't update it, so its
        // value would be stale — fall through to that shell's own query instead.
        var tracked = _terminalTrackedCwd;
        if (activeKind == TerminalShellKind.PowerShell &&
            !string.IsNullOrEmpty(tracked) && Directory.Exists(tracked))
        {
            NavigateSyncedCwd(tracked);
            Terminal.Focus();
            return;
        }

        // Ask the active shell to print its working directory, bracketed by the
        // start/end markers so the output is parsed regardless of trailing newline.
        var command = activeKind switch
        {
            TerminalShellKind.Cmd => $"echo {CwdMarker}%CD%{CwdMarkerEnd}",
            TerminalShellKind.Bash => $"printf '{CwdMarker}%s{CwdMarkerEnd}\\n' \"$PWD\"",
            _ => $"Write-Output (\"{CwdMarker}\" + $PWD.ProviderPath + \"{CwdMarkerEnd}\")",
        };

        BeginCwdSync();
        try
        {
            pty.WriteBytes(Encoding.UTF8.GetBytes(command + "\r"));
        }
        catch
        {
            EndCwdSync();
        }
        Terminal.Focus();
    }

    /// <summary>
    /// When a wtmux/tmux process is running inside this pane's shell, reads its
    /// current pane path via <c>wtmux display-message -p '#{pane_current_path}'</c>
    /// (run as a child process, not typed into the terminal). Returns false when no
    /// multiplexer is detected in this pane or the query yields no usable path.
    /// </summary>
    private bool TryGetMultiplexerCwd(out string path)
    {
        path = "";
        var shellPid = _terminalPty?.ProcessId ?? 0;
        if (shellPid == 0)
        {
            return false;
        }

        // Detect whether wtmux is actually running IN THIS PANE: a wtmux process
        // must be a descendant of this pane's shell. This prevents an unrelated
        // wtmux server running elsewhere from hijacking a plain-shell pane.
        GetShellProcessTree(shellPid, out var descendants, out var hasWtmux);
        if (!hasWtmux)
        {
            return false;
        }

        // Enumerate clients. A bare `display-message` has no "current client" when
        // run from outside the session, so the session is resolved explicitly:
        // match the client PID to this pane's shell subtree, falling back to the
        // sole session (safe now that wtmux is confirmed present in this pane).
        var clients = RunWtmux("list-clients", "-F", "#{client_pid}\t#{session_id}");
        if (string.IsNullOrWhiteSpace(clients))
        {
            return false;
        }

        var sessions = new HashSet<string>();
        string? session = null;
        foreach (var line in clients.Split('\n'))
        {
            var parts = line.Trim().Split('\t');
            if (parts.Length < 2)
            {
                continue;
            }
            var sess = parts[1].Trim();
            if (sess.Length > 0)
            {
                sessions.Add(sess);
            }
            if (int.TryParse(parts[0].Trim(), out var clientPid) && descendants.Contains(clientPid))
            {
                session = sess;
                break;
            }
        }
        session ??= sessions.Count == 1 ? sessions.First() : null;
        if (session is null)
        {
            return false;
        }

        var result = RunWtmux("display-message", "-p", "-t", session, "#{pane_current_path}");
        if (string.IsNullOrWhiteSpace(result))
        {
            SetStatus(Loc.F("wtmux query failed (session {0})", session));
            return false;
        }
        path = result.Trim();
        return path.Length > 0;
    }

    private void NavigateSyncedCwd(string candidate)
    {
        candidate = candidate.Trim().Trim('"');
        if (candidate.Length == 0 || ArchivePath.Contains(candidate))
        {
            return;
        }
        try
        {
            if (Directory.Exists(candidate))
            {
                var full = Path.GetFullPath(candidate);
                if (!string.Equals(GetCurrentPath(_activeGrid), full, StringComparison.OrdinalIgnoreCase))
                {
                    Navigate(_activeGrid, full, true);
                }
            }
        }
        catch
        {
            // ignore an unreadable path
        }
    }

    /// <summary>Runs the wtmux CLI with the given args; returns stdout, or null on any failure.</summary>
    private static string? RunWtmux(params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "wtmux",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in args)
            {
                psi.ArgumentList.Add(a);
            }

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                return null;
            }
            // Drain both pipes asynchronously BEFORE waiting: a synchronous
            // ReadToEnd() first meant the 1500ms timeout never ran (the read
            // blocks forever if wtmux hangs without closing stdout), and an
            // unread stderr can fill its pipe and deadlock the child.
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            _ = proc.StandardError.ReadToEndAsync();
            if (!proc.WaitForExit(1500))
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                return null;
            }
            if (proc.ExitCode != 0)
            {
                return null;
            }
            // Normally the pipe closes with the process; the extra timeout
            // covers a grandchild inheriting (and holding open) the handle.
            return stdoutTask.Wait(500) ? stdoutTask.Result : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Walks the process table once and returns the PID subtree rooted at
    /// <paramref name="rootPid"/> (including itself), plus whether any process in
    /// that subtree is a wtmux executable (i.e. wtmux is running in this pane).
    /// </summary>
    private static void GetShellProcessTree(int rootPid, out HashSet<int> descendants, out bool hasWtmux)
    {
        descendants = new HashSet<int> { rootPid };
        hasWtmux = false;

        var children = new Dictionary<int, List<int>>();
        var names = new Dictionary<int, string>();

        var snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1))
        {
            return;
        }
        try
        {
            var entry = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
            if (!Process32First(snapshot, ref entry))
            {
                return;
            }
            do
            {
                var pid = (int)entry.th32ProcessID;
                var ppid = (int)entry.th32ParentProcessID;
                if (!children.TryGetValue(ppid, out var list))
                {
                    list = [];
                    children[ppid] = list;
                }
                list.Add(pid);
                names[pid] = entry.szExeFile ?? "";
            }
            while (Process32Next(snapshot, ref entry));
        }
        finally
        {
            CloseHandle(snapshot);
        }

        var queue = new Queue<int>();
        queue.Enqueue(rootPid);
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            if (children.TryGetValue(p, out var kids))
            {
                foreach (var k in kids)
                {
                    if (descendants.Add(k))
                    {
                        queue.Enqueue(k);
                    }
                }
            }
        }

        foreach (var pid in descendants)
        {
            if (names.TryGetValue(pid, out var name) &&
                name.StartsWith("wtmux", StringComparison.OrdinalIgnoreCase))
            {
                hasWtmux = true;
                break;
            }
        }
    }

    private const uint TH32CS_SNAPPROCESS = 0x00000002;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "Process32FirstW")]
    private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "Process32NextW")]
    private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    /// <summary>
    /// Determines the shell currently in the foreground of the pane by walking the
    /// process tree from the shell tfx started and taking the deepest descendant
    /// that is itself a shell (so `cmd` typed inside PowerShell is detected as cmd).
    /// Falls back to the launch-time shell kind when nothing deeper is found.
    /// </summary>
    private TerminalShellKind DetectActiveShellKind(int shellPid)
    {
        if (shellPid == 0)
        {
            return _terminalShellKind;
        }

        var children = new Dictionary<int, List<int>>();
        var names = new Dictionary<int, string>();

        var snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1))
        {
            return _terminalShellKind;
        }
        try
        {
            var entry = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
            if (!Process32First(snapshot, ref entry))
            {
                return _terminalShellKind;
            }
            do
            {
                var pid = (int)entry.th32ProcessID;
                var ppid = (int)entry.th32ParentProcessID;
                if (!children.TryGetValue(ppid, out var list))
                {
                    list = [];
                    children[ppid] = list;
                }
                list.Add(pid);
                names[pid] = entry.szExeFile ?? "";
            }
            while (Process32Next(snapshot, ref entry));
        }
        finally
        {
            CloseHandle(snapshot);
        }

        var best = _terminalShellKind;
        var bestDepth = -1;
        var queue = new Queue<(int Pid, int Depth)>();
        var seen = new HashSet<int> { shellPid };
        queue.Enqueue((shellPid, 0));
        while (queue.Count > 0)
        {
            var (pid, depth) = queue.Dequeue();
            if (names.TryGetValue(pid, out var name) && ShellKindFromExe(name) is { } kind && depth >= bestDepth)
            {
                best = kind;
                bestDepth = depth;
            }
            if (children.TryGetValue(pid, out var kids))
            {
                foreach (var k in kids)
                {
                    if (seen.Add(k))
                    {
                        queue.Enqueue((k, depth + 1));
                    }
                }
            }
        }
        return best;
    }

    private static TerminalShellKind? ShellKindFromExe(string exe)
    {
        var s = exe.ToLowerInvariant();
        if (s.StartsWith("cmd", StringComparison.Ordinal))
        {
            return TerminalShellKind.Cmd;
        }
        if (s.StartsWith("powershell", StringComparison.Ordinal) || s.StartsWith("pwsh", StringComparison.Ordinal))
        {
            return TerminalShellKind.PowerShell;
        }
        if (s.StartsWith("bash", StringComparison.Ordinal) || s.StartsWith("wsl", StringComparison.Ordinal) ||
            s is "sh.exe" or "sh")
        {
            return TerminalShellKind.Bash;
        }
        return null;
    }

    private static TerminalShellKind DetectShellKind(string commandLine)
    {
        var s = commandLine.ToLowerInvariant();
        if (s.Contains("pwsh") || s.Contains("powershell"))
        {
            return TerminalShellKind.PowerShell;
        }
        if (s.Contains("bash") || s.Contains("wsl") || s.Contains("git\\bin\\sh") || s.Contains("/sh"))
        {
            return TerminalShellKind.Bash;
        }
        if (s.Contains("cmd"))
        {
            return TerminalShellKind.Cmd;
        }
        return TerminalShellKind.PowerShell; // default shell is PowerShell
    }

    private void BeginCwdSync()
    {
        _cwdSyncPending = true;
        _cwdSyncBytes.Clear();
        if (_cwdSyncTimer is null)
        {
            _cwdSyncTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _cwdSyncTimer.Tick += (_, _) => EndCwdSync();
        }
        _cwdSyncTimer.Stop();
        _cwdSyncTimer.Start();
    }

    private void EndCwdSync()
    {
        _cwdSyncPending = false;
        _cwdSyncBytes.Clear();
        _cwdSyncTimer?.Stop();
    }

    private void TryParseCwdSync(byte[] data)
    {
        _cwdSyncBytes.AddRange(data);
        if (_cwdSyncBytes.Count > 262144)
        {
            EndCwdSync();
            return;
        }

        var text = Encoding.UTF8.GetString(_cwdSyncBytes.ToArray());
        var idx = 0;
        while ((idx = text.IndexOf(CwdMarker, idx, StringComparison.Ordinal)) >= 0)
        {
            var start = idx + CwdMarker.Length;
            var end = text.IndexOf(CwdMarkerEnd, start, StringComparison.Ordinal);
            if (end < 0)
            {
                break; // the closing marker hasn't arrived yet
            }

            // Strip ANSI/cursor sequences and any CR/LF a line-wrap may have
            // injected between the markers; a path contains neither.
            var candidate = StripAnsi(text[start..end])
                .Replace("\r", "").Replace("\n", "").Trim().Trim('"');
            if (candidate.Length > 0 && !ArchivePath.Contains(candidate))
            {
                try
                {
                    if (Directory.Exists(candidate))
                    {
                        var full = Path.GetFullPath(candidate);
                        EndCwdSync();
                        if (!string.Equals(GetCurrentPath(_activeGrid), full, StringComparison.OrdinalIgnoreCase))
                        {
                            Navigate(_activeGrid, full, true);
                        }
                        return;
                    }
                }
                catch
                {
                    // ignore and keep scanning
                }
            }
            idx = end + CwdMarkerEnd.Length;
        }
    }

    private static string StripAnsi(string s) =>
        s.IndexOf('\x1b') < 0
            ? s
            : System.Text.RegularExpressions.Regex.Replace(s, "\x1b\\[[0-9;?]*[ -/]*[@-~]", "");

    // ─── Passive cwd tracking via OSC 7 / OSC 9;9 ───────────────────────────
    // Scans the raw PTY byte stream (across reads) for the shell's invisible
    // working-directory notifications and stores the latest path in
    // _terminalTrackedCwd. No command is sent and nothing is shown in the pane.

    private byte[] FilterAndTrackOsc(byte[] data)
    {
        var output = new List<byte>(data.Length);
        foreach (var b in data)
        {
            if (_oscCollecting)
            {
                _oscRaw.Add(b);
                if (_oscSawEscInBody)
                {
                    _oscSawEscInBody = false;
                    // ESC \ terminates the OSC; anything else means it was malformed.
                    EndOscCollect(output, terminated: b == 0x5c);
                    continue;
                }
                if (b is 0x07 or 0x9c) // BEL or C1 ST
                {
                    EndOscCollect(output, terminated: true);
                    continue;
                }
                if (b == 0x1b) // possible start of ESC \
                {
                    _oscSawEscInBody = true;
                    continue;
                }
                _oscBytes.Add(b);
                if (_oscBytes.Count > 4096) // runaway → give up and pass it through
                {
                    EndOscCollect(output, terminated: false);
                }
                continue;
            }

            if (_oscInEscape)
            {
                _oscInEscape = false;
                if (b == 0x5d) // ESC ]  = OSC introducer → start buffering (held back)
                {
                    _oscCollecting = true;
                    _oscBytes.Clear();
                    _oscRaw.Clear();
                    _oscRaw.Add(0x1b);
                    _oscRaw.Add(0x5d);
                }
                else
                {
                    output.Add(0x1b); // a non-OSC escape (CSI, …) → emit the held ESC …
                    if (b == 0x1b)
                    {
                        _oscInEscape = true; // … another ESC: keep holding
                    }
                    else
                    {
                        output.Add(b); // … and this byte
                    }
                }
                continue;
            }

            if (b == 0x1b) // hold the ESC until we know whether it is ESC ]
            {
                _oscInEscape = true;
                continue;
            }
            output.Add(b);
        }
        return output.ToArray();
    }

    /// <summary>
    /// Ends the current OSC: if it is a cwd notification (OSC 7 / OSC 9;9) it is
    /// consumed (path captured, not forwarded); otherwise the raw sequence is
    /// passed through to the terminal unchanged.
    /// </summary>
    private void EndOscCollect(List<byte> output, bool terminated)
    {
        _oscCollecting = false;
        _oscSawEscInBody = false;

        var body = Encoding.UTF8.GetString(_oscBytes.ToArray());
        var isCwd = body.StartsWith("7;", StringComparison.Ordinal)
                 || body.StartsWith("9;9;", StringComparison.Ordinal);
        if (terminated && isCwd)
        {
            var path = ParseOscCwd(body);
            if (!string.IsNullOrEmpty(path))
            {
                _terminalTrackedCwd = path;
            }
            // consumed: not forwarded to the terminal
        }
        else
        {
            output.AddRange(_oscRaw); // not a cwd OSC (or malformed) → forward as-is
        }
        _oscBytes.Clear();
        _oscRaw.Clear();
    }

    /// <summary>Extracts a filesystem path from an OSC 7 or OSC 9;9 body, or null.</summary>
    private static string? ParseOscCwd(string body)
    {
        if (body.StartsWith("7;", StringComparison.Ordinal))
        {
            return PathFromFileUri(body[2..]);
        }
        if (body.StartsWith("9;9;", StringComparison.Ordinal))
        {
            var p = body[4..].Trim().Trim('"');
            if (p.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                return PathFromFileUri(p);
            }
            return p.Length > 0 ? p : null;
        }
        return null;
    }

    private static string? PathFromFileUri(string uri)
    {
        uri = uri.Trim();
        if (uri.Length == 0)
        {
            return null;
        }
        try
        {
            if (uri.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                var local = new Uri(uri).LocalPath;
                return string.IsNullOrEmpty(local) ? null : local;
            }
            return uri; // some shells emit a bare path after OSC 7
        }
        catch
        {
            return null;
        }
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
    /// Augments the shell command line so the shell reports its working directory
    /// via an invisible <c>OSC 9;9</c> on each prompt — tfx strips and tracks it
    /// (see <see cref="FilterAndTrackOsc"/>), giving the "sync to terminal folder"
    /// button an accurate cwd without printing anything to the prompt. Currently
    /// done for PowerShell (the default shell); other shells fall back to OSC from
    /// their own shell integration, or to the visible marker command.
    /// </summary>
    private static string InjectCwdReporting(string commandLine, TerminalShellKind kind)
    {
        if (kind != TerminalShellKind.PowerShell)
        {
            return commandLine;
        }

        // Don't clobber an explicit command/script the user configured.
        var lower = commandLine.ToLowerInvariant();
        if (lower.Contains("-command") || lower.Contains("-encodedcommand") ||
            lower.Contains("-file") || lower.Contains(" -c ") || lower.EndsWith(" -c") ||
            lower.Contains(" -e ") || lower.EndsWith(" -e"))
        {
            return commandLine;
        }

        // Runs after the profile (so it wraps oh-my-posh / Starship / a custom
        // prompt) and emits OSC 9;9 only for real filesystem locations. Passed as
        // -EncodedCommand (base64 UTF-16LE) to avoid command-line quoting issues;
        // -NoExit keeps the interactive session after the setup runs.
        const string setup =
            "$global:__tfxOrigPrompt = $function:prompt; " +
            "function global:prompt { " +
            "$__l = $ExecutionContext.SessionState.Path.CurrentLocation; " +
            "if ($__l -and $__l.Provider.Name -eq 'FileSystem') { " +
            "[Console]::Out.Write([char]27 + ']9;9;' + $__l.ProviderPath + [char]7) } " +
            "if ($global:__tfxOrigPrompt) { & $global:__tfxOrigPrompt } " +
            "else { 'PS ' + $__l.Path + '> ' } }";

        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(setup));
        return $"{commandLine} -NoExit -EncodedCommand {encoded}";
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
        // Drop the tracked cwd so a fresh shell starts without a stale value.
        _terminalTrackedCwd = null;
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

    // Header buttons that send a control byte to the running shell. The keyboard
    // equivalents inside the WebView2 terminal aren't reliably delivered on all
    // setups, so these provide a dependable way to send the common signals.
    //   Ctrl+C = 0x03 (ETX / interrupt), Ctrl+\ = 0x1C (FS / quit),
    //   Ctrl+Z = 0x1A (SUB / EOF on Windows shells).
    private void TerminalInterrupt_Click(object sender, RoutedEventArgs e) => SendTerminalControl(0x03);
    private void TerminalQuit_Click(object sender, RoutedEventArgs e) => SendTerminalControl(0x1C);
    private void TerminalEof_Click(object sender, RoutedEventArgs e) => SendTerminalControl(0x1A);

    private void SendTerminalControl(byte controlByte)
    {
        _terminalPty?.WriteBytes([controlByte]);
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
