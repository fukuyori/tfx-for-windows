# Changelog

## 0.2.2

- Show the current version at the right edge of the status bar.
- Right-align Size values in Details view and add right-side spacing between list columns.
- Allow direct mouse resizing for the Name column while keeping the other Details columns fixed.
- Keep DataGrid header mouse handling from interfering with Name-column resizing.

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
