using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Input;
using System.Windows.Media;

namespace Tfx;

public sealed class AppConfig
{
    public const int SupportedVersion = 1;

    public string? FontUi { get; private set; }
    public string? FontMono { get; private set; }
    public double? FontSize { get; private set; }
    public Dictionary<string, Color> Colors { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, double> Opacity { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, AppShortcut> Shortcuts { get; } = new(StringComparer.OrdinalIgnoreCase);
    public TerminalConfig Terminal { get; } = new();
    public Dictionary<string, string> OpenWith { get; } = new(StringComparer.OrdinalIgnoreCase);
    public StartupConfig Startup { get; } = new();
    public List<UserCommand> Commands { get; } = [];
    public List<string> Errors { get; } = [];

    public static AppConfig LoadOrCreate(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        if (!File.Exists(path))
        {
            File.WriteAllText(path, DefaultToml, Encoding.UTF8);
        }

        try
        {
            return Parse(File.ReadAllText(path, Encoding.UTF8));
        }
        catch (Exception ex)
        {
            var config = new AppConfig();
            config.Errors.Add($"config.toml: {ex.Message}");
            return config;
        }
    }

    public static AppConfig Parse(string toml)
    {
        var config = new AppConfig();
        var section = "";
        var versionSeen = false;

        // The current [[commands]] array-table entry being filled, if any.
        UserCommand? command = null;

        foreach (var rawLine in toml.Replace("\r\n", "\n").Split('\n'))
        {
            var line = StripComment(rawLine).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            // Array-of-tables header [[commands]] starts a new command entry.
            if (line.StartsWith("[[", StringComparison.Ordinal) && line.EndsWith("]]", StringComparison.Ordinal))
            {
                section = line[2..^2].Trim();
                if (section.Equals("commands", StringComparison.OrdinalIgnoreCase))
                {
                    command = new UserCommand();
                    config.Commands.Add(command);
                }
                else
                {
                    command = null;
                    config.Errors.Add($"Unknown array section: {line}");
                }
                continue;
            }

            if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
            {
                section = line[1..^1].Trim();
                command = null;
                continue;
            }

            var equals = line.IndexOf('=');
            if (equals <= 0)
            {
                config.Errors.Add($"Invalid TOML assignment: {rawLine.Trim()}");
                continue;
            }

            var key = UnquoteKey(line[..equals].Trim());
            var value = line[(equals + 1)..].Trim();

            if (section.Length == 0 && key.Equals("version", StringComparison.OrdinalIgnoreCase))
            {
                versionSeen = true;
                if (!TryParseInt(value, out var version) || version != SupportedVersion)
                {
                    config.Errors.Add($"Unsupported config.toml version: {value}");
                }
                continue;
            }

            switch (section.ToLowerInvariant())
            {
                case "font":
                    ParseFont(config, key, value);
                    break;
                case "colors":
                    if (TryParseColor(value, out var color))
                    {
                        config.Colors[key] = color;
                    }
                    else
                    {
                        config.Errors.Add($"Invalid color for {key}: {value}");
                    }
                    break;
                case "opacity":
                    if (TryParseDouble(value, out var opacity) && opacity is >= 0 and <= 1)
                    {
                        config.Opacity[key] = opacity;
                    }
                    else
                    {
                        config.Errors.Add($"Invalid opacity for {key}: {value}");
                    }
                    break;
                case "shortcuts":
                    string? shortcutError = null;
                    if (TryParseString(value, out var shortcutText) &&
                        AppShortcut.TryParse(shortcutText, out var shortcut, out shortcutError))
                    {
                        config.Shortcuts[key] = shortcut;
                    }
                    else
                    {
                        config.Errors.Add($"Invalid shortcut for {key}: {shortcutError ?? value}");
                    }
                    break;
                case "terminal":
                    ParseTerminal(config, key, value);
                    break;
                case "openwith":
                    if (TryParseString(value, out var app))
                    {
                        config.OpenWith[NormalizeExtension(key)] = app;
                    }
                    else
                    {
                        config.Errors.Add($"Invalid openWith value for {key}: {value}");
                    }
                    break;
                case "startup":
                    ParseStartup(config, key, value);
                    break;
                case "commands":
                    if (command is not null)
                    {
                        ParseCommand(config, command, key, value);
                    }
                    break;
            }
        }

        // Drop commands that never got a usable name + run pair.
        for (var i = config.Commands.Count - 1; i >= 0; i--)
        {
            var c = config.Commands[i];
            if (string.IsNullOrWhiteSpace(c.Name) || string.IsNullOrWhiteSpace(c.Run))
            {
                config.Errors.Add("Ignored a [[commands]] entry missing name or run.");
                config.Commands.RemoveAt(i);
            }
        }

        if (!versionSeen)
        {
            config.Errors.Add("Missing top-level version = 1 in config.toml");
        }

        return config;
    }

    private static void ParseFont(AppConfig config, string key, string value)
    {
        if (key.Equals("size", StringComparison.OrdinalIgnoreCase))
        {
            if (TryParseDouble(value, out var size) && size is >= 8 and <= 40)
            {
                config.FontSize = size;
            }
            else
            {
                config.Errors.Add($"Invalid font size: {value}");
            }
            return;
        }

        if (!TryParseString(value, out var family))
        {
            config.Errors.Add($"Invalid font value for {key}: {value}");
            return;
        }

        if (key.Equals("ui", StringComparison.OrdinalIgnoreCase))
        {
            config.FontUi = ResolveFontAlias(family, ui: true);
        }
        else if (key.Equals("mono", StringComparison.OrdinalIgnoreCase))
        {
            config.FontMono = ResolveFontAlias(family, ui: false);
        }
    }

    // Recognized [terminal] color keys (case-insensitive). Names match the
    // common ANSI slot naming so users coming from Windows Terminal / iTerm
    // schemes feel at home.
    private static readonly string[] TerminalColorKeys =
    [
        "background", "foreground", "cursor",
        "black", "red", "green", "yellow", "blue", "magenta", "cyan", "white",
        "brightBlack", "brightRed", "brightGreen", "brightYellow",
        "brightBlue", "brightMagenta", "brightCyan", "brightWhite",
    ];

    private static void ParseTerminal(AppConfig config, string key, string value)
    {
        // fontSize is numeric; everything else is a quoted string.
        if (key.Equals("fontSize", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("size", StringComparison.OrdinalIgnoreCase))
        {
            if (TryParseDouble(value, out var size) && size is >= 8 and <= 40)
            {
                config.Terminal.FontSize = size;
            }
            else
            {
                config.Errors.Add($"Invalid terminal fontSize: {value}");
            }
            return;
        }

        // Color keys.
        var colorKey = Array.Find(TerminalColorKeys,
            k => k.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (colorKey is not null)
        {
            if (TryParseColor(value, out var color))
            {
                config.Terminal.Colors[colorKey] = color;
            }
            else
            {
                config.Errors.Add($"Invalid terminal color for {key}: {value}");
            }
            return;
        }

        if (!TryParseString(value, out var parsed))
        {
            config.Errors.Add($"Invalid terminal value for {key}: {value}");
            return;
        }

        if (key.Equals("app", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("command", StringComparison.OrdinalIgnoreCase))
        {
            config.Terminal.Command = parsed;
        }
        else if (key.Equals("arguments", StringComparison.OrdinalIgnoreCase) ||
                 key.Equals("args", StringComparison.OrdinalIgnoreCase))
        {
            config.Terminal.Arguments = parsed;
        }
        else if (key.Equals("font", StringComparison.OrdinalIgnoreCase))
        {
            config.Terminal.Font = ResolveFontAlias(parsed, ui: false);
        }
        else if (key.Equals("shell", StringComparison.OrdinalIgnoreCase))
        {
            config.Terminal.Shell = Environment.ExpandEnvironmentVariables(parsed);
        }
    }

    /// <summary>
    /// Parses one <c>key = value</c> line inside a <c>[[commands]]</c> entry.
    /// Recognized keys: name, run, extensions (string array), target
    /// (file/folder/any), selection (single/multiple/any).
    /// </summary>
    private static void ParseCommand(AppConfig config, UserCommand command, string key, string value)
    {
        if (key.Equals("name", StringComparison.OrdinalIgnoreCase))
        {
            if (TryParseString(value, out var name)) command.Name = name;
            else config.Errors.Add($"Invalid command name: {value}");
        }
        else if (key.Equals("run", StringComparison.OrdinalIgnoreCase))
        {
            if (TryParseString(value, out var run)) command.Run = run;
            else config.Errors.Add($"Invalid command run: {value}");
        }
        else if (key.Equals("extensions", StringComparison.OrdinalIgnoreCase))
        {
            if (TryParseStringArray(value, out var exts))
            {
                command.Extensions = exts.Select(NormalizeExtension).ToList();
            }
            else
            {
                config.Errors.Add($"Invalid command extensions: {value}");
            }
        }
        else if (key.Equals("target", StringComparison.OrdinalIgnoreCase))
        {
            if (TryParseString(value, out var t) &&
                (t.Equals("file", StringComparison.OrdinalIgnoreCase) ||
                 t.Equals("folder", StringComparison.OrdinalIgnoreCase) ||
                 t.Equals("current", StringComparison.OrdinalIgnoreCase) ||
                 t.Equals("any", StringComparison.OrdinalIgnoreCase)))
            {
                command.Target = t.ToLowerInvariant();
            }
            else
            {
                config.Errors.Add($"Invalid command target: {value}");
            }
        }
        else if (key.Equals("requireGit", StringComparison.OrdinalIgnoreCase))
        {
            if (TryParseBool(value, out var b)) command.RequireGit = b;
            else config.Errors.Add($"Invalid command requireGit: {value}");
        }
        else if (key.Equals("selection", StringComparison.OrdinalIgnoreCase))
        {
            if (TryParseString(value, out var s) &&
                (s.Equals("single", StringComparison.OrdinalIgnoreCase) ||
                 s.Equals("multiple", StringComparison.OrdinalIgnoreCase) ||
                 s.Equals("any", StringComparison.OrdinalIgnoreCase)))
            {
                command.Selection = s.ToLowerInvariant();
            }
            else
            {
                config.Errors.Add($"Invalid command selection: {value}");
            }
        }
        else if (key.Equals("terminal", StringComparison.OrdinalIgnoreCase))
        {
            if (TryParseBool(value, out var b)) command.Terminal = b;
            else config.Errors.Add($"Invalid command terminal: {value}");
        }
    }

    private static void ParseStartup(AppConfig config, string key, string value)
    {
        if (key.Equals("layout", StringComparison.OrdinalIgnoreCase))
        {
            if (TryParseString(value, out var layout) &&
                (layout.Equals("single", StringComparison.OrdinalIgnoreCase) ||
                 layout.Equals("split", StringComparison.OrdinalIgnoreCase) ||
                 layout.Equals("restore", StringComparison.OrdinalIgnoreCase)))
            {
                config.Startup.Layout = layout.ToLowerInvariant();
            }
            else
            {
                config.Errors.Add($"Invalid startup layout: {value}");
            }
        }
        else if (key.Equals("preview", StringComparison.OrdinalIgnoreCase))
        {
            if (TryParseString(value, out var preview) &&
                (preview.Equals("show", StringComparison.OrdinalIgnoreCase) ||
                 preview.Equals("hide", StringComparison.OrdinalIgnoreCase) ||
                 preview.Equals("restore", StringComparison.OrdinalIgnoreCase)))
            {
                config.Startup.Preview = preview.ToLowerInvariant();
            }
            else
            {
                config.Errors.Add($"Invalid startup preview: {value}");
            }
        }
        else if (key.Equals("rightFolder", StringComparison.OrdinalIgnoreCase))
        {
            if (TryParseString(value, out var folder))
            {
                config.Startup.RightFolders = [ExpandUserPath(folder)];
            }
        }
        else if (key.Equals("rightFolders", StringComparison.OrdinalIgnoreCase))
        {
            if (TryParseStringArray(value, out var folders))
            {
                config.Startup.RightFolders = folders.Select(ExpandUserPath).ToList();
            }
            else
            {
                config.Errors.Add($"Invalid startup rightFolders: {value}");
            }
        }
    }

    private static string StripComment(string line)
    {
        var inString = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"' && (i == 0 || line[i - 1] != '\\'))
            {
                inString = !inString;
            }
            else if (ch == '#' && !inString)
            {
                return line[..i];
            }
        }
        return line;
    }

    private static string UnquoteKey(string key) =>
        key.Length >= 2 && key[0] == '"' && key[^1] == '"' ? key[1..^1] : key;

    private static bool TryParseString(string value, out string parsed)
    {
        parsed = "";
        if (value.Length < 2 || value[0] != '"' || value[^1] != '"')
        {
            return false;
        }

        var body = value[1..^1];
        var builder = new StringBuilder(body.Length);
        for (var i = 0; i < body.Length; i++)
        {
            var ch = body[i];
            if (ch != '\\' || i == body.Length - 1)
            {
                builder.Append(ch);
                continue;
            }

            var next = body[++i];
            builder.Append(next switch
            {
                '"' => '"',
                '\\' => '\\',
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                _ => "\\" + next
            });
        }

        parsed = builder.ToString();
        return true;
    }

    private static bool TryParseStringArray(string value, out List<string> parsed)
    {
        parsed = [];
        if (!value.StartsWith("[", StringComparison.Ordinal) || !value.EndsWith("]", StringComparison.Ordinal))
        {
            return false;
        }

        var body = value[1..^1].Trim();
        if (body.Length == 0)
        {
            return true;
        }

        var current = new StringBuilder();
        var inString = false;
        foreach (var ch in body)
        {
            if (ch == '"' && (current.Length == 0 || current[^1] != '\\'))
            {
                inString = !inString;
            }

            if (ch == ',' && !inString)
            {
                if (!TryParseString(current.ToString().Trim(), out var item))
                {
                    return false;
                }
                parsed.Add(item);
                current.Clear();
                continue;
            }
            current.Append(ch);
        }

        if (!TryParseString(current.ToString().Trim(), out var last))
        {
            return false;
        }
        parsed.Add(last);
        return true;
    }

    private static bool TryParseInt(string value, out int parsed) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);

    private static bool TryParseDouble(string value, out double parsed) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed);

    private static bool TryParseBool(string value, out bool parsed)
    {
        var v = value.Trim();
        if (v.Equals("true", StringComparison.OrdinalIgnoreCase)) { parsed = true; return true; }
        if (v.Equals("false", StringComparison.OrdinalIgnoreCase)) { parsed = false; return true; }
        parsed = false;
        return false;
    }

    private static bool TryParseColor(string value, out Color color)
    {
        color = default;
        if (!TryParseString(value, out var text) ||
            text.Length != 7 ||
            text[0] != '#')
        {
            return false;
        }

        try
        {
            color = (Color)ColorConverter.ConvertFromString(text);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string NormalizeExtension(string key) =>
        key.Trim().TrimStart('.').ToLowerInvariant();

    public static string ExpandUserPath(string path)
    {
        if (path.Equals("~", StringComparison.Ordinal))
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        if (path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]);
        }

        return Environment.ExpandEnvironmentVariables(path);
    }

    private static string ResolveFontAlias(string value, bool ui) =>
        value.ToLowerInvariant() switch
        {
            "system" => "Segoe UI, Yu Gothic UI, Meiryo",
            "monospace" => "Cascadia Mono, Consolas, Yu Gothic UI",
            _ => value
        };

    public static readonly string DefaultToml =
        """
        version = 1

        [font]
        ui = "system"
        mono = "monospace"
        size = 13

        # Windows-native shortcuts.
        [shortcuts]
        reload = "f5"
        openTerminal = "ctrl+shift+t"
        togglePreview = "ctrl+shift+p"
        toggleSplit = "ctrl+backslash"
        swapPanes = "ctrl+shift+x"
        focusSearch = "ctrl+f"
        toggleHidden = "ctrl+shift+."
        goBack = "alt+left"
        goForward = "alt+right"
        goUp = "alt+up"
        openItem = "enter"
        newFolder = "ctrl+shift+n"
        newFile = "ctrl+n"
        rename = "f2"
        moveToTrash = "delete"
        compressToZip = "ctrl+k"
        extractZip = "ctrl+shift+e"
        copyItems = "ctrl+c"
        cutItems = "ctrl+x"
        pasteItems = "ctrl+v"
        selectAll = "ctrl+a"
        newTab = "ctrl+t"
        closeTab = "ctrl+w"
        nextTab = "ctrl+shift+]"
        prevTab = "ctrl+shift+["
        toggleTerminal = "ctrl+j"
        quit = "ctrl+q"

        # Startup layout:
        # [startup]
        # layout = "restore"
        # preview = "restore"
        # rightFolder = "~/Downloads"
        # rightFolders = ["~/Downloads", "~/Documents"]
        #
        # External terminal (toolbar / openTerminal) and built-in pane:
        # [terminal]
        # app = "wt.exe"             # external terminal launched by the toolbar
        # arguments = "-d {path}"
        # shell = "pwsh.exe -NoLogo" # shell for the BUILT-IN terminal pane
        # font = "Cascadia Mono"
        # fontSize = 14
        # background = "#0C0C0C"     # omit to let the window translucency show through
        # foreground = "#CCCCCC"
        # cursor = "#7DD3FC"
        # brightBlack = "#5A5A5A"    # PSReadLine history-prediction ghost text
        #
        # [openWith]
        # md = "code"
        # pdf = "C:\\Program Files\\SumatraPDF\\SumatraPDF.exe"
        #
        # User-defined commands shown in the file-pane context menu. Each runs an
        # external program (fire-and-forget). Tokens: {path} (single item),
        # {paths} (all selected, quoted), {dir} (parent folder). Filters:
        # extensions (omit or ["*"] = all), target = file|folder|any,
        # selection = single|multiple|any.
        # [[commands]]
        # name = "Open in VS Code"
        # run = "code {path}"
        # [[commands]]
        # name = "Count lines"
        # run = "pwsh -NoProfile -File \"{scripts}\\wc.ps1\" {paths}"
        # extensions = ["txt", "md", "cs"]
        # terminal = true   # show output in the built-in terminal pane
        # [[commands]]
        # name = "git push"
        # run = "git -C {cwd} push"
        # target = "current"   # acts on the current folder, no selection needed
        # requireGit = true    # only inside a Git working copy
        # terminal = true
        """;
}

public sealed class TerminalConfig
{
    public string? Command { get; set; }
    public string? Arguments { get; set; }

    // Shell command line for the BUILT-IN terminal pane (separate from the
    // external `app`/`arguments` used by the toolbar). Null = auto-detect
    // (PowerShell, then %ComSpec%). e.g. "pwsh.exe -NoLogo".
    public string? Shell { get; set; }

    // Built-in terminal pane appearance. All optional — null means "use the
    // built-in default". Font / size are separate from the file-pane font so
    // the terminal can be tuned independently.
    public string? Font { get; set; }
    public double? FontSize { get; set; }

    /// <summary>
    /// Color overrides keyed by name: background, foreground, cursor, and the
    /// 16 ANSI slots (black, red, green, yellow, blue, magenta, cyan, white,
    /// brightBlack … brightWhite). brightBlack is what PowerShell's PSReadLine
    /// uses for its history-prediction "ghost" text, so lowering it dims the
    /// suggestion preview.
    /// </summary>
    public Dictionary<string, Color> Colors { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class StartupConfig
{
    public string? Layout { get; set; }
    public string? Preview { get; set; }
    public List<string> RightFolders { get; set; } = [];
}

/// <summary>
/// A user-defined command from a <c>[[commands]]</c> entry. Shown in the file-
/// pane context menu when the current selection matches its filters, then run
/// as an external process (fire-and-forget). <see cref="Run"/> supports the
/// tokens <c>{path}</c> (single item), <c>{paths}</c> (all selected, quoted and
/// space-separated), and <c>{dir}</c> (parent folder of the first item).
/// </summary>
public sealed class UserCommand
{
    public string Name { get; set; } = "";
    public string Run { get; set; } = "";

    /// <summary>Matching extensions (normalized, no dot). Empty / contains "*" = all files.</summary>
    public List<string> Extensions { get; set; } = [];

    /// <summary>
    /// <c>file</c> / <c>folder</c> / <c>any</c> (default) match against the
    /// selected items. <c>current</c> ignores the selection entirely and targets
    /// the current folder — useful for folder-wide actions (e.g. <c>git push</c>)
    /// invoked from the empty area of the listing.
    /// </summary>
    public string Target { get; set; } = "any";

    /// <summary>"single", "multiple", or "any" (default).</summary>
    public string Selection { get; set; } = "any";

    /// <summary>
    /// When true, the command is sent to the built-in terminal pane (its output
    /// shows there) instead of being launched as a separate external process.
    /// </summary>
    public bool Terminal { get; set; }

    /// <summary>
    /// When true, the command only appears when the current folder is inside a
    /// Git working copy. Lets `git` commands show up only where they make sense.
    /// </summary>
    public bool RequireGit { get; set; }
}

public readonly record struct AppShortcut(ModifierKeys Modifiers, Key Key)
{
    public bool Matches(KeyEventArgs e)
    {
        var key = NormalizeEventKey(e);
        return key == Key && Keyboard.Modifiers == Modifiers;
    }

    public string DisplayText
    {
        get
        {
            var parts = new List<string>();
            if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            parts.Add(KeyToDisplay(Key));
            return string.Join("+", parts);
        }
    }

    public static bool TryParse(string text, out AppShortcut shortcut, out string? error)
    {
        shortcut = default;
        error = null;

        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            error = "empty shortcut";
            return false;
        }

        var modifiers = ModifierKeys.None;
        Key? key = null;
        foreach (var raw in parts)
        {
            var token = raw.ToLowerInvariant();
            switch (token)
            {
                case "ctrl":
                case "control":
                    modifiers |= ModifierKeys.Control;
                    break;
                case "opt":
                case "option":
                case "alt":
                    modifiers |= ModifierKeys.Alt;
                    break;
                case "shift":
                    modifiers |= ModifierKeys.Shift;
                    break;
                default:
                    if (key is not null)
                    {
                        error = $"multiple keys in {text}";
                        return false;
                    }
                    if (!TryParseKey(token, out var parsedKey))
                    {
                        error = $"unknown key {raw}";
                        return false;
                    }
                    key = parsedKey;
                    break;
            }
        }

        if (key is null)
        {
            error = $"missing key in {text}";
            return false;
        }

        shortcut = new AppShortcut(modifiers, key.Value);
        return true;
    }

    private static bool TryParseKey(string token, out Key key)
    {
        key = token switch
        {
            "up" => Key.Up,
            "down" => Key.Down,
            "left" => Key.Left,
            "right" => Key.Right,
            "escape" or "esc" => Key.Escape,
            "delete" => Key.Delete,
            "backspace" => Key.Back,
            "return" or "enter" => Key.Enter,
            "tab" => Key.Tab,
            "space" => Key.Space,
            "backslash" or "\\" => Key.Oem5,
            "[" => Key.Oem4,
            "]" => Key.Oem6,
            "." => Key.OemPeriod,
            "," => Key.OemComma,
            "/" => Key.Oem2,
            "-" => Key.OemMinus,
            "=" => Key.OemPlus,
            "`" or "backtick" or "grave" => Key.OemTilde,
            _ => Key.None
        };

        if (key != Key.None)
        {
            return true;
        }

        if (token.Length == 1)
        {
            var ch = char.ToUpperInvariant(token[0]);
            if (ch is >= 'A' and <= 'Z')
            {
                key = Key.A + (ch - 'A');
                return true;
            }
            if (ch is >= '0' and <= '9')
            {
                key = Key.D0 + (ch - '0');
                return true;
            }
        }

        if (token.Length is >= 2 and <= 3 &&
            token[0] == 'f' &&
            int.TryParse(token[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var functionKey) &&
            functionKey is >= 1 and <= 24)
        {
            key = Key.F1 + (functionKey - 1);
            return true;
        }

        return false;
    }

    private static Key NormalizeEventKey(KeyEventArgs e) =>
        e.Key == Key.System ? e.SystemKey :
        e.Key == Key.ImeProcessed ? e.ImeProcessedKey :
        e.Key is Key.OemBackslash ? Key.Oem5 :
        e.Key;

    private static string KeyToDisplay(Key key) =>
        key switch
        {
            Key.Oem4 => "[",
            Key.Oem6 => "]",
            Key.OemPeriod => ".",
            Key.OemComma => ",",
            Key.Oem2 => "/",
            Key.OemMinus => "-",
            Key.OemPlus => "=",
            Key.Oem5 => "\\",
            Key.OemTilde => "`",
            Key.Back => "Backspace",
            Key.Return or Key.Enter => "Enter",
            _ => key.ToString()
        };
}
