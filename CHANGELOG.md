# Changelog

## 0.4.2

- Add Swap-panes toolbar button and `Ctrl+Shift+S` shortcut. In split view, swaps the left / right pane paths and moves the active pane to follow the previously-active folder.
- Add `Ctrl+\` shortcut for split-pane toggle and `Ctrl+Shift+P` for preview-pane toggle. Tooltips and the README key table updated.
- When the user toggles split view back on, reset the pane ratio to 50 / 50 so the single-pane area divides evenly. The user can still drag the splitter afterward; that ratio is saved.
- Rework Tab / arrow key focus behaviour to match the documented spec: Tab swaps between the two file panes only when focus is already in a pane and split view is on; folder tree, toolbar, and search box fall through to default focus traversal. Left / Right move focus between panes only when focus is already in a pane.
- Always intercept Up / Down / PageUp / PageDown while focus is anywhere inside the active pane (container, row, or cell). The previous "only when SelectedItem is null" guard raced with focus settling after a Tab switch.
- Auto-select the first item and use the queued listing-focus helper when switching panes via Tab or Left / Right, so arrow keys land on the row container reliably.
- File-move audit: paste now uses `VbFileSystem.MoveDirectory` (handles cross-volume), wraps the paste loop in try/catch so one failure no longer aborts the rest, and post-verifies that move sources are gone. Drag-drop reports left-behind sources and per-file failures with names. New localized status messages.
- Network-drive responsiveness: drop `File.Exists` from `UpdatePreview` and the rendered-toggle visibility check, drop `Directory.Exists` validation from `LoadPinned`, move `Directory.Exists` into the background task in `ReloadDiffCoreAsync`, and run `Directory.Exists` + `new FileSystemWatcher` on a background thread. Skip FSW entirely for UNC paths (the periodic poll covers them).
- WebView2 on shutdown: replace synchronous `Dispose()` with `CoreWebView2.Stop()` to avoid an occasional "not responding" stall when the window closes mid-navigation.
- Move `Markdown.ToHtml` rendering to a background `Task.Run`, skip the WebView2 about:blank refresh when the WebView was already hidden, and cache `DriveInfo` free-space lookups for 5 s so a slow network share no longer blocks the status bar.
- Status-bar performance: status text and free-space refresh decoupled — `GetFreeSpaceText` returns the cached value immediately and triggers a background refresh that swaps the text in when ready.

## 0.4.1

- Add a `Tfx.Tests` xUnit project and a new `Tfx.Core` library that holds the pure-logic types (`ArchivePath`, `CsvParser`, `JsonPrettyPrinter`) so they can be tested without WPF. CI runs `dotnet restore` / `build` / `test` on `windows-latest` via `.github/workflows/build.yml`.
- Render CSV / TSV files as a `DataGrid` table preview, and JSON files as pretty-printed monospaced text, both via the existing Source / Rendered toggle. CSV parsing and JSON pretty-printing run on a background thread with a 2000-row / 64-column cap to keep the UI responsive on large files.
- Extend `FsHelpers.IsText` to cover `.toml` / `.yaml` / `.yml` / `.ini` / `.cfg` / `.conf` / `.env` and several developer-text extensions so they no longer fall through to "no preview".
- Skip the periodic auto-refresh poll while the window is deactivated or minimized, and skip it entirely for panes whose `FileSystemWatcher` is active (FSW already covers external changes). Periodic interval extended from 10 s to 30 s.
- Add a per-pane in-flight guard so concurrent `FileSystemWatcher` events cannot spawn overlapping refresh tasks.
- Default `IsReadOnly="True"` on both file `DataGrid`s so accidental clicks no longer flip the grid into edit mode and so the rename-detection check in `IsBusyForRefresh` works correctly.
- Cancel preview / reload `CancellationTokenSource`s and stop `FileSystemWatcher` raising before disposal during `Window_Closing`, then fire-and-forget the archive temp folder cleanup. Skip the synchronous WebView2 `Dispose()` (which occasionally blocked on close); call `CoreWebView2.Stop()` instead and let the OS reclaim `msedgewebview2.exe`.
- New `Tfx.Core/Tfx.Core.csproj` + `Tfx.Tests/Tfx.Tests.csproj` are wired into `tfx.sln`. `Tfx.csproj` now references `Tfx.Core` and excludes `Tfx.Core\**` / `Tfx.Tests\**` / `artifacts\**` from default item globbing.
- Add `docs/contributing.md` covering prerequisites, build/test commands, the repo layout, the CI workflow, code style, and the (placeholder) release process.

## 0.4.0

- Split drag-and-drop logic (~310 lines) out of `MainWindow.FileOps.cs` into a new `MainWindow.DragDrop.cs` partial and remove unused click-handler stubs; `MainWindow.FileOps.cs` drops from 852 lines to 535.
- Introduce a `Pane` enum and helpers (`PaneOf`, `GridOf`, `IconViewOf`, `ItemsOf`, `PathOf`, `ActivePane`) in `MainWindow.Pane.cs` and refactor Reload / AutoRefresh / Navigation / Keyboard / External / RubberBand / status updates to use them, eliminating the `isLeft` booleans and most `LeftGrid / RightGrid` ternaries.

## 0.3.2

- Debounce preview updates (~120ms) so rapid arrow-key navigation no longer spawns a preview task per row.
- Batch the initial directory load (200 items per batch with a `Dispatcher.Yield`) so opening large folders no longer freezes the UI thread.
- Shorten the auto-refresh debounce from 250ms to 150ms for a snappier reflection of external file changes.
- Debounce the search box (~150ms) so each keystroke no longer triggers a `CollectionView` refresh on both panes. Esc / Enter still apply / clear the filter immediately.
- Enumerate the folder-tree drives on a background thread (with `AsParallel` per-drive subdirectory probing), so a slow or unresponsive network drive no longer blocks startup or the "Toggle hidden files" action.

## 0.3.1

- Auto-refresh the file panes: each pane subscribes to a `FileSystemWatcher` on its current folder so external changes show up almost immediately. A 10-second fallback poll runs alongside to catch missed events. Auto-refresh is skipped while renaming, drag-in-progress, rubber-band selecting, the context menu is open, or the current path is inside a zip. Reloads use a diff (add / remove / move) against the existing list so scroll position and selection are preserved.
- Add "Open with..." to the file-grid context menu. Invokes the standard Windows "Open with" dialog (`SHOpenWithDialog`) for the selected file.
- Reorder the file-grid context menu closer to Windows 11 conventions: open / locate / pin, clipboard + copy path, archive (compress / extract), current folder actions (new folder / file / terminal), then destructive operations (rename / recycle bin / delete) at the bottom.
- Resolve the initial folder from the current working directory at launch when it is meaningful (ignored when launched from the executable's own directory, `System32`, or `Windows`), so launching `Tfx.exe` from a terminal opens the terminal's current folder.
- Browse inside `.zip` files like folders. Open a zip with Enter or double-click to navigate into it; subfolders inside the archive are navigable; files inside open with the system default app via on-demand extraction to a temp folder; entries can be dragged out to Explorer or other apps. Zip-internal editing (rename, delete, paste, new file/folder) is not supported and is disabled when the current path is inside an archive. The breadcrumb bar shows zip levels as clickable segments and the temp extraction folder is cleaned up on close.

## 0.3.0

- Add Pin / Unpin to the file-grid context menu for a selected folder.
- Auto-size the pinned-folder list to its content and remove its scrollbars, with extra bottom space reserved as a drop zone for new pins.
- Drag a folder from the file list, folder tree, or Explorer onto the pinned list to pin it at the dropped position; existing pins reorder on drop.
- Switch pinned folder name truncation to width-based middle ellipsis that follows sidebar resizing.
- Wrap long lines in the text preview so content no longer gets clipped on the right.
- Render Markdown (`.md`) and HTML (`.html`, `.htm`) previews via WebView2, with a toggle in the preview header to switch between rendered view and source.

## 0.2.2

- Show the current version at the right edge of the status bar.
- Right-align Size values in Details view and add right-side spacing between list columns.
- Allow direct mouse resizing for the Name column while keeping the other Details columns fixed.
- Keep DataGrid header mouse handling from interfering with Name-column resizing.
- Keep the right end of long breadcrumb paths visible when the path bar is narrower than the full path.
- Update the release build script to overwrite files in place without deleting the release folder first.

## 0.2.1

- Apply the hidden-folder setting to the folder tree, including Hidden-attribute folders and dot-prefixed folders.

## 0.2.0

- Focus the selected file-name cell after folder navigation so arrow keys continue moving the file-list selection.
- Select `..` after entering a folder, and select the folder you came from after returning to the parent with `..` or Backspace.
- Navigate pinned folders with one click and remove pins from the pinned-folder context menu.

## 0.1.4

- Make folder paths easier to edit from the top active path and breadcrumb bars.

## 0.1.3

- Restore the previous session on startup, including pane paths, active pane, view/layout state, window placement, and splitter widths.

## 0.1.2

- Apply the upstream `tfx` app icon to the Windows executable and main window.

## 0.1.1

- Hide dot-prefixed files and folders, such as `.git` and `.env`, when hidden files are disabled.

## 0.1.0

Initial Windows release of `tfx`.

- WPF implementation of the terminal-inspired two-pane file manager.
- Folder tree, pinned folders, breadcrumb path bars, search, Details and Icons views.
- Keyboard-first navigation, copy/cut/paste, rename, Recycle Bin delete, permanent delete, drag and drop, shortcuts, zip compression, and zip extraction.
- Image and text preview pane with persistent split/preview/view settings.
- Dark theme, themed title bar and scroll bars, compact toolbar, and Windows shell icons.
