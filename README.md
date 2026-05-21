# tfx for Windows

**Terminal-inspired interface File eXplorer**
Pronunciation: **Tafix**
Version: 0.5.1

[![License: Apache 2.0](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](LICENSE)

A keyboard-friendly, dark-themed file explorer for Windows. C# / WPF port of the macOS edition at <https://github.com/fukuyori/tfx>.

- Repository: <https://github.com/fukuyori/tfx-for-windows>
- Author: fukuyori (<self@spumoni.org>)
- Release notes: [CHANGELOG.md](CHANGELOG.md)
- Development roadmap: [docs/roadmap.md](docs/roadmap.md)
- Contributing guide: [docs/contributing.md](docs/contributing.md)

---

## Highlights

- Two file panes (single or split) with independent navigation history
- Folder tree sidebar plus persistent pinned folders
- Editable address bar with clickable breadcrumb segments and free-text input
- Two view modes: **Details** (multi-column metadata) and **Icons** (large-icon grid)
- New File / New Folder, inline rename, drag-and-drop with full Windows modifier-key conventions, shortcut (`.lnk`) creation
- Zip compression and extraction from the file pane or context menu, plus read-only browsing inside `.zip` files (open, drag out to other apps)
- Right-click context menu (Windows 11–style ordering), "Open with..." dialog, sortable columns, customizable column visibility and order
- Image / text preview pane with rendered Markdown, HTML, CSV / TSV tables, and pretty-printed JSON (toggle between rendered view and source)
- Multi-selection preview: when more than one item is selected, the preview pane shows a compact summary (count, combined size, per-item name / kind / size / modified) up to a cap of 8 entries
- Recursive subfolder search: type a query in the search box and press **Enter** to walk the current folder's subtree on a background thread, streaming matches into the active pane with live status-bar progress; **Esc** cancels and restores the real listing
- Git working-copy integration: when inside a Git repository, file rows show a one-character status badge (M / A / ? / D / R / C / U) in the **Git** column and the current branch appears in the status bar as `⎇ name`
- USB / removable drive hot detection: the folder-tree drive list refreshes when devices are added or removed (via `WM_DEVICECHANGE`)
- Status bar with item counts, selection size, active drive's free space (cached and refreshed in the background to stay responsive on slow network drives), Git branch, and the current version
- Japanese / English UI based on the OS UI language
- All view state, paths, pinned folders, column layout, and view mode are persisted
- File panes auto-refresh on external changes via `FileSystemWatcher` (with a low-frequency periodic fallback) and apply a diff so scroll position and selection are preserved

---

## UI overview

```text
+-- Toolbar -----------------------------------------------------------+
| Back Forward Up Folder Pin | <active path> [Search] | View Hidden   |
| Terminal Explorer Select Reload Preview Split Columns                |
+--------------+----------------+----------------+--------------------+
| PINNED       | Address bar    | Address bar    | PREVIEW            |
| folder list  | Left file view | Right file view| info / image / text|
| pinned paths |                |                |                    |
| FOLDERS tree |                |                |                    |
+--------------+----------------+----------------+--------------------+
| <path>  K of N selected (size)   C:\  120 GB free of 476 GB  0.5.1 |
+---------------------------------------------------------------------+
```

The active file pane is outlined with a bright green border.

---

## Toolbar

Buttons use **Segoe Fluent Icons** with **Segoe MDL2 Assets** fallback. Hover for tooltip + shortcut.

| Group | Buttons |
| --- | --- |
| Navigation | Back, Forward, Up, Open folder, Pin / unpin current folder |
| Path / search | Active path, Search, Focus search |
| View | View mode (Details / Icons), Toggle hidden files |
| Utility | Open Terminal here, Reveal in Explorer, Select all, Reload, Preview toggle, Split toggle, Swap panes, Columns |
| Status bar | Item count / selection size, current Git branch (if inside a working copy), free space on the active drive, version |

File operations such as New Folder, New File, Rename, Zip, Copy, Cut, and Paste are available from the keyboard and context menu.

---

## Keyboard

| Key | Action |
| --- | --- |
| `Enter` | Open selected file / Enter selected folder |
| `Backspace` | Parent folder |
| `Ctrl + [` / `Ctrl + ]` | Back / Forward |
| `Ctrl + Up` | Parent folder |
| `F2` | Inline rename (Details mode) |
| `Delete` | Move selection to Recycle Bin |
| `Shift + Delete` | Permanently delete selection |
| `Ctrl + C` / `Ctrl + X` / `Ctrl + V` | Copy / Cut / Paste |
| `Ctrl + A` | Select all in active view |
| `Ctrl + N` | New folder |
| `Ctrl + Shift + N` | New file |
| `Ctrl + K` | Compress selected items to a zip archive |
| `Ctrl + Shift + E` | Extract selected zip archive(s) |
| `Ctrl + R` | Reload |
| `Ctrl + F` | Focus Search (subfolder search — type and press Enter) |
| `Ctrl + L` / `F4` | Focus address bar (edit path) |
| `Tab` / `Shift + Tab` | Switch focus to the other file pane (split view only; only when focus is already in a pane) |
| `Left` / `Right` | Move focus between file panes (only when focus is already in a pane) |
| `Up` / `Down` | Move selection in active view |
| `Ctrl + Shift + T` | Open Terminal in current folder |
| `Ctrl + Shift + .` | Toggle hidden files |
| `Ctrl + \` | Toggle split pane |
| `Ctrl + Shift + P` | Toggle preview pane |
| `Ctrl + Shift + X` | Swap left and right panes (split view only) |
| `Esc` | Cancel rename / clear search / exit address-bar edit |

---

## Mouse

- **Single click** on tree node -> navigate active pane to that folder
- **Click** on the expander triangle -> expand/collapse only (no navigate)
- **Single click** on row / icon -> select; **Ctrl** / **Shift** for multi-select
- **Double-click** on row / icon -> open
- After entering a folder, the parent row (`..`) is selected and focused. When returning to the parent with `..` or Backspace, the folder you came from is selected and focused.
- **Right-click** -> context menu (Open, Open with..., Reveal, Pin / Unpin, Cut, Copy, Paste, Copy current path, Compress, Extract, New folder, New file, Open Terminal, Rename, Trash, Delete permanently for the selected item)
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

Each pane has its own.

- **Breadcrumb mode** (default): clickable path segments separated by ` > `. Click any segment to jump there.
- When a breadcrumb path is too long for the bar, the right end of the path remains visible and the left side is clipped.
- **Edit mode**: click the active path in the top bar, double-click the breadcrumb bar, click the empty area of the bar, or press `Ctrl + L` / `F4`. Type a path and press Enter to navigate. Environment variables such as `%USERPROFILE%` and `%TEMP%` are expanded. `Esc` cancels.

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

## Settings

Saved automatically to `%APPDATA%\tfx\settings.json` on every change and on close.

| Key | Description |
| --- | --- |
| `LeftPath`, `RightPath` | Current folder of each pane |
| `ActivePane` | Last active pane |
| `ShowSplit`, `ShowPreview`, `ShowHidden` | View toggles |
| `RenderMarkdownHtml` | Render Markdown / HTML / CSV / TSV / JSON previews (toggle in preview header) |
| `ShowPerformanceLogs` | Enable `PerformanceTrace` output to Debug / console. The `TFX_PERFORMANCE_LOGS=1` environment variable also turns this on and wins over the setting. |
| `Left`, `Top`, `Width`, `Height`, `IsMaximized` | Window placement |
| `SidebarWidth`, `PreviewWidth`, `LeftPaneRatio` | Pane layout |
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

Run:

```powershell
dotnet run -- "C:\path\to\folder"
```

The initial folder for the left pane is resolved in this order: (1) the first command-line argument if it is a valid directory, (2) the current working directory at startup when it is meaningful (not the executable's own folder, `System32`, or `Windows`), so launching `Tfx.exe` from a terminal opens the terminal's current folder, and (3) the previously saved path. If none apply, the user profile folder is used.

Publish a self-contained single-file Windows executable:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Or build the release artifact:

```powershell
.\scripts\build-release.ps1
```

The script writes `artifacts\release\tfx-for-windows-<version>-win-x64\Tfx.exe`. By default the app is published as a self-contained single executable. Existing files are overwritten in place; the release folder itself is not deleted.

---

## Project structure

```text
Tfx.csproj
App.xaml / App.xaml.cs              Application bootstrap; brushes; default control styles
AssemblyInfo.cs                     WPF theme info
scripts/build-release.ps1           Release publish helper

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
src/Services/PdfPreviewRenderer.cs  Shell-thumbnail + pdftoppm + LRU-cache PDF preview
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

## Notes and limitations

- Delete-like operations move to the Recycle Bin by default. Use `Shift + Delete` for permanent removal.
- Cross-volume directory moves are handled via `Microsoft.VisualBasic.FileIO.FileSystem.MoveDirectory`, which falls back to copy + delete.
- PDF preview renders the first page with `pdftoppm` when available, then falls back to the Windows shell thumbnail provider. If neither is available, tfx shows file information only.
- Text preview detects UTF-8 with/without BOM, UTF-16 with BOM, EUC-JP, ISO-2022-JP (JIS), and Shift_JIS, and shows the detected encoding and newline style.
- Markdown (`.md`) and HTML (`.html`, `.htm`) previews render via the embedded WebView2 control; a toggle button in the preview header switches between rendered view and source. Requires the Microsoft Edge WebView2 runtime (preinstalled on Windows 11).
- Video previews are not implemented.
- Inline rename (`F2`) is wired to the active Details view.
- The DataGrid header drag-reorder is disabled by default style; use the Columns popup to keep both panes in sync.
- Network locations work if mounted as drives or by typing UNC paths in the address bar.
- Zip browsing is read-only. Entries previewed or opened are extracted on demand to `%TEMP%\tfx\archive-<id>\…` and the folder is cleaned up when the window closes. Nested zips are not auto-mounted; opening a `.zip` inside an archive extracts it first.
- Git integration requires the `git` executable on `PATH`. Without it, the **Git** column stays empty and the branch label in the status bar is hidden. Runs `git status --porcelain=v2 --branch --untracked-files=normal --no-renames` with an 8-second timeout; the parser handles ordinary changes (X / Y status), untracked (`?`), conflicted (`u`), and ignored (`!`) entries, plus type-2 rename / copy records.
- PDF previews go through three stages: cached Windows shell thumbnail (instant, never blocks) → `pdftoppm` if installed → shell-generated thumbnail (may block briefly). Results are stored in an in-process LRU cache keyed by path + last-write time + length + render size, so re-selecting the same PDF is instant.

---

## License

Copyright 2026 fukuyori

Licensed under the Apache License, Version 2.0 (the "License"); you may not use this software except in compliance with the License. You may obtain a copy of the License at

<http://www.apache.org/licenses/LICENSE-2.0>

Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the [LICENSE](LICENSE) and [NOTICE](NOTICE) files for the full text.
