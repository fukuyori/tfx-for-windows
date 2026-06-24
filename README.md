# tfx for Windows

**Terminal-inspired interface File eXplorer**
Pronunciation: **Tafix**
Version: 0.8.7

[English](README.md) | [日本語](README.ja.md)

[![License: Apache 2.0](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](LICENSE)

A keyboard-friendly, dark-themed file explorer for Windows. C# / WPF port of the macOS edition at <https://github.com/fukuyori/tfx>.

- Repository: <https://github.com/fukuyori/tfx-for-windows>
- Author: fukuyori (<self@spumoni.org>)
- Release notes: [CHANGELOG.md](CHANGELOG.md)
- Development roadmap: [docs/roadmap.md](docs/roadmap.md)
- Configuration guide: [docs/configuration.md](docs/configuration.md) / [日本語](docs/configuration.ja.md)
- Contributing guide: [docs/contributing.md](docs/contributing.md)

---

## Highlights

- Two file panes (single or split) with independent navigation history
- Folder tree sidebar (toolbar show/hide toggle and collapse-all button; single-click shows a folder in the list, double-click expands / collapses it) plus persistent pinned folders
- Editable address bar with clickable breadcrumb segments and free-text input
- Two view modes: **Details** (multi-column metadata) and **Icons** (large-icon grid), with monochrome, theme-aware file-type icons
- New File (`.txt`) / New Folder with Explorer-style inline naming — type the name and press Enter, or click the empty list area to confirm; inline rename; drag-and-drop with full Windows modifier-key conventions and an Explorer-style translucent drag preview; shortcut (`.lnk`) creation
- Copy / move / paste through the Windows shell: the standard progress dialog (time remaining, speed, cancel) appears for long operations, with native replace / skip / keep-both conflict prompts (copying into the same folder makes a "- Copy"); lists refresh in place, preserving the selection and keyboard focus
- Paste clipboard content as a new file when the clipboard holds no files: spreadsheet/CSV → `.csv`, image → `.png` (incl. the DIB/DIBV5 scanners and PDF viewers use), URL → `.url`, rich text → `.rtf`, text → `.txt`. A "Paste special" context submenu lets you choose the format (including HTML)
- Zip compression and extraction from the file pane or context menu, plus read-only browsing inside `.zip` files (open, drag out to other apps)
- Right-click context menu (Windows 11–style ordering), "Open with..." dialog, sortable columns, customizable column visibility and order
- Image / text preview pane with rendered Markdown, HTML, CSV / TSV tables, and pretty-printed JSON (toggle between rendered view and source)
- Multi-selection preview: when more than one item is selected, the preview pane shows a compact summary (count, combined size, per-item name / kind / size / modified) up to a cap of 8 entries
- Recursive subfolder search: type a query in the search box and press **Enter** to walk the current folder's subtree on a background thread, streaming matches into the active pane with live status-bar progress; **Esc** cancels and restores the real listing
- Git working-copy integration: when inside a Git repository, file rows show a one-character status badge (M / A / ? / D / R / C / U) in the **Git** column and the current branch appears in the status bar as `⎇ name`
- USB / removable drive hot detection: the folder-tree drive list refreshes when devices are added or removed (via `WM_DEVICECHANGE`)
- User-editable `%APPDATA%\tfx\config.toml` for tfx-compatible font, color, shortcut, startup, terminal, and per-extension open-with settings
- Light and translucent themes from `config.toml`, including custom WPF chrome for transparent windows
- Configurable terminal launcher: "Open Terminal here" opens at the active pane's folder by default, or uses a user-specified executable and optional argument template from **Terminal Settings...** or `config.toml`
- Built-in terminal pane (xterm.js in WebView2, toggled with `Ctrl+J`): a real ConPTY shell docked at the window bottom — configurable shell / font / colors via `config.toml [terminal]`, a header `^C` interrupt button, copy / paste, and drag files from a pane onto it to insert their paths. Closing the pane keeps the session running so reopening resumes it (with its scrollback and pane height); the session ends only when the shell exits (e.g. you type `exit`)
- Status bar with item counts, selection size, active drive's free space (cached and refreshed in the background to stay responsive on slow network drives), Git branch, and the current version
- Japanese / English UI based on the OS UI language
- All view state, paths, pinned folders, column layout, and view mode are persisted
- File panes auto-refresh on external changes via `FileSystemWatcher` (with a low-frequency periodic fallback) and apply a diff so scroll position and selection are preserved

---

## UI overview

```text
+-- Toolbar -----------------------------------------------------------+
| Back Forward Up Pin |      drag area      [Search] | View Hidden    |
| Terminal Reload Preview Split Swap Columns | Min Max Close           |
+--------------+----------------+----------------+--------------------+
| PINNED       | Address bar    | Address bar    | PREVIEW            |
| folder list  | Left file view | Right file view| info / image / text|
| pinned paths |                |                |                    |
| FOLDERS tree |                |                |                    |
+--------------+----------------+----------------+--------------------+
| <path>  K of N selected (size)   C:\  120 GB free of 476 GB  0.8.7 |
+---------------------------------------------------------------------+
```

The active file pane is outlined with a bright green border.

---

## Toolbar

Buttons use **Segoe Fluent Icons** with **Segoe MDL2 Assets** fallback. Hover for tooltip + shortcut.

| Group | Buttons |
| --- | --- |
| Navigation | Back, Forward, Up, Pin / unpin current folder |
| Drag / search | Empty drag area, Search, Focus search |
| View | View mode (Details / Icons), Toggle hidden files |
| Utility | Open Terminal here, Reload, Preview toggle, Split toggle, Swap panes, Columns |
| Window | Minimize, maximize / restore, close |
| Status bar | Item count / selection size, current Git branch (if inside a working copy), free space on the active drive, version |

File operations such as New Folder, New File, Rename, Zip, Copy, Cut, and Paste are available from the keyboard and context menu.

The native title bar is replaced by custom chrome when transparency is enabled. Drag the empty area in the top toolbar to move the window, double-click it to maximize or restore, and drag the right edge to resize the window width.

---

## Keyboard

| Key | Action |
| --- | --- |
| `Enter` | Open selected file / Enter selected folder |
| `Backspace` | Parent folder |
| `Alt + Left` / `Alt + Right` | Back / Forward |
| `Alt + Up` / `Backspace` | Parent folder |
| `F2` | Inline rename (Details mode) |
| `Delete` | Move selection to Recycle Bin |
| `Shift + Delete` | Permanently delete selection |
| `Ctrl + C` / `Ctrl + X` / `Ctrl + V` | Copy / Cut / Paste |
| `Ctrl + Shift + V` | Paste clipboard text as a `.txt` file |
| `Ctrl + A` | Select all in active view |
| `Ctrl + Shift + N` | New folder |
| `Ctrl + N` | New file |
| `Ctrl + K` | Compress selected items to a zip archive |
| `Ctrl + Shift + E` | Extract selected zip archive(s) |
| `F5` | Reload |
| `Ctrl + F` | Focus Search (subfolder search — type and press Enter) |
| `Ctrl + L` / `F4` | Focus address bar (edit path) |
| `Tab` / `Shift + Tab` | Switch focus to the other file pane (split view only; only when focus is already in a pane) |
| `Left` / `Right` | Move focus between file panes (only when focus is already in a pane) |
| `Ctrl + 1` | Move focus to the file list |
| `Ctrl + 2` | Move focus to the terminal pane (opens it if hidden) |
| `Up` / `Down` | Move selection in active view |
| `Shift + Up` / `Shift + Down` | Extend selection (range) |
| `Ctrl + Shift + T` | Open Terminal in current folder |
| `Ctrl + Shift + .` | Toggle hidden files |
| `Ctrl + \` | Toggle split pane |
| `Ctrl + B` | Toggle folder tree (sidebar) |
| `Ctrl + Shift + B` | Collapse all folders in the tree |
| `Ctrl + Shift + P` | Toggle preview pane |
| `Ctrl + Shift + R` | Toggle rendered / source view (while a Markdown / HTML / CSV / JSON preview is shown) |
| `Ctrl + Shift + I` | Load external images for the current preview (when offered) |
| `Ctrl + Shift + X` | Swap left and right panes (split view only) |
| `Esc` | Cancel rename / clear search / exit address-bar edit |

---

## Mouse

- **Single click** on tree node -> navigate active pane to that folder
- **Click** on the expander triangle -> expand/collapse only (no navigate)
- **Single click** on row / icon -> select; **Ctrl** / **Shift** for multi-select
- **Double-click** on row / icon -> open
- After entering a folder, the parent row (`..`) is selected and focused. When returning to the parent with `..` or Backspace, the folder you came from is selected and focused.
- **Right-click** -> context menu (Open, Open with..., Reveal, Pin / Unpin, Cut, Copy, Paste, Copy current path, Compress, Extract, New folder, New file, Open Terminal, Terminal Settings..., Rename, Trash, Delete permanently for the selected item)
- **Click** on a breadcrumb segment -> jump to that ancestor
- **Click** on the empty area of the address bar -> switch to free-text edit mode
- **Click** column header -> sort by that column (toggle ascending / descending)
- **Drag** the Name column boundary in the header -> resize the file-name column

---

## Drag and drop

Works within tfx, between tfx and Windows Explorer, or any other app that exchanges `FileDrop` data.

| Modifier | Effect | Cursor |
| --- | --- | --- |
| (none) | Move within same drive, otherwise Copy | Move / Copy |
| `Shift` | Force Move | Move |
| `Ctrl` | Force Copy | Copy |
| `Alt` | Create shortcut (`.lnk`) at destination | Shortcut |

After any drag-out (move or copy by external app), tfx refreshes both panes automatically. Name conflicts are resolved by appending `(2)`, `(3)`, and so on. Dragging an item back into the same folder with Move does nothing instead of creating a duplicate name.

---

## Address bar

Each pane has its own address bar.

- **Breadcrumb mode** (default): clickable path segments separated by ` > `. Click any segment to jump there.
- When a breadcrumb path is too long for the bar, the right end of the path remains visible and the left side is clipped.
- **Edit mode**: double-click the breadcrumb bar, click the empty area of the bar, or press `Ctrl + L` / `F4`. Type a path and press Enter to navigate. Environment variables such as `%USERPROFILE%` and `%TEMP%` are expanded. `Esc` cancels.

---

## View modes

- **Details** - DataGrid with sortable columns: Name, Date modified, Type, Size, Date created, Owner, Attribute. Inline rename (`F2`) is available here. The Size column is right-aligned, text columns keep a small right-side gap, and only the Name column can be resized directly with the mouse. Visible columns and order are configured from the **Columns** popup.
- **Icons** - WrapPanel of 32x32 shell icons with file names. Same selection / drag-drop / right-click behaviors as Details mode.

Toggle modes from the toolbar's view-mode button. The choice is remembered.

In Details mode, items are ordered as: parent (`..`) row first, then directories, then files. Sorting respects this priority.

---

## Columns popup

Open via the Columns button. The popup stays open while you toggle items.

- Check / uncheck a column to show / hide it
- Up / Down arrows reorder columns. Order applies to both panes
- At least one column must remain visible

You can also reorder columns by dragging their headers in the file list; the new order is mirrored to the other pane and saved.

---

## Pinned folders

Sidebar `PINNED` section.

- **Pin / unpin current folder** adds or removes the current folder of the active pane
- **Right-click > Unpin** removes a pinned entry
- Drag a pinned entry to reorder
- Click an entry to navigate the active pane
- Long paths are shown with a middle ellipsis, such as `C: ... \Downloads`, while the full path remains available as a tooltip.

Default pins on first run: User profile, Desktop, Documents, Downloads.

---

## Zip archives

- **Compress to Zip** creates `<selected-name>.zip` for one item, or `Archive.zip` for multiple items. Name conflicts are resolved with `(2)`, `(3)`, and so on.
- **Extract Zip** extracts each selected `.zip` into a same-named folder in the current pane.
- **Browse inside zip**: open a `.zip` with Enter or double-click to navigate into it like a folder. Subfolders inside the archive are also navigable, the breadcrumb bar shows each zip level as a clickable segment, and the parent (`..`) row at the zip root returns to the filesystem.
- **Open / drag out** of zip entries: opening a file inside the archive extracts it on demand to `%TEMP%\tfx\archive-<id>\…` and launches it with the system default app. Dragging entries out to Explorer or another app sets a `FileDrop` payload with the extracted temp paths (Copy effect only).
- The temp extraction folder is created lazily on first use and removed when the window is closed.
- **Zip-internal editing is not supported.** While the current path is inside a zip, the destructive context-menu items and shortcuts (Cut / Paste / Rename / Recycle Bin / Delete / Compress / Extract / New File / New Folder / Open Terminal here) are disabled, drops into the pane are rejected, and pinning the current archive folder is a no-op.

---

## Status bar

| Position | Content |
| --- | --- |
| Left | `<path>  N items` (or `<path>  K of N selected (combined size)` when something is selected) |
| Right | `<drive>  <free> free of <total>` for the active pane's drive, followed by the current version |

The parent (`..`) row is excluded from counts and size sums.

---

## Command-line options

```
tfx [options] [folder]
```

| Option | Long form | Effect |
| --- | --- | --- |
| `-h` | `--help` | Show help and exit |
| `-1` | `--single` | Start in single-pane layout |
| `-2` | `--split` | Start in split (two-pane) layout |
| `-r` | `--restore` | Restore the saved layout |
| `-p` | `--preview` | Show the preview pane |
| `-P` | `--no-preview` | Hide the preview pane |
| `-t` | `--terminal` | Show the built-in terminal |
| `-T` | `--no-terminal` | Hide the built-in terminal |
| `-g G` | `--geometry=G` | Window geometry `[WxH][+X+Y]` (DIPs; `-X`/`-Y` = from right/bottom), e.g. `1200x800+100+50` |

`[folder]` opens that folder in the left pane (supports `~` and `%VARS%`). Short flags can be combined, e.g. `-2Pt`. Command-line options take precedence over `config.toml [startup]` and the saved session state. A geometry also forces a normal (non-maximized) window.

Example — split layout, preview hidden, terminal shown, `~/Downloads` in the left pane:

```
tfx -2 -P -t ~/Downloads
```

---

## Configuration

On startup, tfx creates `%APPDATA%\tfx\config.toml` when it does not already exist. This file uses the same `version = 1` and section names as the macOS tfx configuration guide where the setting makes sense on Windows:

See [docs/configuration.md](docs/configuration.md) for the full file format, supported keys, and examples.

Shortcut values use Windows-native modifier names such as `ctrl`, `shift`, and `alt`. Supported key names include single letters / digits, `.`, `[`, `]`, `backslash`, arrow keys, `enter`, `tab`, `space`, `delete`, `backspace`, and `f1` through `f24`.

Supported sections are `[font]`, `[colors]`, `[opacity]`, `[shortcuts]`, `[startup]`, `[terminal]`, and `[openWith]`. `[font]` sets the UI and monospace fonts and base size, with optional per-pane overrides (`fileList`, `preview`, `terminal`, `folderTree`, each with a `*Size`) that fall back to `mono`. Color keys use the macOS tfx semantic names and are mapped onto WPF theme resources, including toolbar chrome, selection colors, inputs, scrollbars, and Markdown preview CSS. `[opacity].background` controls the transparent window surface. `[startup]` can force single/split layout, the visibility of the preview pane / terminal pane / folder tree, and explicit startup tabs through `leftFolders` / `rightFolders`. `[terminal] app` maps to the Windows executable or app alias, and `[terminal] arguments` is optional because tfx supplies cwd arguments for Windows Terminal, WezTerm, PowerShell, and pwsh. `[openWith]` maps an extension without the leading dot to an executable or app alias used when opening files of that type.

`config.toml` is intended for user-editable preferences. Session state is still saved automatically to `%APPDATA%\tfx\settings.json` on every change and on close.

## Settings

| Key | Description |
| --- | --- |
| `LeftPath`, `RightPath` | Current folder of each pane |
| `ActivePane` | Last active pane |
| `ShowSplit`, `ShowPreview`, `ShowHidden` | View toggles |
| `RenderMarkdownHtml` | Render Markdown / HTML / CSV / TSV / JSON previews (toggle in preview header) |
| `ShowPerformanceLogs` | Enable `PerformanceTrace` output to Debug / console. The `TFX_PERFORMANCE_LOGS=1` environment variable also turns this on and wins over the setting. |
| `Left`, `Top`, `Width`, `Height`, `IsMaximized` | Window placement |
| `SidebarWidth`, `PreviewWidth`, `LeftPaneRatio` | Pane layout |
| `TerminalCommand` | Executable to launch when invoking **Open Terminal here**. Empty (default) launches `wt.exe` at the active pane's folder and falls back to PowerShell if needed. |
| `TerminalArguments` | Argument template for the configured terminal command. Supports the `{path}` placeholder (replaced with the active pane's folder) and environment-variable expansion such as `%ProgramFiles%`. If omitted, tfx supplies cwd arguments for `wt.exe`, WezTerm, PowerShell, and pwsh. |
| `EnablePdfPreview` | Master toggle for the PDF preview pipeline. `false` skips all PDF rendering and shows only file metadata. Default `true`. |
| `PdfPreviewMaxBytes` | Skip rendering for PDFs larger than this. Default `524288000` (500 MB). Set to `0` for no limit. |
| `PdfRendererPath` | Absolute path to a user-pinned `pdftoppm.exe`. When set, takes priority over auto-detected installations. Empty (default) falls back to the auto-detection / built-in renderers described under [PDF preview](#pdf-preview). |
| `AllowShellPdfThumbnail` | Allow the Windows shell PDF thumbnail provider (Adobe / Foxit / Edge etc.) to run in-process as a last-resort renderer. Default `true`; set `false` for a stricter security posture. |
| `PinnedFolders` | Pinned entries in display order |
| `VisibleFileColumns` | Which columns are visible (`Name` / `Git` / `DateModified` / `Type` / `Size` / `DateCreated` / `Owner` / `Attribute`) |
| `FileColumnOrder` | Column display order |
| `ViewMode` | `Details` or `Icons` |

Delete the file to reset to defaults.

When hidden files are disabled, tfx hides both Windows Hidden-attribute entries and names that start with `.`, such as `.git` or `.env`.

---

## Build

Requires .NET 10 SDK and Windows 10 / 11.

```powershell
dotnet build
```

### Runtime prerequisite: Microsoft Edge WebView2 Runtime

The built-in terminal pane and the Markdown / HTML preview render with WebView2, so the **Microsoft Edge WebView2 Runtime** must be installed. It is preinstalled on Windows 11 and on up-to-date Windows 10, but may be missing on a clean Windows 10 image. If it is absent, opening the terminal pane shows a status-bar hint and the preview falls back to source view. Install the Evergreen runtime from <https://go.microsoft.com/fwlink/p/?LinkId=2124703> (or <https://developer.microsoft.com/microsoft-edge/webview2/>).

Run:

```powershell
dotnet run -- "C:\path\to\folder"
```

The initial folder for the left pane is resolved in this order: (1) the first command-line argument if it is a valid directory, (2) the current working directory at startup when it is meaningful (not the executable's own folder, `System32`, or `Windows`), so launching `Tfx.exe` from a terminal opens the terminal's current folder, and (3) the previously saved path. If none apply, the user profile folder is used.

Tab layout is session-local by default. On the next launch, tfx starts with one tab per visible pane using the restored pane paths; previously open tab lists are not restored. When `[startup] leftFolders` / `rightFolders` are set in `config.toml`, those configured lists are used as startup tabs instead.

Publish a self-contained single-file Windows executable:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Or build the release artifact:

```powershell
.\scripts\build-release.ps1
```

The script writes `artifacts\release\tfx-for-windows-<version>-win-x64\Tfx.exe`. By default the app is published as a self-contained single executable. Existing files are overwritten in place; the release folder itself is not deleted.

### Packaging

Both packaging scripts consume the already-built `Tfx.exe` above (run `build-release.ps1` first) and read the version from `Tfx.csproj`.

```powershell
# Portable ZIP -> artifacts\release\tfx-for-windows-<version>-win-x64-portable.zip
.\scripts\build-zip.ps1

# Installer -> artifacts\release\tfx-for-windows-<version>-setup.exe
.\scripts\build-installer.ps1
```

The ZIP contains a single top-level folder with `Tfx.exe`, `LICENSE`, `NOTICE`, and the READMEs. The installer is built with [Inno Setup 6](https://jrsoftware.org/isdl.php) (`winget install JRSoftware.InnoSetup`); if `ISCC.exe` is not on `PATH` or in the default location, pass `-IsccPath`. The installer adds Start Menu (and optional desktop) shortcuts and an uninstaller, and installs to `Program Files\tfx`.

---

## Project structure

```text
Tfx.csproj
App.xaml / App.xaml.cs              Application bootstrap; brushes; default control styles
AssemblyInfo.cs                     WPF theme info
scripts/build-release.ps1           Release publish helper
scripts/build-zip.ps1               Portable ZIP packager (from built binary)
scripts/build-installer.ps1         Installer builder (Inno Setup; from built binary)
scripts/tfx.iss                     Inno Setup installer definition

src/Views/MainWindow/MainWindow.xaml
                                    Main window layout
src/Views/MainWindow/MainWindow.xaml.cs
                                    Core: fields, ctor, settings load/save, status helpers
src/Views/MainWindow/*.cs           MainWindow partial files split by feature:
                                    tree, pinned folders, navigation, path bar, pane,
                                    search (recursive walk), columns, preview, file ops,
                                    drag and drop, external actions, context menu,
                                    view mode, keyboard routing, auto-refresh, git status.
                                    `MainWindow.Pane.cs` defines a `Pane` enum + helpers
                                    (`PaneOf`, `GridOf`, `IconViewOf`, `ItemsOf`, `PathOf`,
                                    `ActivePane`) used across the partials to avoid
                                    `LeftGrid` / `RightGrid` ternaries.

src/Controls/PathBar.xaml           Breadcrumb + edit-mode address bar UserControl
src/Controls/PathBar.xaml.cs
src/Controls/MiddleEllipsisTextBlock.cs
                                    Width-based middle-ellipsis TextBlock for pinned folder paths

src/Models/FileItem.cs              File / folder model (INPC-aware GitStatusText) +
                                    factory + formatters
src/Models/FileItemComparer.cs      Sort comparer (parent -> dirs -> files, then by column)
src/Models/AppSettings.cs           Persisted settings POCO + ViewMode enum

src/Services/FsHelpers.cs           File system helpers (enumerate, hidden, name conflict,
                                    image / text detect, .lnk creation via WScript.Shell)
src/Services/IconCache.cs           Shell icon retrieval (small + large), per-extension cache
src/Services/WindowTheme.cs         DWM title-bar color integration
src/Services/ShellOpenWith.cs       `SHOpenWithDialog` P/Invoke wrapper
src/Services/ShellThumbnail.cs      `IShellItemImageFactory` wrapper (cache-only and
                                    generate modes via `SIIGBF_INCACHEONLY`)
src/Services/PdfPreviewRenderer.cs  Multi-stage PDF preview: disk cache + shell cache
                                    + external pdftoppm (Job-Object isolated) +
                                    Windows.Data.Pdf + shell thumbnail provider
src/Services/WinRtPdfRenderer.cs    Windows.Data.Pdf wrapper used by the renderer's
                                    fallback stage
src/Services/JobObject.cs           Win32 Job Object wrapper for the external pdftoppm
src/Services/GitStatusReader.cs     `git status --porcelain=v2` runner; `FindRoot` walk
                                    for `.git` ancestor

Tfx.Core/Tfx.Core.csproj            Pure-logic library (net10.0, no WPF). Contains
                                    `ArchivePath`, `CsvParser`, `JsonPrettyPrinter`,
                                    `GitStatusParser`, `PerformanceTrace`.

Tfx.Tests/Tfx.Tests.csproj          xUnit tests for the pure-logic types
                                    (`ArchivePathTests`, `CsvParserTests`,
                                    `JsonPrettyPrinterTests`, `GitStatusParserTests`)
                                    plus informational `Benchmarks/PerformanceBenchmarks.cs`.
```

`MainWindow` is a single class split into partial files by feature. All fields are declared in `MainWindow.xaml.cs` to keep state in one place.

---

## PDF preview

tfx renders the first page of a PDF in the preview pane. Multiple renderer stages run in priority order; the first one that returns an image wins. The whole pipeline is skipped if `EnablePdfPreview` is `false` or the file is larger than `PdfPreviewMaxBytes`.

| Stage | Source | When it runs |
| --- | --- | --- |
| Disk cache | `%LocalAppData%\tfx\pdf-cache\<sha256>.png` (up to 200 entries, LRU) | Whenever a previously-rendered PDF is selected — survives app restarts |
| Shell cache (read-only) | Windows thumbnail cache that Explorer / other apps populated | Always |
| External `pdftoppm` | User-pinned path, then `%PATH%`, then well-known install locations | When a vendor-verified `pdftoppm.exe` is found |
| `Windows.Data.Pdf` | OS-shipped PDFium-derived renderer | When no external `pdftoppm` was found (fallback) |
| Shell thumbnail provider (in-process) | Adobe / Foxit / Edge etc. registered shell extension | Only when `AllowShellPdfThumbnail` is `true` (default) |

### External `pdftoppm` auto-detection

When `PdfRendererPath` is empty, tfx searches the following locations (in order) and uses the first executable whose `FileVersionInfo` metadata identifies it as Poppler / Xpdf / pdftoppm:

1. `%PATH%`
2. `%ProgramFiles%\Calibre2\app\bin\pdftoppm.exe`
3. `%ProgramFiles(x86)%\Calibre2\app\bin\pdftoppm.exe`
4. `%ProgramFiles%\poppler\bin\pdftoppm.exe` / `…\Library\bin\pdftoppm.exe`
5. `%LocalAppData%\Programs\poppler\bin\pdftoppm.exe` / `…\Library\bin\pdftoppm.exe`
6. `%UserProfile%\scoop\apps\poppler\current\bin\pdftoppm.exe` / `…\Library\bin\pdftoppm.exe`
7. `C:\ProgramData\chocolatey\bin\pdftoppm.exe`
8. `%UserProfile%\miniforge3\Library\bin\pdftoppm.exe`
9. `%UserProfile%\anaconda3\Library\bin\pdftoppm.exe`

Set `PdfRendererPath` in `settings.json` to an absolute path (with `\\` escapes) to override the auto-detection. The external renderer runs in a separate process with a [Job Object](https://learn.microsoft.com/windows/win32/procthread/job-objects) limit of 256 MB and an 8-second timeout, killed automatically when tfx exits.

### Built-in `Windows.Data.Pdf`

When no external `pdftoppm` is found, tfx falls back to the PDFium-derived renderer shipped with Windows 10/11 (1809 baseline). No extra install needed; works on OneDrive / Google Drive virtual files because the OS resolves the content on demand when tfx opens the file stream.

### Cloud-synced files

The shell thumbnail provider (Stage 5) returns `0x8004B2xx` errors for OneDrive / Google Drive virtual files. tfx surfaces a hint suggesting either marking the file "Always keep on this device" or installing an external `pdftoppm`. The external `pdftoppm` and `Windows.Data.Pdf` stages both work on cloud-synced files without that workaround.

---

## Notes and limitations

- Delete-like operations move to the Recycle Bin by default. Use `Shift + Delete` for permanent removal.
- Cross-volume directory moves are handled via `Microsoft.VisualBasic.FileIO.FileSystem.MoveDirectory`, which falls back to copy + delete.
- PDF preview pipeline is documented under [PDF preview](#pdf-preview). Short version: cache → external `pdftoppm` (auto-detected) → built-in `Windows.Data.Pdf` → shell thumbnail provider. Each layer can be disabled individually via settings.
- Text preview detects UTF-8 with/without BOM, UTF-16 with BOM, EUC-JP, ISO-2022-JP (JIS), and Shift_JIS, and shows the detected encoding and newline style.
- Markdown (`.md`) and HTML (`.html`, `.htm`) previews render via the embedded WebView2 control; a toggle button in the preview header switches between rendered view and source. Requires the Microsoft Edge WebView2 runtime (preinstalled on Windows 11). The preview WebView2 has JavaScript disabled (`IsScriptEnabled=false`), Markdown is rendered with `Markdig.DisableHtml()` to strip inline `<script>` / `javascript:`, and rendered output is wrapped in a strict CSP (`default-src 'none'; img-src data:; style-src 'unsafe-inline'`) so a hostile `.md` / `.html` cannot exfiltrate files or perform network requests.
- Video previews are not implemented.
- Inline rename (`F2`) is wired to the active Details view.
- The DataGrid header drag-reorder is disabled by default style; use the Columns popup to keep both panes in sync.
- Network locations work if mounted as drives or by typing UNC paths in the address bar.
- Zip browsing is read-only. Entries previewed or opened are extracted on demand to `%TEMP%\tfx\archive-<id>\…` and the folder is cleaned up when the window closes. Nested zips are not auto-mounted; opening a `.zip` inside an archive extracts it first.
- The configured terminal command is started via `ShellExecute`, so executables on `PATH`, registered app aliases (e.g. `wt`, `pwsh`, `code`), and `.cmd` / `.bat` / `.lnk` targets all work. If the configured command fails to start, the launcher silently falls back to `powershell.exe` so the user is never stuck with a broken configuration.
- **"Sync file list to terminal folder" — cwd detection:** tfx resolves the terminal's current directory without printing anything to the prompt. For its built-in **PowerShell** it automatically injects an invisible `OSC 9;9` cwd reporter at startup (passed as `-EncodedCommand`, run after your profile so it wraps oh-my-posh / Starship / a custom prompt, and only for real filesystem locations); tfx strips that sequence from the display, so nothing is shown. It also honours `OSC 7` / `OSC 9;9` emitted by any shell integration (Windows Terminal, oh-my-posh, Starship, …). Under wtmux it queries wtmux out-of-band (see below). Only when none of these is available — e.g. `cmd` or a custom shell that emits no cwd sequence — does it fall back to a visible `[tfx:cwd]…[tfx:end]` command. tfx navigates only when the resolved path is an existing directory.
- **"Sync file list to terminal folder" under wtmux:** when running [wtmux](https://github.com/fukuyori/wtmux) in the built-in terminal, tfx reads the current path from wtmux (`display-message -p '#{pane_current_path}'`). For wtmux to know the path, enable its prompt hook — set `cwd_prompt_hook = true` in wtmux's `config.toml`, or run `wtmux -P on` at startup. Without the hook wtmux reports its startup directory, so the sync lands on the wrong folder. The prompt hook wraps the shell prompt and emits an `OSC 9;9` cwd notification on each prompt; it applies to PowerShell / cmd but not custom shells (e.g. `wsl.exe`), and it can interact with tools that replace the prompt or with raw-escape logging — see the wtmux docs for details. tfx itself only navigates when the reported path is an existing directory, so a non-filesystem or stale path is ignored rather than causing a wrong jump.
- Git integration requires the `git` executable on `PATH`. Without it, the **Git** column stays empty and the branch label in the status bar is hidden. Runs `git status --porcelain=v2 --branch --untracked-files=normal --no-renames` with an 8-second timeout; the parser handles ordinary changes (X / Y status), untracked (`?`), conflicted (`u`), and ignored (`!`) entries, plus type-2 rename / copy records.
- PDF previews are cached on disk under `%LocalAppData%\tfx\pdf-cache\` (LRU, 200 entries). Re-selecting a previously-rendered PDF is essentially free even after restarting tfx; an external edit invalidates the cached entry automatically because the cache key includes the file's last-write time and length. The disk cache can be cleared by deleting that folder.

---

## License

Copyright 2026 fukuyori

Licensed under the Apache License, Version 2.0 (the "License"); you may not use this software except in compliance with the License. You may obtain a copy of the License at

<http://www.apache.org/licenses/LICENSE-2.0>

Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the [LICENSE](LICENSE) and [NOTICE](NOTICE) files for the full text.
