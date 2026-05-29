# tfx for Windows Configuration

English | [日本語](configuration.ja.md)

tfx for Windows stores user-editable configuration under:

```text
%APPDATA%\tfx\
```

The main user-editable configuration file is `config.toml`. tfx creates it on startup when it does not already exist. Existing files are not overwritten.

Session state such as window placement, the last-opened paths, pinned folders, column layout, and view mode is still saved automatically to `settings.json`. Use `config.toml` for hand-written preferences; use `settings.json` as app-owned state.

## Current Scope

`config.toml` supports these sections:

- Top-level `version = 1`
- `[font]`
- `[colors]`
- `[opacity]`
- `[startup]`
- `[shortcuts]`
- `[terminal]`
- `[openWith]`

The loader intentionally accepts a small TOML subset:

- Tables with `[section]` headers
- Assignments with `key = value`
- Double-quoted strings
- Arrays of double-quoted strings for `rightFolders`
- Numeric font sizes and opacity values
- Quoted `#RRGGBB` colors
- `#` comments outside quoted strings

Unsupported sections and unsupported keys are ignored. Invalid values fall back to built-in defaults and surface a status warning instead of crashing the app.

## Default File

New installations create a Windows-native file like this:

```toml
version = 1

[font]
ui = "system"
mono = "monospace"
size = 13

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

# [startup]
# layout = "single"
# preview = "restore"
# rightFolder = "~/Downloads"
# rightFolders = ["~/Downloads", "~/Documents"]

# [terminal]
# app = "wt.exe"
# arguments = "-d {path}"

# [openWith]
# md = "code"
# pdf = "C:\\Program Files\\SumatraPDF\\SumatraPDF.exe"
```

## Keys

### `version`

Required top-level integer.

```toml
version = 1
```

Only `1` is supported.

### `[font]`

Controls the app-wide font family and base size.

```toml
[font]
ui = "system"
mono = "monospace"
size = 13
```

| Key | Type | Default | Description |
| --- | --- | --- | --- |
| `ui` | string | `"system"` | UI font for tree/header/dialog-like surfaces. |
| `mono` | string | `"monospace"` | Monospaced font for file listings, status text, raw text, JSON, and CSV previews. |
| `size` | number | `13` | Base font size. Valid range: `8` through `40`. |

On Windows, `"system"` maps to `Segoe UI, Yu Gothic UI, Meiryo`. `"monospace"` maps to `Cascadia Mono, Consolas, Yu Gothic UI`. Other strings are passed to WPF as font-family names.

### `[colors]`

Overrides semantic color tokens. Values must be quoted `#RRGGBB` colors.

```toml
[colors]
fileListBackground = "#000301"
fileForeground = "#CFFFCF"
directoryForeground = "#6FFF80"
```

Windows currently maps these tokens to the nearest WPF theme resources:

| Key | Current Windows effect |
| --- | --- |
| `fileListBackground` | Main panel/list background. |
| `headerBackground` | Top toolbar, status bar, and app-background fallback. |
| `inputBackground` | Path box, text boxes, scroll tracks, and preview text/CSV background. |
| `fileListRowAlternate` | Alternating file-list and CSV rows. |
| `fileListRowHovered` | Hover background for toolbar/menu/tree surfaces. |
| `fileListRowDropTarget` | Hover fallback when `fileListRowHovered` is omitted. |
| `fileListRowSelected` | Selected row, selected icon item, selected toggle, and active-pane fallback. |
| `fileListRowSelectedForeground` | Foreground used on selected rows and selected icons. If omitted, tfx chooses black or white from the selection background. |
| `fileForeground` | Main foreground text. |
| `directoryForeground` | Accent color fallback. |
| `secondaryForeground` | Muted text. |
| `headerForeground` | Muted text fallback. |
| `titleBarBackgroundActive` | Active file-pane background. |
| `titleBarBackgroundInactive` | Inactive file-pane background. |
| `paneBorderInactive` | Default border color. |
| `paneBorderActive` | Focus border fallback. |
| `paneBorderKeyboardTarget` | Focus border color. |
| `splitHandleActive` | Accent fallback. |
| `folderTreeSelectedInactive` | Inactive tree selection background. |
| `disabledForeground` | Disabled menu/control foreground. |
| `scrollbarThumb` | Scrollbar thumb. |
| `scrollbarThumbHovered` | Scrollbar thumb on hover. |
| `scrollbarThumbDragging` | Scrollbar thumb while dragging. |

The following macOS tfx color keys are accepted by the parser but are not yet wired to dedicated WPF elements: `statusLineForegroundActive`, `statusLineForegroundInactive`, `statusLineBackground`, `folderTreeBackground`, `folderTreeForeground`, `folderTreeSelectedForeground`, `folderTreeFolderIcon`, `folderTreeSelectedActive`, `folderTreeSectionHeader`, `splitHandleIdle`, `gitModified`, `gitAdded`, `gitDeleted`, `gitRenamed`, `gitUntracked`, `gitIgnored`, and `gitConflicted`.

### `[opacity]`

Opacity values must be numbers from `0` through `1`.

```toml
[opacity]
background = 0.92
inactivePane = 0.5
disabledItem = 0.45
```

| Key | Windows behavior |
| --- | --- |
| `background` | Applies to the transparent WPF window surface so the app background can show content behind the window. |
| `inactivePane` | Applies to the inactive file-pane surface. If omitted, `background` is used. |
| `disabledItem` | Accepted for tfx compatibility. WPF disabled-control opacity is still defined by the application styles. |

### `[startup]`

Controls the pane layout used at startup.

```toml
[startup]
layout = "split"
preview = "show"
rightFolders = ["~/Downloads", "~/Documents"]
```

| Value | Behavior |
| --- | --- |
| `"single"` | Starts with one visible file pane. |
| `"split"` | Starts with two visible file panes. |
| `"restore"` | Uses the saved split/single state from `settings.json`. |

`preview` controls the preview pane used at startup:

| Value | Behavior |
| --- | --- |
| `"show"` | Starts with the preview pane visible. |
| `"hide"` | Starts with the preview pane hidden. |
| `"restore"` | Uses the saved preview-pane state from `settings.json`. |

`rightFolder` opens a single right-pane folder when `layout = "split"`:

```toml
[startup]
layout = "split"
rightFolder = "~/Downloads"
```

`rightFolders` accepts a list. Windows currently uses the first valid folder as the right-pane startup folder; pane tabs are not implemented in this port.

```toml
[startup]
layout = "split"
rightFolders = ["~/Downloads", "~/Documents", "~/Desktop"]
```

Paths can be absolute paths, environment-variable paths such as `%USERPROFILE%\Downloads`, or `~`-expanded user paths.

### `[shortcuts]`

Shortcuts are written with Windows-native modifier names.

```toml
[shortcuts]
reload = "f5"
goBack = "alt+left"
goForward = "alt+right"
goUp = "alt+up"
openTerminal = "ctrl+shift+t"
```

Supported modifier tokens:

| Token | Meaning |
| --- | --- |
| `ctrl`, `control` | Ctrl |
| `shift` | Shift |
| `alt` | Alt |

Supported key tokens:

| Token | Meaning |
| --- | --- |
| Single letters or digits | That key |
| `.`, `,`, `/`, `-`, `=`, `[`, `]`, `backslash` | Punctuation keys |
| `up`, `down`, `left`, `right` | Arrow keys |
| `escape`, `esc` | Escape |
| `delete`, `backspace` | Delete / Backspace |
| `return`, `enter` | Enter |
| `tab` | Tab |
| `space` | Space |
| `f1` through `f24` | Function keys |

Supported action keys:

| Key | Default | Action |
| --- | --- | --- |
| `reload` | `f5` | Reload the active file pane. |
| `openTerminal` | `ctrl+shift+t` | Open the configured external terminal at the active folder. |
| `togglePreview` | `ctrl+shift+p` | Show or hide the preview pane. |
| `toggleSplit` | `ctrl+backslash` | Show or hide split view. |
| `swapPanes` | `ctrl+shift+x` | Swap left and right panes. |
| `focusSearch` | `ctrl+f` | Focus the search field. |
| `toggleHidden` | `ctrl+shift+.` | Show or hide hidden files. |
| `goBack` | `alt+left` | Navigate back. |
| `goForward` | `alt+right` | Navigate forward. |
| `goUp` | `alt+up` | Navigate to the parent folder. |
| `openItem` | `enter` | Open the selected item. |
| `newFolder` | `ctrl+shift+n` | Create a folder and start inline name editing. |
| `newFile` | `ctrl+n` | Create a file and start inline name editing. |
| `rename` | `f2` | Rename the selected item inline. |
| `moveToTrash` | `delete` | Move selected items to the Recycle Bin. |
| `compressToZip` | `ctrl+k` | Compress selected items to a zip archive. |
| `extractZip` | `ctrl+shift+e` | Extract selected zip archives. |
| `copyItems` | `ctrl+c` | Copy selected items. |
| `cutItems` | `ctrl+x` | Cut selected items. |
| `pasteItems` | `ctrl+v` | Paste into the active folder. |
| `selectAll` | `ctrl+a` | Select all visible items. |

If two actions resolve to the same shortcut, the later conflicting override is ignored and tfx reports a status warning.

### `[terminal]`

Overrides the external terminal used by the toolbar button and `openTerminal` shortcut.

```toml
[terminal]
app = "C:\Program Files\WezTerm\wezterm-gui.exe"
arguments = "start --cwd {path}"
```

`app` can be an executable on `PATH`, an app execution alias such as `wt.exe` or `pwsh.exe`, or an absolute path. Windows paths may be written naturally with single backslashes. `arguments` is optional. `{path}` is replaced with the active pane's current folder and is quoted safely by tfx.

When `[terminal]` is omitted, tfx opens `wt.exe` at the active pane's folder and falls back to PowerShell if Windows Terminal is unavailable. If `app` is set but `arguments` is omitted, tfx supplies folder-opening arguments for `wt.exe`, WezTerm, PowerShell, and pwsh.

Examples:

```toml
[terminal]
app = "pwsh.exe"
arguments = "-NoExit -Command Set-Location -LiteralPath {path}"
```

```toml
[terminal]
app = "code"
arguments = "-r {path}"
```

tfx for Windows does not currently implement a built-in terminal pane. This setting launches an external terminal/application.

### `[openWith]`

Overrides the app used when opening files by extension. Keys are extensions without the leading dot.

```toml
[openWith]
md = "code"
txt = "notepad.exe"
pdf = "C:\\Program Files\\SumatraPDF\\SumatraPDF.exe"
```

Compound extension keys can be quoted:

```toml
[openWith]
"tar.gz" = "C:\\Tools\\ArchiveViewer\\ArchiveViewer.exe"
```

Directories, zip navigation, and archive-internal files keep their existing tfx behavior.

## Examples

Use a larger file-list font:

```toml
version = 1

[font]
ui = "system"
mono = "Cascadia Mono"
size = 14
```

Use Explorer-like navigation shortcuts with a custom terminal:

```toml
version = 1

[shortcuts]
reload = "f5"
goBack = "alt+left"
goForward = "alt+right"
goUp = "alt+up"
openTerminal = "ctrl+shift+t"

[terminal]
app = "wt.exe"
arguments = "-d {path}"
```

Change the most visible colors:

```toml
version = 1

[colors]
fileListBackground = "#020A12"
fileForeground = "#D6F7FF"
directoryForeground = "#66D9FF"
secondaryForeground = "#4A91A8"
titleBarBackgroundActive = "#06354A"
titleBarBackgroundInactive = "#03131D"
paneBorderKeyboardTarget = "#8AEFFF"
paneBorderInactive = "#1D5265"
```

### Color Samples

The following samples use only color keys that currently affect the Windows WPF theme. Copy one block into `config.toml`, then adjust individual tokens.

#### Evergreen Terminal

A high-contrast black and green look for terminal-style browsing.

```toml
version = 1

[colors]
fileListBackground = "#020603"
headerBackground = "#050A06"
fileListRowSelected = "#12351E"
fileForeground = "#D8FFE0"
directoryForeground = "#67E887"
secondaryForeground = "#78A883"
headerForeground = "#9DF2AA"
titleBarBackgroundActive = "#0F3A1A"
titleBarBackgroundInactive = "#07150A"
paneBorderInactive = "#1A4A27"
paneBorderActive = "#4CAF65"
paneBorderKeyboardTarget = "#8DFF9E"
splitHandleActive = "#67E887"
```

#### Amber Desk

A warm amber console palette with strong active-pane separation.

```toml
version = 1

[colors]
fileListBackground = "#120800"
headerBackground = "#1C0D00"
fileListRowSelected = "#3A2406"
fileForeground = "#FFE7B0"
directoryForeground = "#FFB84D"
secondaryForeground = "#B87928"
headerForeground = "#FFD36A"
titleBarBackgroundActive = "#4A2A08"
titleBarBackgroundInactive = "#1C0D00"
paneBorderInactive = "#5A3510"
paneBorderActive = "#B87928"
paneBorderKeyboardTarget = "#FFD36A"
splitHandleActive = "#FFD36A"
```

#### Frost Graphite (Light Mode)

A light-mode graphite theme for bright rooms while keeping green directory accents.

```toml
version = 1

[colors]
fileListBackground = "#F7FAF5"
headerBackground = "#EAF3E7"
inputBackground = "#FFFFFF"
fileListRowAlternate = "#EEF6EB"
fileListRowHovered = "#E3F1DE"
fileListRowSelected = "#DDEFD8"
fileListRowSelectedForeground = "#102014"
fileForeground = "#18221A"
directoryForeground = "#167A3A"
secondaryForeground = "#5D6B60"
headerForeground = "#1C6B37"
titleBarBackgroundActive = "#D5EBD0"
titleBarBackgroundInactive = "#EAF3E7"
paneBorderInactive = "#C5D5C2"
paneBorderActive = "#6AA66F"
paneBorderKeyboardTarget = "#17813A"
splitHandleActive = "#17813A"
folderTreeSelectedInactive = "#E3F1DE"
disabledForeground = "#8A958C"
scrollbarThumb = "#AFC3AD"
scrollbarThumbHovered = "#91AB91"
scrollbarThumbDragging = "#739172"
```

Start in split view with a known right pane:

```toml
version = 1

[startup]
layout = "split"
rightFolder = "%USERPROFILE%\\Downloads"
```

## Error Handling

tfx treats these as configuration errors:

- Missing or invalid assignment syntax, such as `size: 13`
- Unsupported top-level `version`
- Non-string `ui`, `mono`, `terminal`, or `openWith` values
- Font `size` outside `8` through `40`
- Color values that are not quoted `#RRGGBB` strings
- Opacity values outside `0` through `1`
- Unknown shortcut modifiers or keys
- Shortcut conflicts

When an error is found, tfx keeps running and falls back to built-in defaults for the affected setting.
