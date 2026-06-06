using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace Tfx;

/// <summary>
/// Command-line startup options. Parsed in <see cref="App.OnStartup"/> and
/// applied by <see cref="MainWindow"/>. CLI options take precedence over the
/// config.toml <c>[startup]</c> section and the saved session state.
///
/// Short flags may be combined (e.g. <c>-2Pt</c>); a non-flag argument is the
/// folder to open in the left pane (supports <c>~</c> and <c>%VARS%</c>).
/// </summary>
public sealed class StartupOptions
{
    public enum LayoutMode { Unset, Single, Split, Restore }
    public enum Toggle { Unset, On, Off }

    public LayoutMode Layout { get; private set; } = LayoutMode.Unset;
    public Toggle Preview { get; private set; } = Toggle.Unset;
    public Toggle Terminal { get; private set; } = Toggle.Unset;
    public string? FolderPath { get; private set; }
    public WindowGeometry? Geometry { get; private set; }
    public bool ShowHelp { get; private set; }
    public bool HasError { get; private set; }
    public string? ErrorMessage { get; private set; }

    public static StartupOptions Parse(string[] args)
    {
        var o = new StartupOptions();
        for (var i = 0; i < args.Length; i++)
        {
            var raw = args[i];
            if (string.IsNullOrEmpty(raw))
            {
                continue;
            }

            if (raw is "-h" or "--help" or "/?" or "/h")
            {
                o.ShowHelp = true;
            }
            // Geometry takes a value: -g/-geometry/--geometry <value>, or =/attached forms.
            else if (raw is "-g" or "-geometry" or "--geometry")
            {
                if (i + 1 < args.Length)
                {
                    o.SetGeometry(args[++i]);
                }
                else
                {
                    o.SetError($"Missing value for {raw}");
                }
            }
            else if (raw.StartsWith("--geometry=", StringComparison.Ordinal))
            {
                o.SetGeometry(raw["--geometry=".Length..]);
            }
            else if (raw.StartsWith("-geometry=", StringComparison.Ordinal))
            {
                o.SetGeometry(raw["-geometry=".Length..]);
            }
            else if (raw.StartsWith("-g", StringComparison.Ordinal) && raw.Length > 2 && raw != "-geometry")
            {
                o.SetGeometry(raw[2..]); // attached: -g1200x800
            }
            else if (raw.StartsWith("--", StringComparison.Ordinal))
            {
                o.ApplyLong(raw[2..]);
            }
            else if (raw.Length > 1 && raw[0] == '-')
            {
                foreach (var c in raw[1..])
                {
                    o.ApplyShort(c, raw);
                }
            }
            else
            {
                o.FolderPath = raw; // positional folder (last one wins)
            }
        }
        return o;
    }

    private void SetGeometry(string value)
    {
        if (WindowGeometry.TryParse(value, out var geometry))
        {
            Geometry = geometry;
        }
        else
        {
            SetError($"Invalid geometry: {value} (expected e.g. 1200x800 or 1200x800+100+50)");
        }
    }

    private void ApplyLong(string name)
    {
        switch (name.ToLowerInvariant())
        {
            case "single": Layout = LayoutMode.Single; break;
            case "split": Layout = LayoutMode.Split; break;
            case "restore": Layout = LayoutMode.Restore; break;
            case "preview": Preview = Toggle.On; break;
            case "no-preview": Preview = Toggle.Off; break;
            case "terminal": Terminal = Toggle.On; break;
            case "no-terminal": Terminal = Toggle.Off; break;
            case "help": ShowHelp = true; break;
            default: SetError($"Unknown option: --{name}"); break;
        }
    }

    private void ApplyShort(char c, string source)
    {
        switch (c)
        {
            case '1': Layout = LayoutMode.Single; break;
            case '2': Layout = LayoutMode.Split; break;
            case 'r': Layout = LayoutMode.Restore; break;
            case 'p': Preview = Toggle.On; break;
            case 'P': Preview = Toggle.Off; break;
            case 't': Terminal = Toggle.On; break;
            case 'T': Terminal = Toggle.Off; break;
            case 'h': ShowHelp = true; break;
            default: SetError($"Unknown option: -{c} (in {source})"); break;
        }
    }

    private void SetError(string message)
    {
        HasError = true;
        ErrorMessage = ErrorMessage is null ? message : ErrorMessage + Environment.NewLine + message;
    }

    public const string Usage =
        "tfx - terminal-inspired file explorer\r\n" +
        "\r\n" +
        "Usage: tfx [options] [folder]\r\n" +
        "\r\n" +
        "Options:\r\n" +
        "  -h, --help          Show this help and exit\r\n" +
        "  -1, --single        Start in single-pane layout\r\n" +
        "  -2, --split         Start in split (two-pane) layout\r\n" +
        "  -r, --restore       Restore the saved layout\r\n" +
        "  -p, --preview       Show the preview pane\r\n" +
        "  -P, --no-preview    Hide the preview pane\r\n" +
        "  -t, --terminal      Show the built-in terminal\r\n" +
        "  -T, --no-terminal   Hide the built-in terminal\r\n" +
        "  -g, --geometry G    Window geometry [WxH][+X+Y] (DIPs; -X/-Y = from right/bottom)\r\n" +
        "                      e.g. -g 1200x800  |  --geometry=1200x800+100+50\r\n" +
        "\r\n" +
        "  [folder]            Open this folder in the left pane (supports ~ and %VARS%)\r\n" +
        "\r\n" +
        "Short flags can be combined, e.g. -2Pt.\r\n" +
        "\r\n" +
        "Example:\r\n" +
        "  tfx -2 -P -t ~/Downloads\r\n" +
        "      Split layout, preview hidden, terminal shown, ~/Downloads in the left pane.\r\n";

    /// <summary>
    /// Writes the usage text (prefixed with any parse error) to the parent
    /// console when tfx was launched from a terminal; otherwise shows it in a
    /// message box. tfx is a GUI-subsystem app, so it must attach to the parent
    /// console explicitly to print.
    /// </summary>
    public void WriteHelp()
    {
        var text = (ErrorMessage is null ? "" : ErrorMessage + Environment.NewLine + Environment.NewLine) + Usage;

        if (AttachConsole(AttachParentProcess))
        {
            try
            {
                using var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
                stdout.WriteLine();
                stdout.Write(text);
            }
            catch
            {
                // Fall through to the message box below is not possible after
                // attaching; ignore write failures.
            }
            FreeConsole();
        }
        else
        {
            MessageBox.Show(text, "tfx", MessageBoxButton.OK,
                HasError ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
    }

    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();
}
