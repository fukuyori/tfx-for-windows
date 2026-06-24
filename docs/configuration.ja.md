# tfx for Windows 設定

[English](configuration.md) | 日本語

tfx for Windows はユーザーが編集できる設定を次の場所に保存します。

```text
%APPDATA%\tfx\
```

メインのユーザー編集設定ファイルは `config.toml` です。存在しない場合は起動時に作成されます。既存ファイルは上書きしません。

ウィンドウ位置、最後に開いたパス、ピン留めフォルダー、列設定、表示モードなどのセッション状態は、従来どおり `settings.json` に自動保存されます。手で編集する設定は `config.toml`、アプリが保存する状態は `settings.json` という扱いです。

## 現在の対応範囲

`config.toml` は次の項目に対応しています。

- トップレベルの `version = 1`
- `[font]`
- `[colors]`
- `[opacity]`
- `[startup]`
- `[shortcuts]`
- `[terminal]`
- `[openWith]`

TOML は小さなサブセットだけを受け付けます。

- `[section]` 形式のテーブル
- `key = value` 形式の代入
- ダブルクォート文字列
- `leftFolders` / `rightFolders` など起動時フォルダー / タブ一覧用の文字列配列
- 数値のフォントサイズと透明度
- `"#RRGGBB"` 形式のカラー
- クォート外の `#` コメント

未対応セクションと未対応キーは無視されます。不正な値はクラッシュさせず、組み込み既定値にフォールバックしてステータス警告を表示します。

## 既定ファイル

新規環境では、Windows 版として自然なショートカット表記のファイルを作成します。

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
pasteAsText = "ctrl+shift+v"
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

## キー

### `version`

必須のトップレベル整数です。

```toml
version = 1
```

対応している値は `1` のみです。

### `[font]`

アプリ全体のフォントファミリーと基準サイズを設定します。

```toml
[font]
ui = "system"
mono = "monospace"
size = 13
# 任意のペイン別オーバーライド（ファミリーおよび/またはサイズ。グローバル mono を上書き）:
# fileList = "monospace"
# fileListSize = 12
# preview = "monospace"
# previewSize = 12
# terminal = "monospace"
# terminalSize = 12
# folderTree = "monospace"
# folderTreeSize = 12
```

| キー | 型 | 既定値 | 説明 |
| --- | --- | --- | --- |
| `ui` | string | `"system"` | ツールバー、ヘッダー、ダイアログ風 UI などの UI フォント。 |
| `mono` | string | `"monospace"` | ファイル一覧・フォルダーツリー・プレビューの等幅フォント。下のペイン別キーで上書き。 |
| `size` | number | `13` | 基準フォントサイズ。範囲は `8` から `40` です。 |
| `fileList` / `fileListSize` | string / number | (mono / `size`) | ファイル一覧ペインのフォント / サイズ。 |
| `preview` / `previewSize` | string / number | (mono / `size`) | テキスト / CSV プレビューのフォント / サイズ。 |
| `terminal` / `terminalSize` | string / number | (mono / ペインサイズ) | 内蔵ターミナルペインのフォント / サイズ。`[terminal] font` / `fontSize` が指定されていればそちらが優先。 |
| `folderTree` / `folderTreeSize` | string / number | (mono / `size`) | フォルダーツリーのフォント / サイズ。 |

Windows では `"system"` は `Segoe UI, Yu Gothic UI, Meiryo`、`"monospace"` は `Cascadia Mono, Consolas, Yu Gothic UI` に対応します。それ以外の文字列は WPF のフォントファミリー名として扱います。

### `[colors]`

セマンティックカラーを上書きします。値は `"#RRGGBB"` 形式です。

```toml
[colors]
fileListBackground = "#000301"
fileForeground = "#CFFFCF"
directoryForeground = "#6FFF80"
```

現在の Windows 版で主に反映されるキー:

| キー | 現在の反映先 |
| --- | --- |
| `fileListBackground` | メインパネル / 一覧背景。 |
| `headerBackground` | 上部ツールバー、ステータスバー、アプリ背景のフォールバック。 |
| `inputBackground` | パス欄、テキストボックス、スクロールトラック、テキスト / CSV preview 背景。 |
| `fileListRowAlternate` | ファイル一覧と CSV preview の交互行。 |
| `fileListRowHovered` | ツールバー、メニュー、ツリーなどの hover 背景。 |
| `fileListRowDropTarget` | `fileListRowHovered` 省略時の hover フォールバック。 |
| `fileListRowSelected` | 選択行、選択アイコン、選択済み toggle、アクティブペイン背景のフォールバック。 |
| `fileListRowSelectedForeground` | 選択行と選択アイコンの文字色。省略時は選択背景から黒 / 白を自動選択します。 |
| `fileForeground` | 主要テキスト。 |
| `directoryForeground` | アクセント色のフォールバック。 |
| `secondaryForeground` | 補助テキスト。 |
| `headerForeground` | 補助テキストのフォールバック。 |
| `titleBarBackgroundActive` | アクティブファイルペイン背景。 |
| `titleBarBackgroundInactive` | 非アクティブファイルペイン背景。 |
| `paneBorderInactive` | 通常の境界線。 |
| `paneBorderActive` | フォーカス境界線のフォールバック。 |
| `paneBorderKeyboardTarget` | フォーカス境界線。 |
| `splitHandleActive` | アクセント色のフォールバック。 |
| `folderTreeSelectedInactive` | 非アクティブなツリー選択背景。 |
| `disabledForeground` | 無効状態のメニュー / コントロール文字色。 |
| `scrollbarThumb` | スクロールバーのつまみ。 |
| `scrollbarThumbHovered` | hover 中のスクロールバーつまみ。 |
| `scrollbarThumbDragging` | ドラッグ中のスクロールバーつまみ。 |

macOS 版 tfx の他のカラーキーもパーサーは受け付けますが、専用 WPF 要素への接続はまだ一部未実装です。

### `[opacity]`

値は `0` から `1` の数値です。

```toml
[opacity]
background = 0.92
inactivePane = 0.5
disabledItem = 0.45
```

| キー | Windows 版での動作 |
| --- | --- |
| `background` | 透明 WPF ウィンドウ面に反映され、アプリ背景越しに背後の内容が見えるようになります。 |
| `inactivePane` | 非アクティブなファイルペイン面に反映されます。省略時は `background` を使います。 |
| `disabledItem` | tfx 互換のため受け付けます。WPF の無効状態の透明度は、現時点ではアプリ側スタイルの値を使います。 |

`background = 0.0` の場合でも、カスタムタイトル / ドラッグ領域と右端リサイズ領域にはほぼ見えないヒットテスト面を残すため、ウィンドウ移動と幅変更は可能です。

### `[startup]`

起動時のペイン構成を設定します。

```toml
[startup]
layout = "split"
preview = "show"
leftFolders = ["~/source", "~/Downloads"]
leftActiveTab = 0
rightFolders = ["~/Documents", "~/Desktop"]
rightActiveTab = 0
```

| 値 | 動作 |
| --- | --- |
| `"single"` | 1 ペインで起動します。 |
| `"split"` | 2 ペインで起動します。 |
| `"restore"` | `settings.json` の保存済み single / split 状態を使います。 |

`preview` は起動時の preview pane を設定します。

| 値 | 動作 |
| --- | --- |
| `"show"` | preview pane を表示して起動します。 |
| `"hide"` | preview pane を非表示で起動します。 |
| `"restore"` | `settings.json` の保存済み preview pane 状態を使います。 |

`terminal` は内蔵ターミナルペインの起動時状態を `"show"` / `"hide"` / `"restore"` で指定します。コマンドラインの `-t` / `-T` が優先されます。

`folderTree` は左サイドバー（ピン留め＋フォルダーツリー）の起動時状態を `"show"` / `"hide"` / `"restore"` で指定します。

タブ構成は既定ではセッション内だけの状態です。通常は、復元されたペインパスから各ペイン 1 タブで起動します。`config.toml` にフォルダー一覧を明示した場合だけ、その一覧を起動時タブとして使います。

`leftFolder` / `rightFolder` は起動時タブを 1 つ指定します。

```toml
[startup]
layout = "split"
leftFolder = "~/source"
rightFolder = "~/Downloads"
```

`leftFolders` / `rightFolders` は複数の起動時タブを指定します。存在しないフォルダーはスキップされます。`leftActiveTab` / `rightActiveTab` は 0 始まりのアクティブタブ番号です。省略時、または範囲外の場合は最初の有効なタブを使います。

```toml
[startup]
layout = "split"
leftFolders = ["~/source", "~/Downloads"]
leftActiveTab = 1
rightFolders = ["~/Documents", "~/Desktop"]
rightActiveTab = 0
```

`leftTabs` / `rightTabs` は `leftFolders` / `rightFolders` の別名として受け付けます。

パスには絶対パス、`%USERPROFILE%\Downloads` のような環境変数付きパス、`~` 付きユーザーパスを指定できます。

`geometry` は起動時のウィンドウサイズ/位置を X11 形式 `[幅x高さ][+X+Y]` で指定します（DIP、オフセット先頭の `-` は右端/下端基準）。指定すると最大化を解除して配置します。コマンドラインの `-g` / `--geometry` が優先されます。

```toml
[startup]
geometry = "1200x800+100+50"
```

### `[shortcuts]`

ショートカットは Windows 版の自然な修飾キー名で書きます。

```toml
[shortcuts]
reload = "f5"
goBack = "alt+left"
goForward = "alt+right"
goUp = "alt+up"
openTerminal = "ctrl+shift+t"
```

対応している修飾キー:

| トークン | 意味 |
| --- | --- |
| `ctrl`, `control` | Ctrl |
| `shift` | Shift |
| `alt` | Alt |

対応しているキー:

| トークン | 意味 |
| --- | --- |
| 1 文字の英数字 | そのキー |
| `.`, `,`, `/`, `-`, `=`, `[`, `]`, `backslash` | 記号キー |
| `up`, `down`, `left`, `right` | 矢印キー |
| `escape`, `esc` | Escape |
| `delete`, `backspace` | Delete / Backspace |
| `return`, `enter` | Enter |
| `tab` | Tab |
| `space` | Space |
| `f1` から `f24` | ファンクションキー |

主なアクションキー:

| キー | 既定値 | 動作 |
| --- | --- | --- |
| `reload` | `f5` | アクティブペインを再読み込み。 |
| `openTerminal` | `ctrl+shift+t` | 外部ターミナルを現在フォルダーで開く。 |
| `togglePreview` | `ctrl+shift+p` | preview pane 表示切替。 |
| `toggleFolderTree` | `ctrl+b` | フォルダーツリー（左サイドバー）表示切替。 |
| `collapseFolders` | `ctrl+shift+b` | フォルダーツリーをすべてルートまで折りたたむ。 |
| `toggleRendered` | `ctrl+shift+r` | レンダリング表示／ソース表示の切替（Markdown / HTML / CSV / JSON プレビュー）。トグルが表示されているときのみ有効。 |
| `loadExternalImages` | `ctrl+shift+i` | 現在のプレビューで外部 (https) 画像を一度だけ読み込む。ボタンが表示されているときのみ有効。 |
| `toggleSplit` | `ctrl+backslash` | split view 表示切替。 |
| `swapPanes` | `ctrl+shift+x` | 左右ペイン入れ替え。 |
| `focusSearch` | `ctrl+f` | 検索欄へフォーカス。 |
| `focusFilePane` | `ctrl+1` | アクティブなファイル一覧へフォーカス移動（ターミナルペイン内からも有効）。 |
| `focusTerminal` | `ctrl+2` | 内蔵ターミナルペインへフォーカス移動。非表示なら開いてから移動。 |
| `toggleHidden` | `ctrl+shift+.` | 隠しファイル表示切替。 |
| `goBack` | `alt+left` | 戻る。 |
| `goForward` | `alt+right` | 進む。 |
| `goUp` | `alt+up` | 親フォルダーへ移動。 |
| `openItem` | `enter` | 選択項目を開く。 |
| `newFolder` | `ctrl+shift+n` | フォルダー作成。 |
| `newFile` | `ctrl+n` | ファイル作成。 |
| `rename` | `f2` | インライン rename。 |
| `moveToTrash` | `delete` | Recycle Bin へ移動。 |
| `compressToZip` | `ctrl+k` | zip 圧縮。 |
| `extractZip` | `ctrl+shift+e` | zip 展開。 |
| `copyItems` | `ctrl+c` | Copy。 |
| `cutItems` | `ctrl+x` | Cut。 |
| `pasteItems` | `ctrl+v` | Paste（ファイル、またはクリップボードの画像/CSV/URL/リッチテキスト→`.rtf`/テキストからファイル作成）。 |
| `pasteAsText` | `ctrl+shift+v` | クリップボードのテキストを .txt ファイルとして貼り付け（リッチテキストより優先）。 |
| `selectAll` | `ctrl+a` | すべて選択。 |
| `newTab` | `ctrl+t` | アクティブペインに新しいタブを開く。 |
| `closeTab` | `ctrl+w` | アクティブタブを閉じる。 |
| `nextTab` | `ctrl+shift+]` | 次のタブへ切り替え。 |
| `prevTab` | `ctrl+shift+[` | 前のタブへ切り替え。 |
| `toggleTerminal` | `ctrl+j` | 内蔵ターミナルペインの表示切替。（既定は `` ctrl+` `` を避けています。日本語キーボードでは `` ` `` キーが「半角/全角」位置で IME に取られ届かないためです。US 配列等で問題なければ `` ctrl+` `` を指定できます。） |
| `quit` | `ctrl+q` | アプリを終了する（セッションを保存しターミナルを破棄）。ターミナルペインにフォーカスがある間はシェルの `Ctrl+Q` を優先するため無視されます。`Alt+F4` は常にウィンドウを閉じます。 |

`toggleTerminal` には `` ` ``（バッククォート）キー、タブ切替には `[` / `]` キーが使えます。

2 つのアクションが同じショートカットになる場合、後から来た衝突設定を無視し、ステータス警告を表示します。

### `[terminal]`

**外部**ターミナル（ツールバーボタン / `openTerminal` ショートカット）と、**内蔵**ターミナルペイン（`toggleTerminal`、既定 `Ctrl+J` / ツールバーのトグル）の見た目の両方を設定します。

```toml
[terminal]
app = "C:\Program Files\WezTerm\wezterm-gui.exe"
arguments = "start --cwd {path}"
```

`app` には `PATH` 上の実行ファイル、`wt.exe` / `pwsh.exe` などのアプリ実行エイリアス、絶対パスを指定できます。Windows パスは通常どおり単一のバックスラッシュで書けます。`arguments` は任意です。`{path}` はアクティブペインの現在フォルダーに置換され、安全にクォートされます。

`[terminal]` を省略した場合、tfx は `wt.exe` をアクティブペインのフォルダーで開きます。Windows Terminal が使えない場合は PowerShell にフォールバックします。`app` だけを設定して `arguments` を省略した場合も、`wt.exe`、WezTerm、PowerShell、pwsh にはフォルダーを開く既定引数を自動で補います。

#### 内蔵ターミナルの見た目

上の `app` / `arguments` は**外部**ターミナル用です。以下のキーは**内蔵**ターミナルペイン（`toggleTerminal`、既定 `Ctrl+J` / ツールバーのトグル）のスタイルを設定します:

| キー | 型 | 既定 | 説明 |
| --- | --- | --- | --- |
| `shell` | string | (自動) | 内蔵ペインで起動するシェルのコマンドライン。自動検出は PowerShell → `%ComSpec%` / cmd の順。例: `"pwsh.exe -NoLogo"`。 |
| `font` | string | (組込) | 内蔵ペインのフォントファミリー。`monospace` は `Cascadia Mono, Consolas, Yu Gothic UI` に解決。 |
| `fontSize` | number | (セッション) | フォントサイズ（`8`〜`40`）。`size` も使用可。永続値より優先。 |
| 色キー | string `#RRGGBB` | Campbell | パレット上書き（下記）。 |

256 色（xterm）および 24bit トゥルーカラーのエスケープシーケンスを描画します。指定できる名前付き色キー（すべて `#RRGGBB` を引用）: `background`, `foreground`, `cursor`, および 16 の ANSI スロット `black`, `red`, `green`, `yellow`, `blue`, `magenta`, `cyan`, `white`, `brightBlack`, `brightRed`, `brightGreen`, `brightYellow`, `brightBlue`, `brightMagenta`, `brightCyan`, `brightWhite`。

```toml
[terminal]
shell = "pwsh.exe -NoLogo"
font = "Cascadia Mono"
fontSize = 14
foreground = "#CCCCCC"
cursor = "#7DD3FC"
brightBlack = "#5A5A5A"   # PSReadLine の履歴予測（ゴースト）テキスト
# background = "#0C0C0C"   # 省略するとウィンドウの透過に追従
```

`background` を**省略**すると、ターミナル面は透明になり tfx ウィンドウの透過設定（`[opacity] background`）に追従します。`#RRGGBB` を指定すると不透明な背景色になります。

`brightBlack` は PowerShell の PSReadLine がインライン履歴予測に使う色です。既定でやや抑えめ（`#5A5A5A`）にしてありますが、さらに暗く（例: `#3C3C3C`）すれば予測表示をより目立たなくでき、明るくすれば見やすくなります。

ファイルペインからファイルやフォルダーをターミナルにドラッグ&ドロップすると、それらのフルパスがプロンプトに入力されます（スペース区切り。空白を含むパスは二重引用符で囲まれます）。

#### 内蔵ターミナルの操作

- **中断** — ターミナルペインのヘッダーにある `^C` ボタンで、実行中のコマンドに割り込み（Ctrl+C / ETX `0x03`）を送ります。ページ内のキーボード `Ctrl+C` は環境によってはシェルに届かないため、このボタンが確実な中断手段です。
- **コピー** — 選択範囲がある状態での `Ctrl+C`、または `Ctrl+Shift+C` で選択範囲をクリップボードにコピーします。選択がない状態の `Ctrl+C` は割り込みを送ります。
- **貼り付け** — `Ctrl+V` または `Ctrl+Shift+V` でクリップボードのテキストをシェルに貼り付けます。
- **パス入力** — ファイルペインからファイル/フォルダーをドラッグするとパスが入力されます（上記参照）。
- **閉じる** — `×` ボタン、`Ctrl+J`、または `exit` 入力でペインを閉じます。再度開くと新しいシェルで始まります。

### `[openWith]`

拡張子ごとにファイルを開くアプリを指定します。キーは先頭のドットを除いた拡張子です。

```toml
[openWith]
md = "code"
txt = "notepad.exe"
pdf = "C:\\Program Files\\SumatraPDF\\SumatraPDF.exe"
```

複合拡張子はクォートできます。

```toml
[openWith]
"tar.gz" = "C:\\Tools\\ArchiveViewer\\ArchiveViewer.exe"
```

ディレクトリ、zip ナビゲーション、アーカイブ内部ファイルは既存の tfx 動作を維持します。

### `[[commands]]`

ファイルペインの右クリックメニューに表示される、ユーザー定義コマンドです。各エントリは外部プログラムを起動します（起動して放置 — tfx は出力を取得せず完了も待ちません）。これにより、インストール済みの任意のインタープリター（PowerShell / cmd / Git Bash / Python など）を、組み込みのスクリプト実行環境なしで利用できます。コマンドは、現在の選択がそのすべての条件に合致するときだけメニューに表示されます。

```toml
[[commands]]
name = "VS Code で開く"
run = "code {path}"

[[commands]]
name = "PNG を最適化"
run = "pwsh -File C:\\scripts\\optipng.ps1 {paths}"
extensions = ["png"]

[[commands]]
name = "ここで Git Bash を開く"
run = "C:\\Program Files\\Git\\bin\\bash.exe --login -i"
target = "folder"
selection = "single"

[[commands]]
name = "行数 / 単語数 / 文字数を数える"
run = "pwsh -NoProfile -File \"{scripts}\\wc.ps1\" {paths}"
target = "file"
terminal = true   # 出力を内蔵ターミナルペインに表示

[[commands]]
name = "git push"
run = "git -C {cwd} push"
target = "current"   # 現在のフォルダー対象。何も選択していなくても表示
requireGit = true    # Git 作業ツリー内のみ
terminal = true      # push の出力を出力タブに表示
shortcut = "ctrl+shift+p"   # キー押下で実行
```

別プロセスとして起動する場合（既定の `terminal = false`）、標準出力は**どこにも表示されません** — tfx は出力を取得せず、ウィンドウも保持しません。出力を見るには `terminal = true` にしてください。コマンドの標準出力 / 標準エラーがキャプチャされ、ターミナルペインの**出力（Output）タブ**（対話用の **Shell** タブとは別の読み取り専用タブ）に表示されます。出力タブに内容が入るとタブバーが表示されます。あるいは、起動するプログラム側でウィンドウを保持してください（例: `pwsh -NoExit ...`）。

#### 複数行スクリプト

`run` には、TOML の複数行リテラル文字列（`'''…'''`）でスクリプト全体を記述できます。本文はそのまま（エスケープ処理なし）扱われ、トークンは置換され、tfx はコマンド自身の `shell`（指定があれば）、なければ **`[terminal] shell`**（どちらも未設定なら PowerShell）でスクリプトを実行します。一時ファイルに書き出し、シェルに応じた形式で起動します（PowerShell → `.ps1` を `-NoProfile -ExecutionPolicy Bypass -File`、`cmd` → `.bat` を `/c`、`bash` / `sh` → `.sh`）。`terminal = true` と組み合わせると出力を確認できます:

```toml
[[commands]]
name = "画像情報"
extensions = ["png", "jpg", "jpeg", "gif"]
terminal = true
run = '''
$f = {path}
$img = [System.Drawing.Image]::FromFile($f)
Write-Output ("{0}  {1} x {2}  {3:N0} bytes" -f (Split-Path $f -Leaf), $img.Width, $img.Height, (Get-Item $f).Length)
$img.Dispose()
'''

# [terminal] shell に関わらず、特定のシェル（ここでは cmd）で実行:
[[commands]]
name = "ディレクトリ一覧"
target = "current"
terminal = true
shell = "cmd"
run = '''
@echo off
dir /b {cwd}
'''
```

スクリプト内でもトークン（`{path}` / `{paths}` / `{stem}` など）は引用符付き文字列に展開されます。例えば `$f = {path}` は `$f = "C:\pics\a.png"` になります。

| キー | 型 | 既定 | 説明 |
| --- | --- | --- | --- |
| `name` | string | (必須) | メニューに表示するラベル。 |
| `run` | string | (必須) | 起動するコマンドライン。環境変数は展開されます。下記トークンを使えます。 |
| `extensions` | string 配列 | 全ファイル | 先頭のドットを除いた対象拡張子。省略または `["*"]` で全ファイル。 |
| `target` | string | `any` | `file` / `folder` / `current` / `any`。`file` / `folder` は選択項目に対して判定。`current` は選択を無視して**現在のフォルダー**を対象にし、何も選択していなくてもメニューに表示されます（空き領域を右クリック）。`git push` などフォルダー単位の操作に便利。 |
| `selection` | string | `any` | `single` / `multiple` / `any` — 選択数で限定。（`target = "current"` のときは無視されます。） |
| `requireGit` | bool | `false` | `true` のとき、現在のフォルダーが Git 作業ツリー内のときだけメニューに表示されます。 |
| `terminal` | bool | `false` | `true` のとき、別プロセスを起動する代わりに、コマンドの標準出力 / 標準エラーを内蔵ターミナルペインの読み取り専用**出力（Output）タブ**に流します。ペインが開き、自動的に出力タブへ切り替わります。 |
| `shortcut` | string | (なし) | キーボードショートカット。`[shortcuts]` と同じ文法（例: `"ctrl+shift+g"`）。現在のコンテキストがコマンドの条件に合致するとき、押下で実行されます。コンテキストメニューではコマンド名の横に表示されます。コマンドのショートカットは組み込みより優先されます。 |
| `shell` | string | (なし) | このコマンドの複数行 `run` スクリプトを実行するシェル（例: `"cmd"` / `"pwsh.exe -NoLogo"` / `"bash"`）。このコマンドに限り `[terminal] shell` を上書きします。単一行コマンド（自身で実行ファイルを指定）では無視されます。 |

`run` 内で置換されるトークン（パス系トークンは自動的に二重引用符で囲まれます）:

- `{path}` — 最初に選択した項目のフルパス（何も選択していなければ現在のフォルダー）。
- `{paths}` — 選択したすべての項目（スペース区切り。何も選択していなければ現在のフォルダー）。
- `{dir}` — 項目が置かれているフォルダー（最初の項目の親。何も選択していなければ現在のフォルダー）。
- `{name}` — 最初の項目の拡張子**あり**ファイル名（例: `report.pdf`）。
- `{stem}` — 拡張子**なし**ファイル名（例: `report`）。
- `{ext}` — 拡張子のみ（ドットなし。例: `pdf`。フォルダーの場合は空）。
- `{cwd}` — 選択の有無に関わらず、現在のフォルダー。
- `{scripts}` — `config.toml` と同じ場所の `scripts` フォルダー（`%APPDATA%\tfx\scripts`、必要に応じて自動作成）。絶対パスを書かずに設定と一緒にスクリプトを配布できます。例: `run = "pwsh -File \"{scripts}\\wc.ps1\" {paths}"`。（パス系トークンと異なり `{scripts}` は引用符なしで置換されるため、空白を含む可能性がある場合は自分で引用符で囲んでください。）

すべての条件が合致したときのみコマンドが表示されます。例: `extensions = ["png", "jpg"]` と `selection = "single"` を指定すると、`.png` または `.jpg` を 1 つだけ選んだときにのみ表示されます。`name` または `run` が欠けたエントリは設定警告として報告され、無視されます。

## 例

### カラーサンプル

次のサンプルは、現在の Windows WPF テーマに反映されるカラーキーだけを使っています。どれか 1 つを `config.toml` にコピーして、トークン単位で調整できます。

#### Evergreen Terminal

黒と緑のコントラストを強めた、ターミナル風の配色です。

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

アクティブペインの差が分かりやすい、暖色のアンバー配色です。

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

ライトモード用のグラファイト配色です。明るい環境向けで、ディレクトリは緑系アクセントで残します。

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

## エラー処理

tfx は次のような内容を設定エラーとして扱います。

- `size: 13` のような不正な代入構文
- 未対応のトップレベル `version`
- `ui`、`mono`、`terminal`、`openWith` が文字列ではない
- `size` が `8` から `40` の範囲外
- カラー値が `"#RRGGBB"` 文字列ではない
- 透明度が `0` から `1` の範囲外
- 未知のショートカット修飾キーまたはキー
- ショートカット衝突

エラーがあっても tfx は起動を継続し、該当設定は組み込み既定値にフォールバックします。
