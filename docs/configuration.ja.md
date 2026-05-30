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
- `rightFolders` 用の文字列配列
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
toggleSplit = "ctrl+backslash"
swapPanes = "ctrl+shift+x"
focusSearch = "ctrl+f"
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
selectAll = "ctrl+a"

# [startup]
# layout = "single"
# preview = "restore"
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
```

| キー | 型 | 既定値 | 説明 |
| --- | --- | --- | --- |
| `ui` | string | `"system"` | ツリー、ヘッダー、ダイアログ風 UI などの UI フォント。 |
| `mono` | string | `"monospace"` | ファイル一覧、ステータス、Raw text、JSON、CSV preview などの等幅フォント。 |
| `size` | number | `13` | 基準フォントサイズ。範囲は `8` から `40` です。 |

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
rightFolders = ["~/Downloads", "~/Documents"]
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

`rightFolder` は `layout = "split"` のときに右ペインの初期フォルダーを 1 つ指定します。

```toml
[startup]
layout = "split"
rightFolder = "~/Downloads"
```

`rightFolders` は配列を受け付けます。現在の Windows 版にはペインタブがないため、最初の有効なフォルダーを右ペインの初期フォルダーとして使います。

パスには絶対パス、`%USERPROFILE%\Downloads` のような環境変数付きパス、`~` 付きユーザーパスを指定できます。

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
| `toggleSplit` | `ctrl+backslash` | split view 表示切替。 |
| `swapPanes` | `ctrl+shift+x` | 左右ペイン入れ替え。 |
| `focusSearch` | `ctrl+f` | 検索欄へフォーカス。 |
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
| `pasteItems` | `ctrl+v` | Paste。 |
| `selectAll` | `ctrl+a` | すべて選択。 |

2 つのアクションが同じショートカットになる場合、後から来た衝突設定を無視し、ステータス警告を表示します。

### `[terminal]`

ツールバーと `openTerminal` ショートカットで起動する外部ターミナルを設定します。

```toml
[terminal]
app = "C:\Program Files\WezTerm\wezterm-gui.exe"
arguments = "start --cwd {path}"
```

`app` には `PATH` 上の実行ファイル、`wt.exe` / `pwsh.exe` などのアプリ実行エイリアス、絶対パスを指定できます。Windows パスは通常どおり単一のバックスラッシュで書けます。`arguments` は任意です。`{path}` はアクティブペインの現在フォルダーに置換され、安全にクォートされます。

`[terminal]` を省略した場合、tfx は `wt.exe` をアクティブペインのフォルダーで開きます。Windows Terminal が使えない場合は PowerShell にフォールバックします。`app` だけを設定して `arguments` を省略した場合も、`wt.exe`、WezTerm、PowerShell、pwsh にはフォルダーを開く既定引数を自動で補います。

Windows 版には現在、内蔵ターミナルペインはありません。この設定は外部ターミナル / アプリを起動します。

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
