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

        foreach (var rawLine in toml.Replace("\r\n", "\n").Split('\n'))
        {
            var line = StripComment(rawLine).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
            {
                section = line[1..^1].Trim();
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

    private static void ParseTerminal(AppConfig config, string key, string value)
    {
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

        # Startup layout:
        # [startup]
        # layout = "restore"
        # preview = "restore"
        # rightFolder = "~/Downloads"
        # rightFolders = ["~/Downloads", "~/Documents"]
        #
        # Windows aliases:
        # [terminal]
        # app = "wt.exe"
        # arguments = "-d {path}"
        #
        # [openWith]
        # md = "code"
        # pdf = "C:\\Program Files\\SumatraPDF\\SumatraPDF.exe"
        """;
}

public sealed class TerminalConfig
{
    public string? Command { get; set; }
    public string? Arguments { get; set; }
}

public sealed class StartupConfig
{
    public string? Layout { get; set; }
    public string? Preview { get; set; }
    public List<string> RightFolders { get; set; } = [];
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
            Key.Back => "Backspace",
            Key.Return or Key.Enter => "Enter",
            _ => key.ToString()
        };
}
