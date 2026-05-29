# tfx for Windows Configuration

English | [日本語](configuration.ja.md)

tfx for Windows stores user-editable configuration under:

```text
%APPDATA%\tfx\
```

The main user-editable configuration file is `config.toml`. If it does not exist, tfx creates the default file on startup. Session state such as window placement, last-opened paths, pinned folders, and column layout is still saved automatically to `settings.json`.

## Compatible Syntax

`config.toml` accepts the same `version = 1` and section names used by the macOS tfx configuration where they make sense on Windows. On Windows, `cmd` / `command` shortcut modifiers are interpreted as `Ctrl`, so examples from the macOS edition can be shared as-is:

```toml
version = 1

[shortcuts]
reload = "cmd+r"
openTerminal = "cmd+t"
togglePreview = "cmd+p"
toggleSplit = "cmd+backslash"
swapPanes = "cmd+shift+x"
focusSearch = "cmd+f"
toggleHidden = "cmd+shift+."
goBack = "cmd+["
goForward = "cmd+]"
goUp = "cmd+up"
```

Windows-native names such as `ctrl` / `control`, `shift`, and `alt` are also accepted.

## Supported Sections

The supported sections are `[font]`, `[colors]`, `[opacity]`, `[shortcuts]`, `[startup]`, `[terminal]`, and `[openWith]`. Unsupported keys are ignored.

```toml
version = 1

[font]
ui = "system"
mono = "monospace"
size = 13

[startup]
layout = "split"
rightFolders = ["~/Downloads", "~/Documents"]

[terminal]
app = "wt.exe"
arguments = "-d {path}"

[openWith]
md = "code"
pdf = "C:\\Program Files\\SumatraPDF\\SumatraPDF.exe"
```

`[terminal] app` names a Windows executable, absolute path, or app execution alias. `arguments` is a Windows extension for the argument template; `{path}` is replaced with the current folder.

`[openWith]` keys are extensions without the leading dot. Values can be executable names, absolute paths, or app execution aliases.

`[colors]` accepts the semantic color names from macOS tfx. Windows maps them onto the nearest WPF theme resources. The most useful keys today are `fileListBackground`, `fileForeground`, `directoryForeground`, `secondaryForeground`, `titleBarBackgroundActive`, `titleBarBackgroundInactive`, `paneBorderInactive`, and `paneBorderKeyboardTarget`.
