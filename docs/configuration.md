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
toggleFolderTree = "ctrl+b"
collapseFolders = "ctrl+shift+b"
toggleSplit = "ctrl+backslash"
swapPanes = "ctrl+shift+x"
focusSearch = "ctrl+f"
focusFilePane = "ctrl+1"
focusTerminal = "ctrl+2"
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
# terminal = "restore"
# folderTree = "restore"
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

When `background = 0.0`, tfx keeps a nearly invisible hit-test surface for the custom title/drag area and right-edge resize handle, so the window can still be moved and resized.

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

`terminal` controls the built-in terminal pane at startup with the same `"show"` / `"hide"` / `"restore"` values. The `-t` / `-T` command-line options override it.

`folderTree` controls the left sidebar (pinned folders + folder tree) at startup with the same `"show"` / `"hide"` / `"restore"` values.

`rightFolder` opens a single right-pane folder when `layout = "split"`:

```toml
[startup]
layout = "split"
rightFolder = "~/Downloads"
```

`rightFolders` accepts a list. Windows currently uses the first valid folder as the right-pane startup folder.

```toml
[startup]
layout = "split"
rightFolders = ["~/Downloads", "~/Documents", "~/Desktop"]
```

Paths can be absolute paths, environment-variable paths such as `%USERPROFILE%\Downloads`, or `~`-expanded user paths.

`geometry` sets the startup window size and/or position in X11 style `[WxH][+X+Y]` (DIPs; a leading `-` on an offset anchors it to the right / bottom edge). It forces a normal (non-maximized) window. The `-g` / `--geometry` command-line option overrides it.

```toml
[startup]
geometry = "1200x800+100+50"
```

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
| `toggleFolderTree` | `ctrl+b` | Show or hide the folder tree (left sidebar). |
| `collapseFolders` | `ctrl+shift+b` | Collapse every node in the folder tree back to the roots. |
| `toggleRendered` | `ctrl+shift+r` | Toggle rendered vs. source view (Markdown / HTML / CSV / JSON preview). Active only while that toggle is visible. |
| `loadExternalImages` | `ctrl+shift+i` | Load external (https) images for the current preview, once. Active only while the button is visible. |
| `toggleSplit` | `ctrl+backslash` | Show or hide split view. |
| `swapPanes` | `ctrl+shift+x` | Swap left and right panes. |
| `focusSearch` | `ctrl+f` | Focus the search field. |
| `focusFilePane` | `ctrl+1` | Move keyboard focus to the active file list (also works from inside the terminal pane). |
| `focusTerminal` | `ctrl+2` | Move keyboard focus to the built-in terminal pane; opens it first if hidden. |
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
| `newTab` | `ctrl+t` | Open a new tab in the active pane. |
| `closeTab` | `ctrl+w` | Close the active tab. |
| `nextTab` | `ctrl+shift+]` | Switch to the next tab. |
| `prevTab` | `ctrl+shift+[` | Switch to the previous tab. |
| `toggleTerminal` | `ctrl+j` | Show or hide the built-in terminal pane. (Default avoids `` ctrl+` `` because the `` ` `` key is hard to reach / IME-bound on Japanese keyboards; you can set it to `` ctrl+` `` here if your layout allows.) |
| `quit` | `ctrl+q` | Quit the application (saves the session and tears down the terminal). Ignored while the terminal pane is focused so the shell keeps `Ctrl+Q`; `Alt+F4` always closes the window. |

The `` ` `` (backtick / grave) key token is accepted for `toggleTerminal`; `[` and `]` are accepted for the tab-cycle shortcuts.

If two actions resolve to the same shortcut, the later conflicting override is ignored and tfx reports a status warning.

### `[terminal]`

Configures both the **external** terminal (toolbar button / `openTerminal` shortcut) and the appearance of the **built-in** terminal pane (`toggleTerminal`, default `Ctrl+J` / toolbar toggle).

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

#### Built-in terminal appearance

The `app` / `arguments` keys above configure the **external** terminal. The keys below style the **built-in** terminal pane (toggled with `toggleTerminal`, default `Ctrl+J`, or the toolbar button):

| Key | Type | Default | Description |
| --- | --- | --- | --- |
| `shell` | string | (auto) | Shell command line for the built-in pane, e.g. `"pwsh.exe -NoLogo"`. When omitted, tfx uses Windows PowerShell, falling back to `%ComSpec%` / cmd. Environment variables are expanded. |
| `font` | string | (built-in) | Font family for the built-in pane. `monospace` resolves to `Cascadia Mono, Consolas, Yu Gothic UI`. |
| `fontSize` | number | (session) | Font size (`8`–`40`). `size` is an accepted alias. Takes precedence over the persisted size. |
| color keys | string `#RRGGBB` | Campbell | Palette overrides (see below). |

`shell` selects the shell for the **built-in** terminal pane only — it's independent of `app` (which launches the **external** terminal). Example: `shell = "pwsh.exe -NoLogo"` to use PowerShell 7.

The terminal supports 16-color, xterm 256-color, and 24-bit truecolor SGR sequences. If `background` is omitted, the terminal surface stays transparent so the window's `[opacity] background` translucency shows through; set `background` to force an opaque color.

Recognized color keys (all quoted `#RRGGBB`): `background`, `foreground`, `cursor`, and the 16 ANSI slots `black`, `red`, `green`, `yellow`, `blue`, `magenta`, `cyan`, `white`, `brightBlack`, `brightRed`, `brightGreen`, `brightYellow`, `brightBlue`, `brightMagenta`, `brightCyan`, `brightWhite`.

```toml
[terminal]
shell = "pwsh.exe -NoLogo"
font = "Cascadia Mono"
fontSize = 14
foreground = "#CCCCCC"
cursor = "#7DD3FC"
brightBlack = "#5A5A5A"   # PSReadLine history-prediction "ghost" text
# background = "#0C0C0C"   # omit to let window translucency show through
```

If `background` is **omitted**, the terminal surface is transparent and follows the tfx window's translucency (`[opacity] background`). Set it to a `#RRGGBB` value to force an opaque background.

`brightBlack` is the color PowerShell's PSReadLine uses for its inline history prediction. The built-in default is already dimmed (`#5A5A5A`); lower it (e.g. `#3C3C3C`) to make the suggestion preview more subtle, or raise it to make it more visible.

Dragging files or folders from a file pane onto the terminal types their full paths at the prompt (space-separated; paths with spaces are double-quoted).

#### Built-in terminal operations

- **Interrupt** — the `^C` button in the terminal pane header sends an interrupt (Ctrl+C / ETX `0x03`) to the running command. This is the dependable way to interrupt because the in-page keyboard `Ctrl+C` isn't delivered to the shell on all setups.
- **Copy** — `Ctrl+C` with a selection, or `Ctrl+Shift+C`, copies the selection to the clipboard. `Ctrl+C` with no selection sends the interrupt instead.
- **Paste** — `Ctrl+V` or `Ctrl+Shift+V` pastes clipboard text into the shell.
- **Insert paths** — drag files / folders from a file pane onto the terminal to type their paths (see above).
- **Close** — the `×` button, `Ctrl+J`, or typing `exit` closes the pane. Reopening starts a fresh shell.

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

### `[[commands]]`

User-defined commands shown in the file-pane context menu. Each entry runs an external program (fire-and-forget — tfx does not capture output or wait), which makes any installed interpreter (PowerShell, cmd, Git Bash, Python, …) usable without an embedded scripting runtime. A command appears in the menu only when the current selection matches all of its filters.

```toml
[[commands]]
name = "Open in VS Code"
run = "code {path}"

[[commands]]
name = "Optimize PNG"
run = "pwsh -File C:\\scripts\\optipng.ps1 {paths}"
extensions = ["png"]

[[commands]]
name = "Open Git Bash here"
run = "C:\\Program Files\\Git\\bin\\bash.exe --login -i"
target = "folder"
selection = "single"

[[commands]]
name = "Count lines / words / chars"
run = "pwsh -NoProfile -File \"{scripts}\\wc.ps1\" {paths}"
target = "file"
terminal = true   # show the output in the built-in terminal pane

[[commands]]
name = "git push"
run = "git -C {cwd} push"
target = "current"   # acts on the current folder; shows with nothing selected
requireGit = true    # only inside a Git working copy
terminal = true      # show push output in the Output tab
shortcut = "ctrl+shift+p"   # run with a keypress
```

A command launched as a separate process (the default, `terminal = false`) does **not** show its standard output anywhere — tfx neither captures it nor keeps the window open. To see output, set `terminal = true`: the command's stdout / stderr are captured and shown in the terminal pane's **Output** tab (a read-only sink, separate from the interactive **Shell** tab). The tab strip appears once the Output tab has content. Alternatively, make the launched program keep its own window open (e.g. `pwsh -NoExit ...`).

#### Multi-line scripts

`run` can hold a whole script using TOML's multi-line literal string (`'''…'''`). The body is taken verbatim (no escaping), tokens are still substituted, and tfx runs it with the command's own `shell` if set, otherwise the **`[terminal] shell`** (falling back to PowerShell when neither is set). The script is written to a temp file and invoked in the form that shell expects: PowerShell → `.ps1` with `-NoProfile -ExecutionPolicy Bypass -File`, `cmd` → `.bat` with `/c`, `bash` / `sh` → `.sh`. Combine with `terminal = true` to see the output:

```toml
[[commands]]
name = "Image info"
extensions = ["png", "jpg", "jpeg", "gif"]
terminal = true
run = '''
$f = {path}
$img = [System.Drawing.Image]::FromFile($f)
Write-Output ("{0}  {1} x {2}  {3:N0} bytes" -f (Split-Path $f -Leaf), $img.Width, $img.Height, (Get-Item $f).Length)
$img.Dispose()
'''

# Run a script with a specific shell (cmd here) regardless of [terminal] shell:
[[commands]]
name = "Dir listing"
target = "current"
terminal = true
shell = "cmd"
run = '''
@echo off
dir /b {cwd}
'''
```

Inside the script the tokens (`{path}`, `{paths}`, `{stem}`, …) expand to quoted strings, so `$f = {path}` becomes e.g. `$f = "C:\pics\a.png"`.

| Key | Type | Default | Description |
| --- | --- | --- | --- |
| `name` | string | (required) | Label shown in the context menu. |
| `run` | string | (required) | Command line to launch. Environment variables are expanded. Supports the tokens below. |
| `extensions` | string array | all | Matching extensions without the leading dot. Omitted or `["*"]` matches every file. |
| `target` | string | `any` | `file`, `folder`, `current`, or `any`. `file` / `folder` match the selected items; `current` ignores the selection and acts on the **current folder** (the command shows even with nothing selected — right-click the empty area), useful for folder-wide actions like `git push`. |
| `selection` | string | `any` | `single`, `multiple`, or `any` — restrict by how many items are selected. (Ignored when `target = "current"`.) |
| `requireGit` | bool | `false` | When `true`, the command appears only when the current folder is inside a Git working copy. |
| `terminal` | bool | `false` | When `true`, the command's stdout / stderr stream into the terminal pane's read-only **Output** tab instead of launching a separate process. The pane opens and switches to the Output tab automatically. |
| `shortcut` | string | (none) | Keyboard shortcut, same grammar as `[shortcuts]` (e.g. `"ctrl+shift+g"`). Pressing it runs the command when the current context matches its filters. Shown in the context menu next to the command name. Command shortcuts take precedence over built-in ones. |
| `shell` | string | (none) | Shell used to run a multi-line `run` script for this command (e.g. `"cmd"`, `"pwsh.exe -NoLogo"`, `"bash"`). Overrides `[terminal] shell` for this command only. Ignored for single-line commands (which name their own executable). |

Tokens substituted in `run` (path tokens are double-quoted automatically):

- `{path}` — the first selected item's full path (or the current folder when nothing is selected).
- `{paths}` — every selected item, space-separated (or the current folder when nothing is selected).
- `{dir}` — the folder the item sits in (parent of the first item; the current folder when nothing is selected).
- `{name}` — the first item's file name **with** extension, e.g. `report.pdf`.
- `{stem}` — the file name **without** extension, e.g. `report`.
- `{ext}` — the extension only, without the dot, e.g. `pdf` (empty for folders).
- `{cwd}` — the current folder, regardless of selection.
- `{scripts}` — the `scripts` folder next to `config.toml` (`%APPDATA%\tfx\scripts`), created on demand. Use it to ship scripts alongside the config without hard-coding an absolute path — e.g. `run = "pwsh -File \"{scripts}\\wc.ps1\" {paths}"`. (Unlike the path tokens, `{scripts}` is substituted raw, so quote it yourself when the path may contain spaces.)

All filters must match for the command to appear: e.g. `extensions = ["png", "jpg"]` with `selection = "single"` shows the command only when exactly one `.png` or `.jpg` file is selected. An entry missing `name` or `run` is reported as a config warning and skipped.

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
