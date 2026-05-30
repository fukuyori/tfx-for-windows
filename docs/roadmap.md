# tfx for Windows — Development Roadmap

This document defines the development order for `tfx-for-windows`. It is adapted from the upstream macOS roadmap [`fukuyori/tfx/docs/development-roadmap.md`](https://github.com/fukuyori/tfx/blob/main/docs/development-roadmap.md) with Windows-specific adjustments where the underlying platform differs (Win32 / WPF / .NET 10 rather than AppKit / SwiftUI).

Each item in §1 and §2 carries an `Upstream:` line that points back to the corresponding section in the upstream roadmap when one exists, so the two documents can be cross-referenced as upstream evolves.

Project documentation is written in English by default. `README.md` is the English README; `README.ja.md` is maintained as the Japanese README.

---

## 0. Principles

- Prioritize everyday responsiveness, clear selection state, and predictable drag-and-drop behavior.
- Use the folder tree for display, navigation, and choosing file drop targets.
- Do not allow moving real folders within the folder tree.
- Allow dragging files from the file view onto folders in the folder tree.
- Treat pinned folders as shortcuts. Only their order in the `PINNED` section can be changed.
- Store user-editable settings under `%APPDATA%\tfx\`.
- Use `config.toml` for declarative configuration, Lua for future dynamic extension, and `settings.json` for UI state such as window placement and pane widths. (Lua remains planned; see §2.16.)
- Sandbox Lua in the initial implementation. Do not allow file mutation or external command execution.

---

## 1. Completed Work

Tracked here in version-tagged sections so the implementation history stays auditable.

### 1.1 Through 0.3.0 — Foundation

Upstream: covers material from §1.1 (Responsiveness), §1.2 (File View and Preview), §1.3 (Folder Tree and Pinned Folders), §1.5 (Archive and Context Menu), §1.6 (Navigation Refinement), §1.7 (Finder Compatibility — clipboard / `.lnk` / hidden folders), §1.8 (Selection and Preview Details).

- WPF / .NET 10 two-pane file manager with split and single layouts.
- Folder tree + pinned-folder sidebar with first-run defaults (User profile, Desktop, Documents, Downloads).
- Editable breadcrumb address bar with environment-variable expansion.
- Details and Icons view modes with persisted column visibility / order.
- Inline rename, New File / New Folder, Recycle Bin and permanent delete, drag-and-drop with full Windows modifier-key conventions (`Shift` = move, `Ctrl` = copy, `Alt` = shortcut), shortcut (`.lnk`) creation via WScript.Shell.
- Zip compression and extraction.
- Image / text preview pane with persistent split / preview / view settings.
- Status bar with item counts, selection size, active drive's free space, and version.
- Dark theme, themed title bar and scroll bars, compact toolbar, Windows shell icons.
- Japanese / English UI driven by `CultureInfo.CurrentUICulture`.
- Hidden-files toggle covers Windows Hidden-attribute entries and dot-prefixed names.
- Folder-tree subdirectory probing is per-item and lazy.
- Session restore: pane paths, active pane, view / layout state, window placement, splitter widths.

### 1.2 0.3.1 — Live Refresh, Open With, Zip Browsing

Upstream: §1.9 (Live Refresh and Reload) — `FileSystemWatcher` instead of `DispatchSource`-based `DirectoryWatcher`. §1.5 (Archive and Context Menu) — read-only zip browsing and Open With.

- **Auto-refresh**: each pane subscribes to a `FileSystemWatcher` on its current folder with debounce, plus a periodic fallback poll for missed events. Refresh skips while renaming, dragging, rubber-band selecting, the context menu is open, or the current path is inside a zip. Reloads use a diff (add / remove / move) against the existing list so scroll position and selection survive.
- **Context menu**: "Open with..." invokes the standard Windows dialog via P/Invoked `SHOpenWithDialog`. Item order rearranged to follow Windows 11 conventions (open / locate / pin → clipboard + copy path → archive → current-folder actions → destructive operations at the bottom).
- **Initial folder resolution**: command-line argument → meaningful current working directory (skipped when the CWD equals the executable's own directory, `System32`, or `Windows`) → saved path → user profile. Launching `Tfx.exe` from a terminal opens the terminal's current folder.
- **Browse inside `.zip`**: virtual path scheme `<zip>::<inner>` makes zips navigable like folders. Subfolders inside the archive are navigable, files extract on demand to `%TEMP%\tfx\archive-<id>\…` when opened or dragged out, and the breadcrumb bar shows each zip level as a clickable segment. The temp folder is removed on close. Zip-internal editing (rename, delete, paste, new file/folder) is structurally disabled when inside an archive.

### 1.3 0.3.2 — Responsiveness Pass

Upstream: aligns with §1.1 (Responsiveness and Interaction) — incremental display, debouncing, cancellation. No 1:1 mapping for the LoadDrives background pass; it is Windows-specific because slow network drives surface differently here.

- Preview updates debounced (~120 ms) so arrow-key navigation stops spawning a preview task per row.
- Initial directory load is batched (200 items per batch with `Dispatcher.Yield`) so large folders no longer freeze the UI thread.
- Auto-refresh debounce shortened from 250 ms to 150 ms.
- Search box debounced (~150 ms). `Esc` clears and `Enter` applies the filter immediately.
- `LoadDrives` runs on a background thread with per-drive subdirectory probing via `AsParallel`, so a slow or unresponsive network drive no longer blocks startup or the hidden-files toggle.

### 1.4 0.4.0 — Code Organization

Upstream: §1.4 (Code Organization). The Windows project uses `MainWindow.*.cs` partials in place of feature-oriented Swift files.

- `MainWindow.FileOps.cs` split: drag-and-drop logic (~310 lines) moved into a new `MainWindow.DragDrop.cs` partial. Unused click-handler stubs removed. `MainWindow.FileOps.cs` dropped from 852 to 535 lines.
- `Pane` enum (`Left` / `Right`) and helpers (`PaneOf`, `GridOf`, `IconViewOf`, `ItemsOf`, `PathOf`, `ActivePane`) added in `MainWindow.Pane.cs`. Reload / AutoRefresh / Navigation / Keyboard / External / RubberBand / status updates refactored to use them, eliminating `isLeft` booleans and most `LeftGrid` / `RightGrid` ternaries.

### 1.5 Windows-Specific Capabilities (not present in macOS roadmap)

Upstream: none — these are platform-specific or are Windows-side equivalents of macOS-native facilities (e.g., WebView2 instead of `WKWebView`-via-Quick Look, `SHOpenWithDialog` instead of `NSWorkspace.shared.openURL` with `Configuration.applicationURL`).

- `MiddleEllipsisTextBlock` custom control: width-based middle ellipsis for pinned-folder paths, recomputed on `SizeChanged` of the parent `ScrollContentPresenter`.
- WebView2-based Markdown and HTML preview (via `Microsoft.Web.WebView2` + `Markdig`) with a Source / Rendered toggle in the preview header. State persists as `RenderMarkdownHtml` in settings.
- Zip drag-out: zip entries are extracted on demand and surfaced through a `FileDrop` payload so Explorer or other applications can accept the drop. Effect is restricted to `Copy` to prevent accidental Move on the virtual source.
- `SHOpenWithDialog` P/Invoke wrapper (`ShellOpenWith`) for the native "Open with" dialog.

### 1.6 0.6.3 — Configuration, Theme Surfaces, and Custom Chrome

Upstream: covers material from §2.6 (Built-in Color Themes), §2.8 (Configuration Foundation), §2.9 (Shortcut Organization), §2.10 (Theme Customization via TOML), §2.11 (Extension-Based Behavior), and replaces the Windows-side terminal hand-off from §2.4.

- `%APPDATA%\tfx\config.toml` v1 support with `[font]`, `[colors]`, `[opacity]`, `[startup]`, `[shortcuts]`, `[terminal]`, and `[openWith]`.
- Windows-native shortcut notation through `[shortcuts]` (`ctrl`, `alt`, `shift`, `win`, function keys, arrows, punctuation keys). Legacy `cmd` / `command` aliases are intentionally not accepted.
- Runtime theme resources now follow `[colors]`, including light-mode palettes, active/inactive pane colors, selection colors, scrollbars, Markdown preview CSS, and custom chrome colors.
- `[opacity].background` applies to the WPF window surfaces instead of `Window.Opacity`, so text and icons remain readable while the background can be translucent or fully transparent.
- Custom transparent chrome replaces the standard title bar: the top empty toolbar area is draggable, double-click toggles maximize/restore, and the right window edge remains resizable even when `background = 0.0`.
- `[startup].layout`, `[startup].rightFolder` / `rightFolders`, and `[startup].preview` control initial split/single layout and preview-pane visibility.
- `Open Terminal here` uses the active file pane folder by default. If `[terminal]` is omitted it starts Windows Terminal (`wt.exe -d {path}`) with a PowerShell fallback; configured apps such as WezTerm get sensible default arguments when only `app` is provided.
- English and Japanese configuration guides are maintained (`docs/configuration.md`, `docs/configuration.ja.md`), including three distinctive color samples and one explicit light-mode sample.
- Manual 0.6.3 checks are complete for transparent dragging / right-edge resizing at `background = 0.0`, light-mode color application, WezTerm `[terminal] app` handling, and `[startup] layout / preview` behavior.

---

## 2. Upcoming Work

Items are listed in recommended execution order, weighted by **importance**, **relevance to the Windows port**, **effort**, and **risk**. Item numbers reflect priority — they are not strict dependency markers. Each item carries its own dependencies in prose. The next concrete sequence is §2.7 Pane Tabs, then §2.9 Built-in Terminal Pane.

### Phase A — Foundation Backfill

Items in this phase close gaps from the upstream macOS §1 that the Windows port has not yet shipped. They are prerequisites for confident execution of Phase B.

#### 2.1 Test Foundation and CI

Status: **Done.** Landed alongside the introduction of the `Tfx.Core` library. The 31 initial `ArchivePath` cases pass locally and the CI workflow is published as `.github/workflows/build.yml`. Move this section under §1 with a version tag at the next release bump.

Upstream: §1.13 (Test Foundation and CI). The macOS port already shipped this. The Windows port matches it with xUnit + GitHub Actions on `windows-latest` instead of Swift Testing + `xcodebuild` on `macos-latest`.

Goal: catch regressions early and unblock confident refactoring of every later item.

Tasks:

- Add a `Tfx.Tests` project (xUnit). Pure-logic targets first: `ArchivePath`, `MiddleEllipsisTextBlock` (measure logic), `FsHelpers`, `FileItemComparer`, `Loc`.
- Wire `dotnet test` into a `.github/workflows/build.yml` running `windows-latest`, triggered on push, pull request, and manual dispatch.
- Upload test logs as artifacts on failure (7-day retention). Cancel superseded runs on the same ref via `concurrency`.
- Document the test command and project layout in `docs/contributing.md`.

Done when:

- Initial test run passes locally and in CI.
- New public methods on classes covered by tests carry at least one focused test.
- A failing test fails the CI build.

#### 2.2 CSV / JSON / Plain-Text Previews

Status: **Done.** `CsvParser` and `JsonPrettyPrinter` live in `Tfx.Core` with 25 dedicated tests. `MainWindow.xaml` adds a `DataGrid`-based `CsvPreview` overlay. The Source / Rendered toggle now covers `.csv` / `.tsv` (rendered table) and `.json` (rendered pretty-printed text) in addition to Markdown / HTML. `FsHelpers.IsText` was extended to recognise `.toml` / `.yaml` / `.yml` / `.ini` / `.cfg` / `.conf` / `.env` and several developer-text extensions, so they no longer fall through to "no preview". Move this section under §1 with a version tag at the next release bump.

Upstream: §1.11 (CSV / JSON / Text Previews) — already shipped on macOS.

Goal: parity with upstream §1.11 for tabular and structured-text previews.

Tasks:

- `.csv` / `.tsv` → in-app monospaced table preview (`CsvPreview` + a reusable parser). First row treated as a header.
- `.json` → pretty-printed monospaced text preview (`JsonPreview`). Falls back to raw text on parse failure.
- `.toml` / `.yaml` / `.yml` / `.ini` / `.cfg` / `.conf` / `.log` / `.txt` / `.env` use the existing text preview so display works even for extensions the system has no preview handler for.
- Extend the Source / Rendered toggle (currently Markdown / HTML only) to cover CSV / TSV and JSON. Raw-mode preview should hide the per-file info strip in rendered mode, matching the existing Markdown / HTML behavior.

Done when:

- CSV / TSV files render as a table with sortable columns (or at least a fixed table; sorting is optional polish).
- JSON files render pretty-printed; toggle returns the original text.
- Plain-text extensions listed above never fall through to "no preview".

#### 2.3 Multi-Selection Preview

Status: **Done.** When more than one item is selected (excluding the `..` row), the preview pane renders a compact summary in `InfoPreview` showing the total count, combined size, and per-item name / kind / size / modified up to a cap of 8 entries; above that, a "(+N more)" footer indicates the rest. `ImagePreview`, `TextPreview`, `HtmlPreview`, and `CsvPreview` are hidden during multi-selection, and any in-flight preview task is cancelled via the existing `ReplacePreviewToken` path. Move this section under §1 with a version tag at the next release bump.

Upstream: §1.2 (File View and Preview) — "Multiple selected files can be shown side by side in the preview pane." Already shipped on macOS.

Goal: parity with upstream §1.2.

Tasks:

- When more than one item is selected, the preview area shows a compact summary per item (name, kind, size, modified) up to a small cap (e.g. 8). Above the cap, show the count plus the combined size.
- Cancel stale per-item preview work when the selection changes.

Done when:

- Selecting multiple files shows their combined info without spawning per-file content previews.
- The preview clears cleanly on selection-change to a single item or to nothing.

#### 2.4 Subfolder Search

Status: **Done.** Search is **always** a recursive walk of the current folder's subtree; there is no recursive-mode toggle (removed per user feedback). Typing in the search box does not auto-trigger; pressing **Enter** starts the walk, **Esc** clears the box and reloads the real folder listing, and any folder navigation cancels the in-flight search. Matches stream into the active pane's `ObservableCollection` in batches of 50 with the status bar updating every ~120 ms ("Searching: N matches" → "Search complete: N matches"). Results show the relative path in the Name cell while preserving `FullPath` so Open / Reveal still work. Inside zip archives the recursive walk is a no-op (the archive listing is already flat). Move this section under §1 with a version tag at the next release bump.

Upstream: §1.7 (Finder Compatibility and Search) — "Subfolder search supports progress reporting, incremental results, cancellation, and status-line display." Already shipped on macOS.

Goal: parity with upstream §1.7 search behavior.

Tasks:

- Extend the existing `SearchBox` with a toggle (modifier or button) for recursive search.
- Recursive search runs on a background thread with cancellation, incremental results streamed into the view, and a progress indicator in the status bar.
- Cancellation on every navigation, focus change to address bar, or `Esc`.

Done when:

- Recursive search returns first results within ~300 ms on a typical project folder and can be cancelled at any time.
- Switching folders cancels the in-flight search.

#### 2.5 Performance Measurement Infrastructure

Status: **Done.** `Tfx.Core/PerformanceTrace.cs` exposes a static `Begin(label)` / `Measure(label, action)` helper that no-ops when disabled and prints one line per call (`Debug` + console) when on. It's activated by either the `TFX_PERFORMANCE_LOGS=1` environment variable (wins, never resets) or the persisted `AppSettings.ShowPerformanceLogs` flag (toggled via `PerformanceTrace.SetEnabled`). Trace points are wired into `DirectoryLoader.Load`, `PreviewLoader.Load`, `ApplySearchFilter`, `CsvParser.Parse`, and `JsonPrettyPrinter.TryPrettyPrint`. `Tfx.Tests/Benchmarks/PerformanceBenchmarks.cs` adds 7 informational benchmarks (`ArchivePath` × 2, `CsvParser` × 3 sizes, `JsonPrettyPrinter` × 2) that print per-iteration timings via `ITestOutputHelper` and never assert. Move this section under §1 with a version tag at the next release bump.

Upstream: §1.14 (Performance Measurement Infrastructure). The macOS port uses `PerformanceTrace` + a Developer menu toggle + `tfxTests/PerformanceBenchmarks.swift`. The Windows port mirrors this with the same env var name (`TFX_PERFORMANCE_LOGS=1`) and a `Tfx.Tests/Benchmarks/` folder.

Goal: parity with upstream §1.14.

Tasks:

- `PerformanceTrace` static helper that honors a `TFX_PERFORMANCE_LOGS=1` environment variable and a UserDefaults-equivalent setting toggle (Developer menu item: "Show Performance Logs").
- Trace points around directory load, filter/sort, preview load, archive open.
- A small set of benchmarks under `Tfx.Tests/Benchmarks/` that print timings (no assertions) for 1 k / 5 k file-item creation, `DirectoryLoader.Load`, search filter, etc.

Done when:

- Toggling the env var or the Developer menu surfaces timings to the debug output.
- Benchmarks run as part of the test suite without blocking on assertions.

### Phase B — User-Facing Features (Adapted from macOS §2)

#### 2.6 Git Status Indicators

Status: **Done.** `Tfx.Core/GitStatus.cs` implements the `git status --porcelain=v2 --branch --untracked-files=normal --no-renames` parser (untracked, ignored, modified, added, deleted, renamed, copied, conflicted). `src/Services/GitStatusReader.cs` walks for a `.git` ancestor and runs the `git` process on a background thread with cancellation and 8-second timeout; silently disables itself if `git` isn't on `PATH`. `MainWindow.GitStatus.cs` orchestrates per-pane state, cached `_leftGitRoot` / `_rightGitRoot` + `_leftGitStatus` / `_rightGitStatus`, and stamps the new mutable `FileItem.GitStatusText` (now `INotifyPropertyChanged`-aware) onto each row. A new narrow "Git" column appears between Name and Date Modified, and the current branch shows in the status bar as `⎇ name` next to the free-space text. Refresh hooks: Navigate, Reload completion, and the auto-refresh diff path. Folders aggregate to "M" / "?" if any descendant has changes. Move this section under §1 with a version tag at the next release bump.

Upstream: §2.2 (Git Status Indicators) — same plan; `Process.Start` replaces the macOS `Process` API but the porcelain v2 parsing is identical.

Goal: surface Git status next to files inside a Git working copy. Direct port of upstream §2.2.

Tasks:

- Detect the Git root for the current directory via `.git` ancestor walk; cache the result per pane.
- Run `git status --porcelain=v2 --untracked-files=normal` on a background thread when entering a working copy and on `FileSystemWatcher` events.
- Decorate file rows with status badges: `M` modified, `A` added, `?` untracked, `D` deleted, `!` ignored.
- Show the current branch in the status bar (right side, next to free-space text).
- Skip all `git` work in folders not inside a working copy.

Done when:

- Rows in a Git working copy display accurate status badges that refresh on external changes.
- Non-Git folders incur no `git` process startup cost.

#### 2.7 Pane Tabs

Status: **Done in 0.6.4.** Per-pane tabs implemented in `src/Models/PaneTab.cs` + `src/Views/MainWindow/MainWindow.Tabs.cs`: each tab owns its path, back/forward history, and remembered selection (fixing the former global shared-history bug). Tab strip shows when a pane has 2+ tabs; `Ctrl+T` / `Ctrl+W` / `Ctrl+Shift+[` / `Ctrl+Shift+]` plus context-menu "New Tab" / "Open in New Tab". Tab lists persist per pane in `settings.json` (additive keys) and restore on startup, dropping tabs whose folders no longer exist. Closing the right pane's last tab collapses to single-pane; the left pane always keeps one tab. Move this section under §1 with a version tag at the next consolidation pass.

Upstream: §2.3 (Pane Tabs).

Goal: each pane carries multiple folders and switches with the keyboard. Direct port of upstream §2.3.

Tasks:

- Per-pane tab container owning multiple navigation states (path + history + selection memo) and tracking the active one.
- `TabControl`-style header with click to switch, `Ctrl+W` close, `Ctrl+T` new tab at active folder, `Ctrl+Shift+[ / ]` cycle.
- Persist tab list (paths + active index) under new `AppSettings` keys, additive only.
- Folder tree, preview, and search controls follow the active tab.

Done when:

- Each pane can hold multiple tabs that survive relaunch.
- Closing the last tab in a pane is deterministic (decide between "hide pane" and "empty-tab placeholder" during design).
- Keyboard shortcuts work for new / close / next / previous tab.

#### 2.8 Built-in Color Themes

Status: **Partially completed in 0.6.3; remaining work on hold.** User-defined `[colors]` in `config.toml` now drives the live WPF theme resources, Markdown preview CSS, custom chrome colors, selection colors, scrollbar colors, and light-mode palettes. The remaining work is an in-app theme picker and named built-in presets.

Upstream: §2.6 (Built-in Color Themes). The macOS color tokens map directly; XAML `ResourceDictionary` replaces the Swift theme token table.

Goal: ship visible theme variety and eventually expose named presets from the UI. Direct port of upstream §2.6.

Tasks:

- Continue expanding the theme token table only where real UI surfaces still need coverage.
- Promote the documented color samples into named built-in themes if an in-app theme picker is added.
- Add a "Theme" submenu / settings entry that switches the active resource dictionary at runtime.

Done when:

- Switching themes updates the main UI consistently and immediately without restart.
- Missing color tokens in a theme fall back to the default.

#### 2.9 Built-in Terminal Pane

Status: **Next (§2.7 Pane Tabs shipped in 0.6.4).** The external terminal launcher remains supported and stays the default hand-off path, but the built-in terminal pane is the current implementation target now that pane tabs are done. Decision: ConPTY (`CreatePseudoConsole`) self-hosted via P/Invoke, output drawn to a WPF text control, minimal VT-escape handling first. The 0.5.2 settings UI introduced configurable terminal commands, and 0.6.3 adds `config.toml` `[terminal]` support plus active-pane working-directory defaults; those paths should remain intact while the built-in pane is added.

Upstream: §2.4 (Built-in Terminal Pane). Library evaluation differs: SwiftTerm on macOS vs `Microsoft.Terminal.Wpf` / ConPTY samples on Windows.

Goal: collapsible shell pane at the bottom of the window. Aligns with the project's terminal-inspired identity. Direct port of upstream §2.4.

Tasks:

- Evaluate ConPTY-based options before writing custom: `Microsoft.Terminal.Wpf`, [WPF.ConPTY.Terminal samples](https://github.com/microsoft/terminal), or community packages.
- Default shell from `%ComSpec%` / PowerShell preference; working directory follows the active pane (toggleable, default on).
- Commands: toggle terminal pane (`Ctrl+\`` proposed), focus terminal pane, run command on selected files.
- Persist visibility, height, font size in `AppSettings`.

Done when:

- The terminal pane can be toggled on / off through a menu item and a keyboard shortcut.
- Active pane folder changes drive a `cd` in the terminal when the follow-folder setting is on.

#### 2.10 NTFS ACL / Owner Editing

Status: **On hold.** Deferred per user direction; the surface area (UAC restart, take-ownership, ACL edits) is high relative to the typical day-to-day workflow tfx targets.

Upstream: §2.5 (Permissions and Owner Editing). Adapted: NTFS ACL (`System.Security.AccessControl.FileSecurity`) replaces POSIX mode bits; UAC restart-with-`runas` replaces `AuthorizationServices`.

Goal: equivalent of upstream §2.5 "Permissions and Owner Editing", adapted for Windows. NTFS ACLs replace POSIX bits.

Tasks:

- "Properties" sheet showing the current security descriptor in a readable form (owner, primary group, simplified per-principal allow/deny summary).
- Edits apply via `System.Security.AccessControl.FileSecurity` / `DirectorySecurity`.
- Owner / take-ownership operations elevate via a UAC restart-with-`runas` flow or a privileged helper, mirroring the upstream "admin credentials" model.
- Failures (access denied, requires elevation, sealed by parent) surface through the status bar.

Done when:

- A user can grant or revoke read/write on a file they own without elevation.
- Take-ownership operations prompt for elevation cleanly and roll back on cancel.

#### 2.11 Auto-Update

Status: **On hold.** Deferred per user direction; revisit once a regular release cadence (and a signing-key plan) is in place. Until then, distribution stays at "GitHub Releases — direct download".

Upstream: §2.7 (Sparkle Auto-Update). Adapted: **Velopack** (recommended) / `NetSparkle` / `Squirrel.Windows` instead of Sparkle. GitHub Releases delivery replaces the macOS appcast XML.

Goal: in-app updates for the direct-download distribution channel. Adapted from upstream §2.7 (Sparkle) — on Windows the equivalents are **Velopack** (recommended for GitHub Releases), `NetSparkle`, or `Squirrel.Windows`.

Tasks:

- Recommend Velopack. Integrate via NuGet.
- Generate signing keypair; ship the public key in the app, keep the private key offline.
- Publish updates to GitHub Releases with a manifest (`releases.json`).
- "Check for Updates…" menu item plus a setting for automatic checks.

Done when:

- A test release pushed to GitHub Releases can be installed in-app.
- The release process and signing are documented.

### Phase C — Configuration & Extensibility (Depends on Phase B)

#### 2.12 Configuration Foundation (TOML)

Status: **Partially completed in 0.6.3; remaining polish on hold.** The app creates `%APPDATA%\tfx\config.toml` on demand and merges v1 TOML-style overrides for `[font]`, `[colors]`, `[opacity]`, `[startup]`, `[shortcuts]`, `[terminal]`, and `[openWith]` on top of built-in defaults. `settings.json` continues to own window placement, splitter widths, view state, and other runtime UI state. The current parser is a small purpose-built subset instead of a full TOML library; full TOML parsing, split config files, migration policy, and richer error UI remain future work.

Upstream: §2.8 (Configuration Foundation). Adapted: `%APPDATA%\tfx\` replaces `~/Library/Application Support/tfx/`; a full TOML parser can still be introduced if the configuration surface grows beyond the current v1 subset.

Goal: user-editable declarative configuration so later items can build on it. Direct port of upstream §2.8.

Configuration directory:

```text
%APPDATA%\tfx\
```

Possible future layout:

```text
config.toml
themes/*.toml
filetypes.toml
shortcuts.toml
scripts/*.lua
markdown/preview.css
```

Remaining tasks:

- Decide whether to adopt a full TOML parser such as `Tomlyn` once nested tables, arrays, or comments-preserving rewrites become necessary.
- Define migration rules for future `version = N` changes; carry forward at least one prior version's migration code (§3.5).
- Built-in defaults remain in code; TOML overrides are merged on top.
- Keep `settings.json` for window placement / splitter widths / view state; TOML covers declarative configuration only.

Done when:

- The configuration directory is created on demand.
- The app runs with built-in defaults when no configuration files exist.
- TOML loading errors are surfaced clearly to the user.

#### 2.13 Shortcut Organization

Status: **Done for config overrides in 0.6.3.** Shortcut definitions are centralized in the action map and can be overridden through `[shortcuts]` in `config.toml`. User-defined conflicts are reported, and unsupported macOS-style `cmd` / `command` aliases are rejected so Windows documentation stays unambiguous. A visual shortcut editor remains optional future UI work.

Upstream: §2.9 (Shortcut Organization). Direct port. Depends on §2.12.

Done when:

- Existing shortcuts can be reviewed from one action definition list.
- User-defined shortcut conflicts are reported clearly.

#### 2.14 Theme Customization via TOML

Status: **Partially completed in 0.6.3.** `[colors]` in `config.toml` overrides the live theme tokens, including light-mode colors and translucent backgrounds. `docs/configuration.md` and `docs/configuration.ja.md` include three distinctive color samples. Named `themes/*.toml` files and an in-app theme picker remain deferred.

Upstream: §2.10 (Theme Customization via TOML). Direct port. Depends on §2.12 and §2.8.

Done when:

- A user-defined `themes/*.toml` overrides built-in themes and appears in the theme picker.
- Missing tokens fall back to the active built-in default.

#### 2.15 Extension-Based Behavior

Status: **Partially completed in 0.6.3; remaining work on hold.** `[openWith]` supports per-extension external openers with `{path}` substitution. Per-extension preview selection and Lua-backed rule precedence remain future work.

Upstream: §2.11 (Extension-Based Behavior). Direct port. Depends on §2.12.

Done when:

- The default preview / open behavior can be changed per extension.
- Rule precedence (built-in → TOML → Lua) is explicit and documented.

#### 2.16 Lua Extension API

Upstream: §2.12 (Lua Extension API). Adapted: **`MoonSharp`** (managed Lua 5.2 interpreter, NuGet, no native dependency) replaces the macOS Lua-host choice. Depends on §2.12 (config), most useful after §2.13.

Introduced incrementally:

1. Read-only inspection. Lua reads current folder / selection / extension info and returns a value (filter, label, classification).
2. Markdown post-processing. Lua filters Markdown source before conversion or HTML after conversion. Output is sanitized before being shown.
3. Shortcut bindings. Lua callbacks bound to shortcut TOML actions.
4. Open / preview hooks. Lua decides how a file is opened or previewed, within the sandbox.

Restrictions across all steps:

- No file mutation.
- No external command execution.
- Read-only host APIs; sanitized return values.
- Long-running scripts can be detected and stopped.

Done when:

- Each step has its own "done when" gate; step 1 ships before step 2 begins.
- Lua script errors never crash the app.

#### 2.17 Markdown Preview Extensions

Upstream: §2.13 (Markdown Preview Extensions). Direct port. Targets: ruby text, KaTeX / MathJax, Mermaid, custom inline / block syntax, CSS customization. Implementation is straightforward because the existing renderer is already WebView2, so CDN-loaded or bundled scripts can be injected into the HTML template the same way the dark-mode CSS is injected today.

Priority: lower than §2.16. Address once the remaining extension hooks land and concrete user demand surfaces.

### Items Skipped or Replaced

| Upstream | Decision | Reason |
| --- | --- | --- |
| §2.1 macOS Tags | **Skipped** | Windows lacks an OS-level tag system that interoperates with Explorer or other applications. Internal-only virtual tags can be reconsidered after configuration and extension APIs mature. |
| §2.7 Sparkle | **Replaced by §2.11 Velopack** | Windows-native auto-update stack. |
| §2.4 Built-in Terminal Pane | **Planned after §2.7; external launcher remains supported** | `AppSettings.TerminalCommand` / `TerminalArguments` and `[terminal]` in `config.toml` invoke any shell (wt / WezTerm / pwsh / Git Bash / `code -r "{path}"` / …) with `{path}` and env-var expansion. The built-in pane variant is now tracked as the next major item after pane tabs. |
| §2.5 POSIX permissions | **Replaced by §2.10 NTFS ACL** | Different security model. |
| §3.3 distribution (TestFlight / Mac App Store) | **Replaced by GitHub Releases + `winget`** | See §3.3 below. |

---

## 3. Cross-Cutting Concerns

Constraints and policies that apply across every item in §2. These are not feature deliverables; they exist so feature work can refer to them.

### 3.1 Performance Targets

Initial budgets. Revise once §2.5 produces real numbers from typical hardware.

| Path | Target |
| --- | --- |
| Cold launch on a modern Win11 SSD | < 1.0 s |
| Directory load — 1k items | first paint < 100 ms, complete < 300 ms |
| Directory load — 10k items | first paint < 200 ms, complete < 1.0 s |
| External change → auto-refresh visible | < 350 ms (150 ms watcher debounce + load) |
| Typical session memory | < 250 MB (WebView2 process inflates this when active) |

### 3.2 Reliability and Quality Gates

- No data loss from drag / drop / move operations. Same-name conflicts must surface the resolver (currently `FsHelpers.NextAvailablePath`).
- Permission and read errors are surfaced through the status bar, never swallowed.
- CI build + tests must pass before any release tag is cut.
- Every new public mutator on long-lived state (e.g., `_pinned`, `_settings`, navigation history) ships with at least one focused test once §2.1 lands.
- No new WPF binding errors or compiler warnings introduced.

### 3.3 Distribution Plan

| Channel | Notes |
| --- | --- |
| Local `dotnet run` | Current state. Development only. |
| GitHub Releases (direct download) | `dotnet publish -r win-x64 --self-contained` produces `Tfx.exe`. `scripts\build-release.ps1` already wraps this. |
| GitHub Releases + Velopack (auto-update) | Blocked by §2.11 (currently on hold). Stable + beta channels via separate manifest URLs once unblocked. |
| Microsoft Store | Out of scope for now. MSIX packaging is feasible later but not committed. |
| `winget` manifest | Trivial once Releases is established; revisit after §2.11 ships. |

### 3.4 Windows and Hardware Compatibility

- Target framework: `net10.0-windows`. Re-evaluate lowering when GitHub Actions support `net10` reliably for distribution builds.
- Primary target: Windows 11. Windows 10 22H2 supported as long as WebView2 Runtime is present.
- x64 first. ARM64 build added when there's demand (the WPF + .NET 10 toolchain supports it out of the box).
- Locales: English (source) and Japanese are actively maintained. Additional locales accepted by PR with translation review.
- WebView2 Runtime is preinstalled on Windows 11; on Windows 10 the app falls back to source view if the runtime is missing.

### 3.5 Data and Configuration Migration

- `AppSettings` (JSON) is additive: new fields ship with defaults; existing fields are never removed without an explicit migration step.
- The JSON file lives at `%APPDATA%\tfx\settings.json`. Document every persisted key in `README.md` (current behavior) and migrate documentation to `docs/detailed-design.md` once that file exists.
- `config.toml` v1 (§2.12) is additive: unknown keys are ignored, supported keys override built-in defaults, and runtime state stays in `settings.json`.
- Future TOML versions carry a top-level `version = N`. The loader migrates older versions forward and keeps at least one prior version's migration code on hand once the schema advances beyond v1.
- Pinned folders, window state, and other user data are read-merge-write: never destructively rewritten on load when fields are missing.

---

## 4. Performance Measurement (On-Demand)

This becomes a reactive checklist once §2.5 ships. Until then, ad-hoc measurement via `Stopwatch` is acceptable.

- Reproduce with `TFX_PERFORMANCE_LOGS=1` (env var) or the **Developer → Show Performance Logs** toggle (in-app, after §2.5).
- Compare against the §3.1 targets.
- For repeatable scenarios, add a corresponding benchmark to `Tfx.Tests/Benchmarks/`.
- Land the fix with a regression test where feasible.

---

## 5. Documentation Work

Status: **0.6.3 docs are aligned; new documentation projects are on hold.**

Tasks:

- Add `docs/contributing.md` with the test, benchmark, CI, and (eventually) release commands.
- Add `docs/detailed-design.md` describing the partial-class layout, settings schema, archive virtual-path scheme, and drag-drop event flow.
- Add `docs/code-organization.md` describing the `Pane` abstraction, `MainWindow.*.cs` split rules, and where each concern lives.
- Keep `README.md` and `README.ja.md` aligned for feature behavior, version labels, and release notes links.
- Keep `CHANGELOG.md` aligned with `Tfx.csproj` `<Version>` on every version bump.
- Keep `CHECKLIST.md` aligned with the current feature set (it currently lists the manual smoke-test items used before each release).
- Keep `docs/configuration.md` and `docs/configuration.ja.md` aligned with the implemented `config.toml` schema, including shortcut grammar, terminal examples, startup behavior, and color samples.

Done when:

- The roadmap, detailed design, and README do not contradict each other.
- Users can start basic customization from the configuration examples.
- New contributors can run tests and benchmarks and understand the release process from the documentation.

---

## 6. Cross-Reference Index

Quick lookup between this Windows roadmap and the upstream macOS roadmap [`fukuyori/tfx/docs/development-roadmap.md`](https://github.com/fukuyori/tfx/blob/main/docs/development-roadmap.md).

### 6.1 Completed Work

| Windows | Upstream | Notes |
| --- | --- | --- |
| §1.1 Through 0.3.0 (Foundation) | §1.1, §1.2, §1.3, §1.5, §1.6, §1.7, §1.8 | Mixed coverage; foundation features built across multiple upstream sections. |
| §1.2 0.3.1 (Live Refresh, Open With, Zip) | §1.5, §1.9 | `FileSystemWatcher` ↔ `DispatchSource` `DirectoryWatcher`. |
| §1.3 0.3.2 (Responsiveness) | §1.1 | `LoadDrives` background pass is Windows-specific. |
| §1.4 0.4.0 (Code Organization) | §1.4 | `MainWindow.*.cs` partials ↔ Swift feature folders. |
| §1.5 Windows-specific | — | WebView2 / `SHOpenWithDialog` / Zip drag-out / `MiddleEllipsisTextBlock`. |
| §1.6 0.6.3 (Configuration, Theme Surfaces, Custom Chrome) | §2.4, §2.6, §2.8, §2.9, §2.10, §2.11 | `config.toml` v1, color / opacity tokens, startup controls, external terminal defaults, shortcut overrides, and custom transparent chrome. |

### 6.2 Upcoming Work

| Windows | Upstream | Mapping |
| --- | --- | --- |
| §2.1 Test Foundation and CI | §1.13 | xUnit + GitHub Actions `windows-latest` ↔ Swift Testing + `xcodebuild` on `macos-latest`. |
| §2.2 CSV / JSON / Plain-Text Previews | §1.11 | Direct port. |
| §2.3 Multi-Selection Preview | §1.2 (partial) | Direct port. |
| §2.4 Subfolder Search | §1.7 (partial) | Direct port. |
| §2.5 Performance Measurement Infrastructure | §1.14 | `TFX_PERFORMANCE_LOGS` env var name preserved. |
| §2.6 Git Status Indicators | §2.2 | Direct port; `Process.Start` for `git status`. |
| §2.7 Pane Tabs | §2.3 | **Next.** Direct port. |
| §2.8 Built-in Color Themes | §2.6 | **Partially completed in 0.6.3; remaining work on hold.** Runtime color tokens and light-mode samples shipped; in-app theme picker remains. |
| §2.9 Built-in Terminal Pane | §2.4 | **Planned after §2.7.** Configurable external launcher remains supported. |
| §2.10 NTFS ACL / Owner Editing | §2.5 | **On hold.** NTFS ACL ↔ POSIX bits; UAC `runas` ↔ `AuthorizationServices`. |
| §2.11 Auto-Update | §2.7 | **On hold.** Velopack / `NetSparkle` ↔ Sparkle 2. |
| §2.12 Configuration Foundation (TOML) | §2.8 | **Partially completed in 0.6.3; remaining polish on hold.** `%APPDATA%\tfx\config.toml` v1; full parser / split files / migrations remain. |
| §2.13 Shortcut Organization | §2.9 | **Completed for config overrides in 0.6.3.** Optional visual editor remains. |
| §2.14 Theme Customization via TOML | §2.10 | **Partially completed in 0.6.3; remaining work on hold.** `[colors]` tokens and samples shipped; named theme files / picker remain. |
| §2.15 Extension-Based Behavior | §2.11 | **Partially completed in 0.6.3; remaining work on hold.** `[openWith]` shipped; preview hooks and Lua precedence remain. |
| §2.16 Lua Extension API | §2.12 | `MoonSharp` ↔ upstream Lua-host choice. |
| §2.17 Markdown Preview Extensions | §2.13 | WebView2-based; CDN / bundled scripts injected into the existing HTML template. |
| (skipped) | §2.1 macOS Tags | No OS-level Windows equivalent. |

### 6.3 Cross-Cutting Concerns

| Windows | Upstream | Mapping |
| --- | --- | --- |
| §3.1 Performance Targets | §3.1 | Same structure; absolute numbers re-derived after §2.5. |
| §3.2 Reliability and Quality Gates | §3.2 | Direct port. |
| §3.3 Distribution Plan | §3.3 | TestFlight / Mac App Store removed; GitHub Releases + Velopack + `winget` added. |
| §3.4 OS and Hardware Compatibility | §3.4 | Windows 11 primary / Windows 10 22H2 supported; x64 first; WebView2 Runtime caveat. |
| §3.5 Data and Configuration Migration | §3.5 | `settings.json` for UI state + `config.toml` v1 for declarative settings. Future TOML migrations remain planned. |
