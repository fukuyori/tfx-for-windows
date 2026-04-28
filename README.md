# tfx for Windows

**Terminal-inspired interface File eXplorer**
Pronunciation: **Tafix**
Version: 0.01

[![License: Apache 2.0](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](LICENSE)

A keyboard-friendly, dark-themed file explorer for Windows. C# / WPF port of the macOS edition at <https://github.com/fukuyori/tfx>.

- Repository: <https://github.com/fukuyori/tfx-for-windows>
- Author: fukuyori (<self@spumoni.org>)

---

## Highlights

- Two file panes (single or split) with independent navigation history
- Folder tree sidebar plus persistent pinned folders
- Editable address bar with clickable breadcrumb segments and free-text input
- Two view modes: **Details** (multi-column metadata) and **Icons** (large-icon grid)
- Inline rename, drag-and-drop with full Windows modifier-key conventions, shortcut (`.lnk`) creation
- Right-click context menu, sortable columns, customizable column visibility and order
- Image / text preview pane
- Status bar with item counts, selection size, and active drive's free space
- All view state, paths, pinned folders, column layout, and view mode are persisted

---

## UI overview

```text
+-- Toolbar -----------------------------------------------------------+
| tfx 0.01  Back Forward Up Reload | Split Preview View Cols  [Search]|
|                          | New Rename Trash | Term Expl | Hide      |
+--------------+----------------+----------------+--------------------+
| PINNED       | Address bar    | Address bar    | PREVIEW            |
| folder list  | Left file view | Right file view| info / image / text|
| Pin+ Pin-    |                |                |                    |
| FOLDERS tree |                |                |                    |
+--------------+----------------+----------------+--------------------+
| <path>  K of N selected (size)         C:\  120 GB free of 476 GB  |
+---------------------------------------------------------------------+
```

The active file pane is outlined with a bright green border.

---

## Toolbar

Buttons use the **Segoe MDL2 Assets** font (built into Windows 10+). Hover for tooltip + shortcut.

| Group | Buttons |
| --- | --- |
| Navigation | Back, Forward, Up, Reload |
| View | Split toggle, Preview toggle, View mode (Details / Icons), Columns |
| File ops | New folder, Rename, Move to Recycle Bin |
| External | Open Terminal here, Reveal in Explorer |
| Misc | Toggle hidden files |

The search box sits between the left and right groups in the toolbar.

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
| `Ctrl + R` | Reload |
| `Ctrl + F` | Focus Search |
| `Ctrl + L` / `F4` | Focus address bar (edit path) |
| `Tab` / `Shift + Tab` | Cycle focus: FolderTree -> Left pane -> Right pane |
| `Left` / `Right` | Move focus between file panes |
| `Up` / `Down` | Move selection in active view |
| `Ctrl + Shift + T` | Open Terminal in current folder |
| `Ctrl + Shift + .` | Toggle hidden files |
| `Esc` | Cancel rename / clear search / exit address-bar edit |

---

## Mouse

- **Single click** on tree node -> navigate active pane to that folder
- **Click** on the expander triangle -> expand/collapse only (no navigate)
- **Single click** on row / icon -> select; **Ctrl** / **Shift** for multi-select
- **Double-click** on row / icon -> open
- **Right-click** -> context menu (Open, Reveal, Cut, Copy, Paste, Rename, Trash, Delete permanently, New folder, Open Terminal)
- **Click** on a breadcrumb segment -> jump to that ancestor
- **Click** on the empty area of the address bar -> switch to free-text edit mode
- **Click** column header -> sort by that column (toggle ascending / descending)

---

## Drag and drop

Works within tfx, between tfx and Windows Explorer, or any other app that exchanges `FileDrop` data.

| Modifier | Effect | Cursor |
| --- | --- | --- |
| (none) | Move within same drive, otherwise Copy | Move / Copy |
| `Shift` | Force Move | Move |
| `Ctrl` | Force Copy | Copy |
| `Alt` | Create shortcut (`.lnk`) at destination | Shortcut |

After any drag-out (move or copy by external app), tfx refreshes both panes automatically. Name conflicts on copy / move are resolved by appending `(2)`, `(3)`, and so on.

---

## Address bar

Each pane has its own.

- **Breadcrumb mode** (default): clickable path segments separated by ` > `. Click any segment to jump there.
- **Edit mode**: click the empty area of the bar (or press `Ctrl + L` / `F4`). Type a path and press Enter to navigate. Environment variables such as `%USERPROFILE%` and `%TEMP%` are expanded. `Esc` cancels.

---

## View modes

- **Details** - DataGrid with sortable columns: Name, Date modified, Type, Size, Date created, Owner, Attribute. Inline rename (`F2`) is available here. Visible columns and order are configured from the **Columns** popup.
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

- **Pin +** adds the current folder of the active pane
- **Pin -** removes the selected entry
- Drag a pinned entry to reorder
- Double-click an entry to navigate the active pane

Default pins on first run: User profile, Desktop, Documents, Downloads.

---

## Status bar

| Position | Content |
| --- | --- |
| Left | `<path>  N items` (or `<path>  K of N selected (combined size)` when something is selected) |
| Right | `<drive>  <free> free of <total>` for the active pane's drive |

The parent (`..`) row is excluded from counts and size sums.

---

## Settings

Saved automatically to `%APPDATA%\tfx\settings.json` on every change and on close.

| Key | Description |
| --- | --- |
| `LeftPath`, `RightPath` | Current folder of each pane |
| `ShowSplit`, `ShowPreview`, `ShowHidden` | View toggles |
| `Width`, `Height` | Window size |
| `PinnedFolders` | Pinned entries in display order |
| `VisibleFileColumns` | Which columns are visible |
| `FileColumnOrder` | Column display order |
| `ViewMode` | `Details` or `Icons` |

Delete the file to reset to defaults.

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

The first command-line argument, if it is a valid directory, becomes the initial folder for the left pane. Otherwise the previously saved path is restored.

Publish a self-contained Windows executable:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true
```

---

## Project structure

```text
Tfx.csproj
App.xaml / App.xaml.cs              Application bootstrap; brushes; default control styles
AssemblyInfo.cs                     WPF theme info

PathBar.xaml / PathBar.xaml.cs      Breadcrumb + edit-mode address bar UserControl

MainWindow.xaml                     Main window layout
MainWindow.xaml.cs                  Core: fields, ctor, settings load/save, status helpers
MainWindow.Tree.cs                  Folder tree (drives, lazy expand, click-to-navigate)
MainWindow.Pinned.cs                Pinned folders (load, drag-reorder, click)
MainWindow.Navigation.cs            Navigate / Reload / Back / Forward / Parent
MainWindow.PathBar.cs               Path bar wiring; environment expansion; window title
MainWindow.Pane.cs                  Active pane border; split toggle; selection events
MainWindow.Search.cs                CollectionView search filter
MainWindow.Columns.cs               Columns popup, ordering, visibility, sorting
MainWindow.Preview.cs               Preview rendering; preview toggle
MainWindow.FileOps.cs               Copy / Cut / Paste / Trash / Permanent delete /
                                    Rename / New folder / Open / Drag / Drop
MainWindow.External.cs              Terminal, Explorer, Hidden toggle
MainWindow.ContextMenu.cs           Right-click context menu
MainWindow.View.cs                  Details / Icons toggle and ListBox handlers
MainWindow.Keyboard.cs              KeyDown / PreviewKeyDown; Tab focus cycle

FileItem.cs                         File / folder model + factory + formatters
FileItemComparer.cs                 Sort comparer (parent -> dirs -> files, then by column)
AppSettings.cs                      Persisted settings POCO + ViewMode enum
FsHelpers.cs                        File system helpers (enumerate, hidden, name conflict,
                                    image / text detect, .lnk creation via WScript.Shell)
IconCache.cs                        Shell icon retrieval (small + large), per-extension cache
```

`MainWindow` is a single class split into partial files by feature. All fields are declared in `MainWindow.xaml.cs` to keep state in one place.

---

## Notes and limitations

- Delete-like operations move to the Recycle Bin by default. Use `Shift + Delete` for permanent removal.
- Cross-volume directory moves are handled via `Microsoft.VisualBasic.FileIO.FileSystem.MoveDirectory`, which falls back to copy + delete.
- PDF and video previews are not implemented.
- Inline rename (`F2`) is wired only in Details view; from Icons mode use the toolbar Rename button after selecting an item, then F2 still triggers a rename in the underlying DataGrid.
- The DataGrid header drag-reorder is disabled by default style; use the Columns popup to keep both panes in sync.
- Network locations work if mounted as drives or by typing UNC paths in the address bar.

---

## License

Copyright 2026 fukuyori

Licensed under the Apache License, Version 2.0 (the "License"); you may not use this software except in compliance with the License. You may obtain a copy of the License at

<http://www.apache.org/licenses/LICENSE-2.0>

Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the [LICENSE](LICENSE) and [NOTICE](NOTICE) files for the full text.
