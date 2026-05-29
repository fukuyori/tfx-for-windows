# tfx for Windows 設定

[English](configuration.md) | 日本語

tfx for Windows はユーザーが編集できる設定を次の場所に保存します。

```text
%APPDATA%\tfx\
```

メインのユーザー編集設定ファイルは `config.toml` です。ファイルがない場合、tfx は起動時に既定の `config.toml` を作成します。ウィンドウ位置、最後に開いたパス、ピン留めフォルダー、列設定などのセッション状態は、従来どおり `settings.json` に自動保存されます。

## 互換表記

`config.toml` は macOS 版 tfx の `version = 1` と同じセクション名を受け付けます。Windows 版ではショートカット内の `cmd` / `command` を `Ctrl` として解釈します。そのため、macOS 版の例にある次のような表記をそのまま使えます。

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

Windows らしい `ctrl` / `control`、`shift`、`alt` も使えます。

## 対応セクション

現在対応しているセクションは `[font]`、`[colors]`、`[opacity]`、`[shortcuts]`、`[startup]`、`[terminal]`、`[openWith]` です。未対応キーは無視されます。

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

`[terminal] app` は Windows の実行ファイル名、絶対パス、またはアプリ実行エイリアスを指定します。`arguments` は Windows 版の拡張キーで、`{path}` が現在のフォルダーに置換されます。

`[openWith]` のキーは先頭のドットを除いた拡張子です。値には実行ファイル名、絶対パス、またはアプリ実行エイリアスを指定します。

`[colors]` は macOS 版 tfx のセマンティック名を受け付けます。Windows 版では現在の WPF テーマに近いリソースへ割り当てます。特に `fileListBackground`、`fileForeground`、`directoryForeground`、`secondaryForeground`、`titleBarBackgroundActive`、`titleBarBackgroundInactive`、`paneBorderInactive`、`paneBorderKeyboardTarget` が反映されます。
