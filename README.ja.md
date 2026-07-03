# tfx for Windows

**Terminal-inspired interface File eXplorer**
読み方: **Tafix**
Version: 0.9.0

[English](README.md) | 日本語

[![License: Apache 2.0](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](LICENSE)

tfx for Windows は、キーボード操作を重視した Windows 向けのダークテーマファイルエクスプローラーです。macOS 版 tfx (<https://github.com/fukuyori/tfx>) の C# / WPF ポートです。

- Repository: <https://github.com/fukuyori/tfx-for-windows>
- Author: fukuyori (<self@spumoni.org>)
- Release notes: [CHANGELOG.md](CHANGELOG.md)
- Roadmap: [docs/roadmap.md](docs/roadmap.md)
- 設定ガイド: [docs/configuration.ja.md](docs/configuration.ja.md)
- Contributing: [docs/contributing.md](docs/contributing.md)

---

## 主な機能

- 左右 2 ペイン、または 1 ペイン表示。各ペインは独立した履歴を持ちます。
- フォルダーツリー（ツールバーで表示/非表示の切替・全折りたたみボタン。シングルクリックでファイル一覧に反映、ダブルクリックで展開/折りたたみ）、ピン留めフォルダー、編集可能なアドレスバー、クリック可能な breadcrumb。
- **Details** と **Icons** の 2 表示モード。ファイル種別ごとのテーマ色に追従する単色アイコン。
- New File（`.txt`）/ New Folder は Explorer 風のインライン命名（名前を入力して Enter、または一覧の空き場所クリックで確定）、インライン rename、Windows の修飾キーに沿ったドラッグ & ドロップ（Explorer 風の半透明ドラッグプレビュー付き）、`.lnk` ショートカット作成。
- コピー / 移動 / 貼り付けは Windows シェル経由。時間のかかる処理では標準の進捗ダイアログ（残り時間・速度・キャンセル）が表示され、名前衝突は置換 / スキップ / 両方残すのネイティブ確認（同一フォルダーへのコピーは「- コピー」を作成）。一覧はその場で差分更新され、選択とフォーカスを保持します。
- クリップボードにファイルが無い場合、貼り付けで内容をファイル化: 表計算 / CSV → `.csv`、画像 → `.png`（スキャン PDF 等の DIB/DIBV5 も）、URL → `.url`、リッチテキスト → `.rtf`、テキスト → `.txt`。右クリックの「形式を選んで貼り付け」で形式（HTML を含む）を選択可能。
- zip 圧縮 / 展開、`.zip` 内部の読み取り専用ブラウズ、zip 内ファイルの open / drag out。
- Windows 11 風の右クリックメニュー、"Open with..." ダイアログ、ソート可能な列、列の表示 / 順序カスタマイズ（ヘッダーのドラッグで並べ替え＝保存・両ペイン同期）。
- 画像 / テキストプレビュー、Markdown / HTML / CSV / TSV / JSON のレンダリング表示とソース表示切替。
- 複数選択時の要約プレビュー。
- 検索ボックスで Enter を押す recursive subfolder search。
- Git 作業ツリー内では **Git** 列に `M` / `A` / `?` などのバッジを表示し、ステータスバーに branch を表示。
- USB / リムーバブルドライブの追加・削除を検出してフォルダーツリーを更新。
- `%APPDATA%\tfx\config.toml` による tfx 互換のフォント、カラー、ショートカット、起動、ターミナル、拡張子別 open-with 設定。
- `config.toml` によるライトテーマ、透過テーマ、カスタムウィンドウ chrome。
- ターミナルは未設定でもアクティブペインのフォルダーで開きます。WezTerm / Windows Terminal / PowerShell / pwsh には既定の cwd 引数を補います。
- 内蔵ターミナルペイン（WebView2 上の xterm.js、`Ctrl+J` で開閉）: ウィンドウ下部にドッキングする本物の ConPTY シェル。シェル / フォント / 配色は `config.toml [terminal]` で設定でき、ヘッダーの `^C` 中断ボタン、コピー / 貼り付け、ファイルペインからのドラッグでパス入力に対応。閉じてもセッションは生き続け、再度開くと（スクロールバックとペイン高さも）そのまま再開します。終了は `exit` などでシェル自体を終了したときのみです。
- UI 言語は OS の UI 言語に合わせて日本語 / 英語を切り替えます。
- パス、ピン留め、列設定、表示モードなどの状態を自動保存します。

---

## キーボード

| Key | Action |
| --- | --- |
| `Enter` | 選択ファイルを開く / 選択フォルダーへ入る |
| `Backspace` | 親フォルダーへ移動 |
| `Alt + Left` / `Alt + Right` | 戻る / 進む |
| `Alt + Up` / `Backspace` | 親フォルダーへ移動 |
| `F2` | インライン rename |
| `Delete` | Recycle Bin へ移動 |
| `Shift + Delete` | 完全削除 |
| `Ctrl + C` / `Ctrl + X` / `Ctrl + V` | Copy / Cut / Paste |
| `Ctrl + Shift + V` | クリップボードのテキストを `.txt` で貼り付け |
| `Ctrl + A` | すべて選択 |
| `Ctrl + Shift + N` | 新規フォルダー |
| `Ctrl + N` | 新規ファイル |
| `Ctrl + K` | zip 圧縮 |
| `Ctrl + Shift + E` | zip 展開 |
| `F5` | 再読み込み |
| `Ctrl + F` | 検索ボックスへフォーカス |
| `Ctrl + L` / `F4` | アドレスバー編集 |
| `Tab` / `Shift + Tab` | split view 内で反対側のペインへフォーカス |
| `Shift + ↑` / `Shift + ↓` | 選択範囲を拡張（範囲選択） |
| `Ctrl + 1` | ファイル一覧へフォーカス移動 |
| `Ctrl + 2` | ターミナルペインへフォーカス移動（非表示なら開く） |
| `Ctrl + Shift + T` | 現在フォルダーでターミナルを開く |
| `Ctrl + Shift + .` | 隠しファイル表示切替 |
| `Ctrl + \` | split pane 表示切替 |
| `Ctrl + B` | フォルダーツリー（サイドバー）表示切替 |
| `Ctrl + Shift + B` | フォルダーツリーをすべて折りたたむ |
| `Ctrl + Shift + P` | preview pane 表示切替 |
| `Ctrl + Shift + R` | レンダリング表示／ソース表示の切替（Markdown / HTML / CSV / JSON プレビュー時） |
| `Ctrl + Shift + I` | 現在のプレビューで外部画像を読み込む（表示可能なとき） |
| `Ctrl + Shift + X` | 左右ペイン入れ替え |
| `Esc` | rename / search / address edit のキャンセル |

---

## ウィンドウ操作

透過表示のため、標準タイトルバーの代わりにアプリ内のカスタム chrome を使います。

- 上部ツールバー中央の空き領域をドラッグするとウィンドウを移動できます。
- 上部ツールバー中央の空き領域をダブルクリックすると最大化 / 復元します。
- 右上のボタンで最小化、最大化 / 復元、閉じる操作ができます。
- ウィンドウ右端をドラッグすると幅を変更できます。

---

## コマンドラインオプション

```
tfx [options] [folder]
```

| オプション | ロング形式 | 動作 |
| --- | --- | --- |
| `-h` | `--help` | ヘルプを表示して終了 |
| `-1` | `--single` | 単一ペインで起動 |
| `-2` | `--split` | 分割（2ペイン）で起動 |
| `-r` | `--restore` | 保存済みレイアウトを復元 |
| `-p` | `--preview` | プレビューペインを表示 |
| `-P` | `--no-preview` | プレビューペインを非表示 |
| `-t` | `--terminal` | 内蔵ターミナルを表示 |
| `-T` | `--no-terminal` | 内蔵ターミナルを非表示 |
| `-g G` | `--geometry=G` | ウィンドウ位置/サイズ `[幅x高さ][+X+Y]`（DIP、`-X`/`-Y` は右端/下端基準）。例 `1200x800+100+50` |

末尾の `[folder]` は左ペインに開くフォルダ（`~` と `%VAR%` 展開対応）。短縮フラグは結合可（例 `-2Pt`）。コマンドラインオプションは `config.toml [startup]` や保存済みセッションより優先されます。ジオメトリ指定時は最大化を解除して配置します。

タブ構成は既定ではセッション内だけの状態です。次回起動時は、復元されたペインパスから各ペイン 1 タブで始まります。`config.toml` の `[startup] leftFolders` / `rightFolders` を指定した場合だけ、その一覧を起動時タブとして反映します。

例 — 分割・プレビュー非表示・ターミナル表示で `~/Downloads` を左ペインに:

```
tfx -2 -P -t ~/Downloads
```

---

## 設定

ユーザー編集用の設定ファイルは `%APPDATA%\tfx\config.toml` です。ファイルが存在しない場合は起動時に作成されます。`version = 1` と tfx のセクション名を使いつつ、ショートカットは Windows で自然な `ctrl`、`alt`、`shift` 表記に統一しています。

詳しくは [docs/configuration.ja.md](docs/configuration.ja.md) を参照してください。

主な設定:

- `[font]`: UI / 等幅フォントとサイズ。ペイン別（`fileList` / `preview` / `terminal` / `folderTree`、各 `*Size`）に上書き可能で、未指定は `mono` にフォールバック。
- `[colors]`: ダーク / ライトテーマ、選択色、入力欄、スクロールバー、Markdown preview CSS など。
- `[opacity]`: 透過ウィンドウ面と非アクティブペインの透明度。
- `[startup]`: 起動時の single / split、preview pane・ターミナルペイン・フォルダーツリーの表示 / 非表示 / 復元、`leftFolders` / `rightFolders` による明示的な起動時タブ。
- `[terminal]`: 外部ターミナル。未設定でもアクティブペインのフォルダーで開きます。
- `[openWith]`: 拡張子ごとの起動アプリ。

ウィンドウ位置、最後に開いたフォルダー、ピン留め、列設定などのセッション状態は `%APPDATA%\tfx\settings.json` に自動保存されます。初期化したい場合は該当ファイルを削除してください。

---

## ビルド

.NET 10 SDK と Windows 10 / 11 が必要です。

```powershell
dotnet build
```

### 実行時の前提: Microsoft Edge WebView2 ランタイム

内蔵ターミナルペインと Markdown / HTML プレビューは WebView2 で描画するため、**Microsoft Edge WebView2 ランタイム**が必要です。Windows 11 および最新の Windows 10 にはプリインストールされていますが、クリーンな Windows 10 では入っていないことがあります。未インストールの場合、ターミナルを開くとステータスバーに案内が表示され、プレビューはソース表示にフォールバックします。Evergreen ランタイムを <https://go.microsoft.com/fwlink/p/?LinkId=2124703>（または <https://developer.microsoft.com/microsoft-edge/webview2/>）からインストールしてください。

実行:

```powershell
dotnet run -- "C:\path\to\folder"
```

リリース成果物を作成:

```powershell
.\scripts\build-release.ps1
```

出力先は `artifacts\release\tfx-for-windows-<version>-win-x64\Tfx.exe` です。

### パッケージング

以下の2スクリプトは、上記でビルド済みの `Tfx.exe` を利用します（先に `build-release.ps1` を実行）。バージョンは `Tfx.csproj` から読み取ります。

```powershell
# ポータブル ZIP -> artifacts\release\tfx-for-windows-<version>-win-x64-portable.zip
.\scripts\build-zip.ps1

# インストーラー -> artifacts\release\tfx-for-windows-<version>-setup.exe
.\scripts\build-installer.ps1
```

ZIP は単一のトップレベルフォルダーに `Tfx.exe`・`LICENSE`・`NOTICE`・README 類を格納します。インストーラーは [Inno Setup 6](https://jrsoftware.org/isdl.php)（`winget install JRSoftware.InnoSetup`）で作成します。`ISCC.exe` が `PATH` や既定の場所に無い場合は `-IsccPath` で指定してください。インストーラーはスタートメニュー（任意でデスクトップ）ショートカットとアンインストーラーを追加し、`Program Files\tfx` にインストールします。

---

## メモ

- Delete 系操作は既定で Recycle Bin へ移動します。完全削除は `Shift + Delete` です。
- Markdown / HTML preview は WebView2 を使います。JavaScript は無効化され、Markdown は Markdig の `.DisableHtml()` と CSP で保護されます。
- PDF preview は disk cache、shell cache、外部 `pdftoppm`、`Windows.Data.Pdf`、shell thumbnail provider の順に試します。
- Git 連携には `git` が `PATH` 上に必要です。見つからない場合、Git 列と branch 表示は静かに無効化されます。
- **「ターミナルのフォルダーへ同期」の cwd 取得:** tfx は**プロンプトに文字を出さずに**カレントディレクトリを取得します。内蔵の **PowerShell** には、起動時に**非表示の `OSC 9;9` cwd レポーターを自動注入**します（`-EncodedCommand` で渡し、プロファイル読み込み後に実行して oh-my-posh / Starship / 独自プロンプトを内側で呼び、FileSystem の場所のときだけ送出）。そのシーケンスは tfx が画面から除去するので何も表示されません。加えて、各種シェル統合（Windows Terminal、oh-my-posh、Starship 等）が出す `OSC 7` / `OSC 9;9` も利用します。wtmux 使用時は wtmux に問い合わせます（下記）。これらが無い場合（`cmd` や cwd を出さないカスタムシェル等）のみ、可視の `[tfx:cwd]…[tfx:end]` コマンドにフォールバックします。移動は返ったパスが実在ディレクトリのときのみ行います。
- **ターミナルで wtmux を使う場合の「ターミナルのフォルダーへ同期」:** 内蔵ターミナルで [wtmux](https://github.com/fukuyori/wtmux) を使用しているとき、tfx は wtmux からカレントパスを取得します（`display-message -p '#{pane_current_path}'`）。wtmux がパスを把握できるよう、**prompt hook を有効化**してください。wtmux の `config.toml` で `cwd_prompt_hook = true` を設定するか、起動時に `wtmux -P on` を実行します。hook が無効だと wtmux は起動時ディレクトリを返すため、同期先が正しくなりません。なお prompt hook はシェルの prompt をラップして毎回 `OSC 9;9`（cwd 通知）を送出します。PowerShell / cmd には効きますが `wsl.exe` などのカスタムシェルには効かず、prompt を直接上書きするツールや raw escape を記録するツールと干渉する可能性があります（詳細は wtmux 側ドキュメント参照）。tfx 側は返ってきたパスが実在ディレクトリのときのみ移動するため、ファイルシステム外や古いパスは無視され、誤遷移は起きません。
- zip 内部は読み取り専用です。zip 内での rename / delete / paste / new file などは無効です。

---

## License

Copyright 2026 fukuyori

Licensed under the Apache License, Version 2.0. See [LICENSE](LICENSE) and [NOTICE](NOTICE).
