# Changelog

## 0.7.2

### Fixes

- **Reveal in Explorer no longer crashes**: the context-menu "Reveal in Explorer" command resolved `explorer.exe` under `System32`, where it does not exist, so launching it threw an unhandled `Win32Exception` and the app terminated. It now uses `%SystemRoot%\explorer.exe` (the Windows directory). The absolute-path hardening against a planted `explorer.exe` on `PATH` / in the CWD is preserved.

### Terminal: more signal buttons

- The terminal pane header now has **`^\` (Ctrl+\\, 0x1C / quit)** and **`^Z` (Ctrl+Z, 0x1A / EOF)** buttons next to the existing **`^C`** interrupt. They send the control byte straight to the running shell from C#, so they work even where the in-WebView2 keyboard equivalents aren't delivered.

## 0.7.1

### User-defined commands: shortcuts, finer tokens, multi-line scripts

- A command can carry a `shortcut` (same grammar as `[shortcuts]`, e.g. `"ctrl+shift+g"`); pressing it runs the command when the current context matches its filters. The shortcut is shown next to the command in the context menu, and command shortcuts take precedence over the built-in ones.
- `run` gains finer path tokens for the first selected item: `{name}` (file name with extension), `{stem}` (without extension), `{ext}` (extension only, no dot), alongside the existing `{path}` / `{dir}` / `{cwd}`.
- `run` accepts a **multi-line script** via TOML's `'''…'''` literal string; tfx writes it to a temp file and runs it with the per-command `shell` (if set) or the `[terminal] shell` (PowerShell `.ps1`, `cmd` `.bat`, or `bash` `.sh`; tokens still substituted). Pair with `terminal = true` to view the output. A per-command `shell` key overrides the shell for that one command.

## 0.7.0

### User-defined commands in the context menu (roadmap §2.15)

- New `[[commands]]` array in `config.toml` adds **user-defined commands to the file-pane context menu**, each running an external program (fire-and-forget) — the shell-script alternative to an embedded scripting runtime, consistent with how tfx already shells out for `[openWith]`, the terminal, and git. A command appears only when the current selection matches its filters.
- Each entry: `name` (menu label), `run` (command line with `{path}` / `{paths}` / `{dir}` / `{scripts}` tokens, env-var expanded), and optional filters `extensions` (omit or `["*"]` = all files), `target` (`file` / `folder` / `any`), `selection` (`single` / `multiple` / `any`). Path tokens are quoted when substituted; `{scripts}` expands to the `scripts` folder next to `config.toml` (`%APPDATA%\tfx\scripts`) so commands can call bundled scripts without an absolute path. Invalid entries are reported as config warnings and skipped. Documented in `docs/configuration.md` / `.ja.md`.
- Set `terminal = true` on an entry to capture its stdout / stderr into the terminal pane's read-only **Output** tab instead of launching a separate process. The terminal pane now has two tabs — the interactive **Shell** and the command **Output** sink — with the tab strip appearing once Output has content. The `{scripts}` token expands to `%APPDATA%\tfx\scripts` so commands can call bundled scripts (e.g. a `wc.ps1`) without an absolute path.
- `target = "current"` makes a command act on the **current folder** and appear even with nothing selected (right-click the empty area) — for folder-wide actions. `requireGit = true` restricts a command to Git working copies, and the new `{cwd}` token expands to the current folder, so e.g. `git push` can be wired as `run = "git -C {cwd} push"`, `target = "current"`, `requireGit = true`, `terminal = true`.
- This supersedes the earlier Lua extension plan (former roadmap §2.16), which is dropped.

## 0.6.9

### Fix: terminal failed to start as a single-file executable

- The xterm.js assets (terminal.html, xterm.js/css, addons) are now **embedded in the executable** and extracted to `%LOCALAPPDATA%\tfx\terminal` at startup, then served to the WebView2 terminal via the virtual host. Previously they shipped as loose `Content` files next to the exe; in a single-file publish those files aren't present at `AppContext.BaseDirectory`, so the terminal page couldn't load and the pane failed with `0x80070003` (path not found). tfx now runs as a true single file.
- Both the terminal and the Markdown / HTML preview also create their WebView2 with a **user-data folder pinned to `%LOCALAPPDATA%\tfx\WebView2`** (shared `CoreWebView2Environment`). WebView2's default user-data folder sits next to the executable; when tfx is installed read-only (e.g. `Program Files`) that folder can't be created and initialization fails. Pinning it to a writable per-user location avoids that.
- `scripts/build-release.ps1` now wipes the publish folder before building (so stale loose assets from older versions can't linger), suppresses `.pdb` output (`DebugType=none`), and warns if anything other than `Tfx.exe` ends up in the single-file release.

## 0.6.8

### Preview: load external images on demand

- Markdown / HTML preview keeps blocking external network requests by default (`img-src data:` in the CSP), but a new **Load images** button in the preview header re-renders the current document with external `https:` images allowed (e.g. shields.io badges). The permission is **per-render and never remembered** — selecting any file, including re-selecting the same one, requires pressing the button again. Scripts, `fetch`, and external styles stay blocked.

### Localization fixes

- Fixed several tooltips that stayed English on a Japanese OS because they bypassed `ApplyLocalization` (XAML-literal tooltips) or used dictionary keys that didn't match the `({0})` placeholder form the code looks up: the toolbar navigation / search / hidden / terminal / reload / preview / split / swap buttons, the terminal-pane toggle, the window Minimize / Maximize / Close buttons, the terminal interrupt (`^C`) and close buttons, and the new Load-images button. All UI strings resolve to Japanese on a Japanese OS and to the English source text elsewhere.

## 0.6.7

### WebView2 runtime check for the terminal

- Opening the terminal pane now **checks for the Edge WebView2 Runtime** first (via `CoreWebView2Environment.GetAvailableBrowserVersionString`). On a clean Windows 10 without the runtime the pane used to open blank with no shell; it now shows a status-bar hint pointing to the runtime installer instead. The README documents the runtime as a prerequisite.

### Terminal copy / paste / interrupt

- Added an **Interrupt** button to the terminal pane header that sends Ctrl+C / ETX (`0x03`) to the running shell. The in-WebView2 keyboard `Ctrl+C` isn't reliably delivered on all setups, so the button is the dependable way to interrupt a running command.
- The built-in terminal also handles **`Ctrl+C`**, **`Ctrl+V`**, and their `Ctrl+Shift+` variants in the page where the key event is received: `Ctrl+C` copies when there's a selection and sends the interrupt when there isn't; `Ctrl+Shift+C` always copies; `Ctrl+V` / `Ctrl+Shift+V` paste. Clipboard read for paste is enabled by granting only the WebView2 `ClipboardRead` permission.

### Drop files onto the terminal to insert their paths

- Dragging files / folders from a file pane onto the built-in terminal now **types their full paths at the prompt** (space-separated, double-quoted when a path contains spaces — valid for both cmd and PowerShell). The WebView2's own external-drop handling is disabled so the drop reaches WPF, which can read the full `FileDrop` paths; a browser context can't expose them for security reasons. The shell is focused after the drop.

### Faster terminal first-open

- The built-in terminal pane now **warms up the WebView2 + xterm.js page in the background** once the UI goes idle at startup (when the pane isn't already shown). Previously the first open was slow because creating the WebView2 runtime process and loading/parsing xterm (~550 KB of JS) happened only on demand; pre-loading it means opening the pane just has to spawn the shell. The `_terminalPaneOpen` guard keeps the warm-up's resize message from starting a shell before the pane is actually opened.

### Quit shortcut

- Added a remappable **`quit`** shortcut (default `Ctrl+Q`) that closes the window — running `Window_Closing` so the session is saved and the terminal is torn down. It is ignored while the built-in terminal pane is focused so the shell keeps `Ctrl+Q` (XON/XOFF flow control); `Alt+F4` continues to close the window unconditionally. Configurable via `config.toml` `[shortcuts]` `quit`.

### Startup folder fix

- **Command-line path / working directory now wins at startup again.** Launching `Tfx.exe <folder>` or starting it from a terminal (a "meaningful" current working directory) is supposed to open that folder in the left pane, but since pane tabs landed in 0.6.4 the saved-tab restore ran *after* `ResolveInitialPath` and overwrote `_leftPath` with the previously saved tab, so the requested folder was discarded. `ResolveInitialPath` now reports whether the folder came from an explicit source (command-line arg or meaningful CWD); when it did, the left pane opens it as a single fresh tab and skips the saved-tab restore. Normal launches (Explorer / Start menu — working directory not meaningful) still restore the saved tab set, and the right pane always restores its saved tabs.

## 0.6.6

### Terminal pane lifecycle refinements

- **Fresh shell on reopen**: closing the terminal pane now disposes the running shell while keeping the xterm.js page alive; reopening clears the screen (a `reset` message to the page) and spawns a brand-new shell, so each open starts clean instead of resuming the previous session. A `_terminalPaneOpen` guard prevents the page's reset-fit resize message from spawning a shell while the pane is closed.
- **Clean teardown on app exit**: `Window_Closing` now stops the terminal WebView2 and calls `ShutdownTerminal()`, so no orphaned pseudo console / shell process lingers after tfx closes. Internally the teardown is split into `DisposeTerminalIfAny()` (PTY only, for pane close) and `ShutdownTerminal()` (PTY + readiness flags, for app exit).
- **Click anywhere to focus**: clicking inside the terminal pane moves keyboard focus to the terminal.

## 0.6.5

### Built-in terminal pane (roadmap §2.9)

- Collapsible terminal pane docked at the bottom of the window, toggled with `Ctrl+J` or the file-pane context-menu "Toggle terminal pane". Resize its height by dragging the splitter; close it with the header `×` or `Ctrl+J`. (The default is `Ctrl+J` rather than `Ctrl+\`` because on Japanese keyboards the `` ` `` key sits at the IME-bound 半角/全角 position and never reaches the app; remap via `config.toml` `[shortcuts]` `toggleTerminal` if desired.)
- Rendering uses **xterm.js** (the terminal engine VS Code uses) hosted in a WebView2 control, loading bundled assets from `Assets/terminal/` via a local virtual host (no network/CDN). This replaces an initial self-written VT parser, giving full 24-bit + xterm-256 color, correct CJK / wide-character width, scrollback, and proper full-screen TUI rendering (vim, less, etc.).
- Shell I/O flows through tfx's own ConPTY wrapper (`src/Services/ConPty.cs`, `CreatePseudoConsole` via P/Invoke): PTY output → base64 → xterm `write`; xterm `onData` → ConPTY stdin; fit-addon resize → `ResizePseudoConsole`.
- The shell starts in the active pane's folder when the pane opens (startup cwd only). Shell, font, font size, and the full ANSI palette are configurable from `config.toml` `[terminal]` (`shell` / `font` / `fontSize` / `background` / `foreground` / `cursor` / 16 ANSI slots); omitting `background` keeps the terminal transparent so window translucency shows through. Defaults to `powershell.exe -NoLogo`, falling back to `%ComSpec%` / cmd. Typing `exit` closes the pane.
- Persists visibility and height in `settings.json` (`ShowTerminalPane` / `TerminalPaneHeight`, additive). The shortcut parser learns the `` ` `` (backtick) key token.

## 0.6.4

### Pane tabs (roadmap §2.7)

- Each pane now carries multiple tabs, each with its own current folder, back / forward history, and remembered selection. `src/Models/PaneTab.cs` holds the per-tab state; `src/Views/MainWindow/MainWindow.Tabs.cs` owns the `_leftTabs` / `_rightTabs` lists and active-index tracking.
- This also fixes a latent bug: back / forward history used to be a single global stack shared by both panes (`_back` / `_forward`). History is now genuinely per-tab, so `Alt+Left` / `Alt+Right` only walk the active tab's own history.
- A tab strip appears above each pane's address bar, shown only when that pane has 2 or more tabs. Click a tab to switch; click the `×` (or middle-click) to close. The active tab's folder name is highlighted.
- Keyboard: `Ctrl+T` new tab (at the active pane's current folder), `Ctrl+W` close tab, `Ctrl+Shift+]` next tab, `Ctrl+Shift+[` previous tab. All four are remappable via `config.toml` `[shortcuts]` (`newTab` / `closeTab` / `nextTab` / `prevTab`); the shortcut parser now understands `[` and `]` key tokens.
- Context menu: "New Tab" always available; "Open in New Tab" appears when a single folder is selected.
- Closing the last tab of the right pane collapses it to single-pane view (split off); the left pane always keeps at least one tab.
- Tab lists (paths + active index per pane) persist in `settings.json` (`LeftTabs` / `RightTabs` / `LeftActiveTab` / `RightActiveTab`, additive — older files keep working) and are restored on startup, dropping any tab whose folder no longer exists.

## 0.6.3

- Added `%APPDATA%\tfx\config.toml` support with tfx-compatible `version = 1` sections for `[font]`, `[colors]`, `[opacity]`, `[shortcuts]`, `[startup]`, `[terminal]`, and `[openWith]`.
- Shortcut defaults and generated `config.toml` now use Windows-native `ctrl` / `alt` / function-key notation, including `F5` reload and Explorer-like `Alt+Left` / `Alt+Right` / `Alt+Up` navigation.
- Light color themes now work across the WPF shell: toolbar/status chrome, inputs, selected rows, icon view, tree hover/selection, scrollbars, text/CSV preview, and Markdown preview CSS are all theme-resource driven.
- Theme brushes now use dynamic WPF resources so colors loaded from `config.toml` update existing controls; the native title bar also follows the configured light/dark colors.
- `[opacity].background` now enables a transparent WPF window surface with custom chrome, and `[opacity].inactivePane` now affects the inactive pane surface instead of being parsed only.
- The terminal launcher now opens at the active pane's folder even when no terminal arguments are configured; default cwd arguments are supplied for Windows Terminal, WezTerm, PowerShell, and pwsh.
- `[startup].preview` now supports `show`, `hide`, and `restore` to control whether the preview pane is visible at startup.
- Terminal and per-extension open-with settings can now be supplied from `config.toml`; the existing `settings.json` remains the persisted session-state file.
- Added English and Japanese configuration guides plus a standalone Japanese README.

## 0.6.1

- **PDF preview speed**: render target reduced from 800 px back to the long-standing 600 px (rasterization cost drops to ~56% with no perceptible quality loss now that the preview Image uses `RenderOptions.BitmapScalingMode="HighQuality"`). `FindPdftoppm` is memoised per session — the first lookup still walks `%PATH%` and the 9 known install paths with `FileVersionInfo` vendor verification, but subsequent renders skip the ~10–100 ms scan entirely.
- **Pinned-folder highlight tracks the active pane**: clicking a pinned folder, then navigating elsewhere via the file list / address bar / folder tree, used to leave the pinned entry highlighted. Re-clicking the same pin then did nothing because WPF's `SelectionChanged` doesn't fire when the selection doesn't actually change. New `SyncPinnedSelectionToActivePane` is called from `UpdateActivePane` and at the end of `Reload`; it sets the pinned list's `SelectedItem` to the entry that matches the active pane's current folder, or clears it when no pin matches. A `_syncingPinnedSelection` guard prevents the re-entrancy that would otherwise re-trigger `Navigate`.

## 0.6.0

### PDF preview overhaul

- **New multi-stage renderer pipeline**: disk cache → shell cache → external `pdftoppm` (auto-detected) → `Windows.Data.Pdf` (OS-shipped PDFium-derived) → shell thumbnail provider. Earlier shell-only path returned 256-px-ish blurry images and failed on OneDrive / Google Drive virtual files (`0x8004B2xx` from the cloud-files API). The new pipeline renders at the requested resolution and works on cloud-synced files.
- **`Windows.Data.Pdf` integration**: targets `net10.0-windows10.0.19041.0` so the WinRT projection is available; `SupportedOSPlatformVersion=10.0.17763.0` keeps Windows 10 1809 compatibility. New `src/Services/WinRtPdfRenderer.cs` opens the PDF via `FileStream` + `AsRandomAccessStream` (cheaper than `StorageFile.GetFileFromPathAsync`) and forces an opaque-white background so transparent pages don't render dark against tfx's dark preview.
- **External `pdftoppm` auto-detection**: when `PdfRendererPath` is empty, tfx searches `%PATH%`, Calibre's bundled location, the poppler-windows standalone layout, Scoop's, Chocolatey's, and Conda/Miniforge install paths. Each candidate is gated by a `FileVersionInfo` vendor check (must identify itself as Poppler / Xpdf / pdftoppm), so a bare `pdftoppm.exe` dropped in a writable PATH entry without the right metadata is rejected.
- **Job Object isolation for `pdftoppm`**: 256 MB process-memory cap, max 4 active processes per job, `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` so leftovers die with tfx, `JOB_OBJECT_LIMIT_DIE_ON_UNHANDLED_EXCEPTION` for early termination on faults. `src/Services/JobObject.cs` provides the Win32 wrapper.
- **Persistent disk cache**: rendered first pages are written to `%LocalAppData%\tfx\pdf-cache\<sha256>.png` (up to 200 entries, LRU by mtime). Re-selecting a previously-rendered PDF is essentially free even after restarting tfx. Cache key includes path + last-write time + length + render size, so external edits invalidate the entry automatically.
- **High-quality WPF scaling**: `ImagePreview` now sets `RenderOptions.BitmapScalingMode="HighQuality"` so the bitmap doesn't look pixelated when the preview pane is wider than the source.
- **Configurable**: `AppSettings.EnablePdfPreview` (master toggle), `PdfPreviewMaxBytes` (default 500 MB), `PdfRendererPath` (pin to a specific `pdftoppm.exe`), `AllowShellPdfThumbnail` (in-process shell thumbnailer; default `true`).
- **Diagnostic error surfacing**: shell-thumbnail HRESULT / exception text is now propagated to the preview pane instead of being swallowed and replaced with the generic "no provider available" message. `0x8004B2xx` errors get an inline hint pointing at "Always keep on this device" or `PdfRendererPath`.

### Security hardening

- **Markdown XSS**: Markdig pipeline now uses `.DisableHtml()` so inline `<script>` / `<img onerror>` / `javascript:` URLs in `.md` files cannot execute. A strict CSP `<meta>` (`default-src 'none'; img-src data:; style-src 'unsafe-inline'`) is injected as defense in depth.
- **HTML preview**: `.html` / `.htm` files are no longer navigated via `file://` (which would have re-enabled same-origin `file://` fetches). They are loaded with `NavigateToString` inside a CSP-wrapped document. WebView2 is configured with `IsScriptEnabled=false`, `AreDevToolsEnabled=false`, `IsWebMessageEnabled=false`, `AreHostObjectsAllowed=false` for the preview pane.
- **Zip Slip guard** in `ArchiveBrowser.ExtractEntriesToTemp`: entries containing `..\` or absolute paths now fail the `IsPathInside` check and are skipped. `ZipFile.ExtractToDirectory` provides this guard automatically, but the on-demand extractor used by zip browsing was hand-rolled.
- **`TextPreviewReader` OOM**: previously called `File.ReadAllBytes` then truncated to 256 KB, so clicking a multi-GB `.log` could exhaust memory. Now streams only the first 256 KB via `FileStream`.
- **Git command hardening**: `GitStatusReader` resolves `git.exe` once via PATH lookup and pins the absolute path, blocking PATH planting. The command now includes `-c core.fsmonitor= -c core.hooksPath=NUL -c protocol.file.allow=never -c core.sshCommand= -c core.pager=cat` to defuse CVE-2022-24765-class repository-config exploits.
- **`{path}` injection in `TerminalLauncher`**: the working-directory substitution is now always wrapped in `"..."` with embedded quotes doubled, so a folder name containing spaces / quotes / `&` / `|` cannot break out of the argument and be re-interpreted as additional arguments or shell commands.
- **`CreateShortcut` target validation**: `FsHelpers.CreateShortcut` now requires the source path to exist (`File.Exists || Directory.Exists`). Without this check, a forged `FileDrop` payload from another process could persist arbitrary command strings (e.g. `cmd.exe /c calc & ...`) as a `.lnk` `TargetPath` that any later user click would execute.
- **Archive temp cleanup on startup**: `EnsureArchiveTempRoot` opportunistically sweeps `%TEMP%\tfx\archive-*` leftovers from previous tfx runs that crashed before they could delete their own folders, instead of accumulating extracted (possibly sensitive) files indefinitely.
- **`explorer.exe` absolute path**: `RevealInExplorer` now uses `%SystemDirectory%\explorer.exe` so it cannot race a planted `explorer.exe` on PATH or in CWD.

## 0.5.4

- Auto-refresh now updates modified date / size / attributes / owner in place when a file on disk changes. `DiffApply` used to match rows by name only and skip everything else, so an externally edited file kept showing its old timestamp and size until the user navigated away and back. `FileItem`'s `Modified` / `ModifiedText` / `Size` / `SizeText` / `OwnerText` / `AttributeText` are now backing-field properties with `INotifyPropertyChanged`, and `DiffApply` calls a new `UpdateMutableFrom` helper whenever the fresh listing reports different metadata for an existing row. Selection, focus, and scroll position are preserved because the row instance is reused.
- Toolbar trim: removed the **Select all**, **Open folder**, and **Reveal in Explorer** buttons. The keyboard shortcut `Ctrl+A` still selects all, `Reveal in Explorer` remains available from the right-click context menu, and navigation is well covered by the editable path bar, drag-and-drop, pinned folders, and the folder tree. Unused handlers (`SelectAll_Click`, `OpenFolder_Click`, `Explorer_Click`, `OpenFolderPicker`) and the matching `Loc` entries / README rows were removed alongside.

## 0.5.3

- Right-button drag from tfx to Windows Explorer now uses a shell-native drag source for real filesystem items. `ShellFileDrag` builds a real Shell `IDataObject` with `SHParseDisplayName` / `SHCreateDataObject` and starts the drag with `ole32!DoDragDrop`, so Explorer receives the same kind of data object it expects from a normal Explorer file drag. This makes Explorer's standard right-drag menu include `ショートカットをここに作成` / "Create shortcut here" in addition to Copy / Move / Cancel.
- Kept tfx-internal right-button drops on the existing WPF path. While a shell-native drag is active, drops back into a tfx pane are detected and routed to tfx's own Copy / Move / Create shortcut / Cancel menu, which then executes through the existing `ExecuteDrop` logic and `FsHelpers.CreateShortcut`.
- Avoided the unsafe intermediate approaches discovered during testing: a hand-built `Shell IDList Array` payload could crash Explorer, and setting only `Preferred DropEffect = Link` on a plain `FileDrop` payload caused Explorer to show the no-drop cursor. The final implementation does not inject those ad hoc formats; it asks the Shell to create the data object.
- Refactored drag cleanup so right-drag state, context-menu suppression, pane reloads, and pending-drag clearing are handled in small helpers (`TryStartNativeRightDrag`, `BuildFileDropData`, `CompleteFileDrag`). Archive drag-out remains restricted to Copy and continues to use the WPF `FileDrop` fallback.

## 0.5.2

- Configurable terminal launcher: the "Open Terminal here" command honors a user-specified executable and argument template stored in `AppSettings.TerminalCommand` / `TerminalArguments`. Empty values keep the previous auto-detect behavior (`wt.exe` when running inside Windows Terminal, otherwise `powershell.exe`).
- New "Terminal Settings..." entry in the file-pane context menu opens a small dialog with Command / Arguments / Reset fields. Arguments support the `{path}` placeholder (replaced with the current pane's folder) and environment variable expansion (e.g. `%ProgramFiles%`).
- If a custom command fails to start, the launcher falls back to `powershell.exe` so the user is never stuck with a broken configuration.

## 0.5.1

- Git column centered: the badge character now centers in the 30 px column instead of hugging the left edge.
- Toggle split-on copies the left pane's current folder to the right pane (unless they already match), so opening split view starts both sides at the same known location.
- Arrow-key wrap-around in the file list: `..` + Up wraps to the bottom entry; the bottom + Down wraps to `..`. PageUp / PageDown still clamp at the edges.
- Arrow keys with no current selection (e.g. right after a rename) now land on `..` so navigation can resume immediately.
- Rename keeps the renamed entry selected: the new name is set as a pending selection target before the post-rename reload, restoring selection + focus on the renamed row.

## 0.5.0

- Git status indicators (roadmap §2.6): each file row in a Git working copy shows a one-character badge in a new "Git" column (M / A / D / R / C / ? / ! / U), and the current branch appears in the status bar as `⎇ name` next to the free-space text. Directories aggregate to "M" or "?" based on descendants. Uses `git status --porcelain=v2 --branch --untracked-files=normal --no-renames` on a background thread with cancellation and an 8-second timeout; silently disables itself if `git` is not on `PATH`. Refresh hooks: navigation, reload completion, and the auto-refresh diff path. `Tfx.Core/GitStatus.cs` parser and `Tfx.Tests/GitStatusParserTests.cs` (20 cases).
- JSON pretty-print now uses `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`, so CJK / Latin-with-diacritics / emoji characters stored as `\uXXXX` escape sequences in the source file display as the actual characters in the Rendered preview.
- Preview pane toggle now expands the window to the right when shown and shrinks it back when hidden (only when the window is not maximized), keeping the two file panes at the same width regardless of preview visibility. Clamps to the working area; shifts `Left` if growing would push past the right edge.
- Attribute column width reduced from 140 px to 90 px — comfortably fits the `drwxr-xr-x` strings while reclaiming space for other columns.
- `FileItem` gained `INotifyPropertyChanged` + a mutable `GitStatusText` property so badges can update without rebuilding rows.

## 0.4.5

- Subfolder search reliability fixes: switch the walker to a single `EnumerateFileSystemInfos` call with `EnumerationOptions { RecurseSubdirectories=true, IgnoreInaccessible=true, AttributesToSkip=ReparsePoint(|Hidden|System) }`, so the runtime handles recursion / permission errors / attribute filtering efficiently — particularly on SMB / network shares. Mid-iteration enumeration errors no longer abort the whole subtree silently.
- Subfolder search matching now uses `CompareInfo.IndexOf` with `IgnoreCase | IgnoreWidth | IgnoreKanaType`, so full-width / half-width and hiragana / katakana variants match the query.
- Detect drive arrival / removal via the `WM_DEVICECHANGE` Windows message and refresh the folder-tree drive list with a 250 ms debounce. Plugging in a USB drive now shows it without restarting the app.
- Swap left and right panes shortcut moved from `Ctrl + Shift + S` to `Ctrl + Shift + X` to free up Ctrl + Shift + S for future "Save" semantics.
- File-list column alignment fix carry-over (0.4.4 mid-stream): shared cell-text styles set `VerticalAlignment="Center"` and `TextTrimming="CharacterEllipsis"`.
- PDF preview speed: reduce render target from 1200 px to 600 px (covers HiDPI ×2 while halving rasterisation cost) and replace pdftoppm's polling exit wait with `WaitForExitAsync` (eliminates up to 100 ms tail latency).
- PDF preview reliability: add `SIIGBF_INCACHEONLY` flag to the shell-thumbnail fast path so it never blocks on background thumbnail generation, fixing "blank on first selection, shows on second" symptom. The renderer now falls back through three stages: shell-cache → pdftoppm → shell-generate.
- PDF preview cache: in-process LRU (10 entries, keyed by path + last-write time + length + render size) so repeated selections of the same PDF return instantly without re-rendering.

## 0.4.4

- Subfolder search (roadmap §2.4): the search box now always runs a recursive walk of the current folder. Type and press **Enter** to start; **Esc** clears and restores the real folder listing. Results stream into the active pane in batches of 50 with the status bar updating every ~120 ms ("Searching: N matches" → "Search complete: N matches"). Navigation to another folder cancels the in-flight search. Results show the relative path in the Name cell while keeping `FullPath` absolute. Inside zip archives the recursive walk is a no-op.
- Removed the recursive-search toggle button and the `Ctrl+Shift+F` shortcut per user feedback — search is always recursive and only fires on Enter, so no auto-filter on text change. The in-memory `CollectionView` filter is dropped accordingly.
- File-list column alignment fix: the shared `ListingCellText` / `RightAlignedCellText` styles now set `VerticalAlignment="Center"` and `TextTrimming="CharacterEllipsis"`, so Date / Type / Size / Created / Owner / Attribute cells line up with the centered Name column and clip with ellipsis instead of bleeding into adjacent columns. The Name column TextBlock also gained `TextTrimming="CharacterEllipsis"`.

## 0.4.3

- Multi-selection preview (roadmap §2.3): selecting more than one item in the file list now renders a compact summary in the preview pane — total count, combined size, and per-item name / kind / size / modified up to a cap of 8 entries, with a "(+N more)" footer for the rest. Image / text / CSV / HTML previews are hidden during multi-selection and any in-flight preview load is cancelled.
- Performance measurement infrastructure (roadmap §2.5): `Tfx.Core/PerformanceTrace.cs` exposes a `Begin(label)` / `Measure(...)` static helper that no-ops when disabled (zero overhead) and emits one line per call to `Debug` and the console when enabled. Activated by either the `TFX_PERFORMANCE_LOGS=1` environment variable (wins) or the persisted `AppSettings.ShowPerformanceLogs` setting. Trace points wired into `DirectoryLoader.Load`, `PreviewLoader.Load`, `ApplySearchFilter`, `CsvParser.Parse`, and `JsonPrettyPrinter.TryPrettyPrint`.
- Add `Tfx.Tests/Benchmarks/PerformanceBenchmarks.cs` with 7 informational benchmarks (`ArchivePath` × 2, `CsvParser` × 3 sizes, `JsonPrettyPrinter` × 2) that print per-iteration timings via `ITestOutputHelper` and never assert. Total test count: 63.
- Always land on the left pane at startup with the `..` row preselected and focus on the file list, regardless of the saved `ActivePane` value. A belt-and-braces `FocusPane(Pane.Left)` call at `Loaded` + `ApplicationIdle` ensures focus survives the async initial reload.
- Extend `docs/contributing.md` with the performance-trace env var, the in-app setting key, and the benchmark filter commands.

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
