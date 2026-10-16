# Archivio

An elegant Windows WinUI 3 desktop application for listing video files, managing embedded video metadata, and editing cover art.

動画ファイルを一覧表示し、動画ファイル内のタグを正本としてメタデータやカバーアートを管理するWinUI 3デスクトップアプリケーションです。

---

## Language / 言語
* [English (#english)](#english)
* [日本語 (#日本語)](#日本語)

---

<a name="english"></a>
# English

Archivio is a Windows video metadata manager built with WinUI 3 (Windows App SDK) and .NET 10. It reads and writes editable metadata through embedded video tags using TagLib. Windows APIs and FFprobe provide technical media properties; the video file is the source of truth.

## Features
* **Dynamic Grid View:** An interactive `DataGrid` listing video files, with support for column resizing, sorting, and user-led column reordering (drag-and-drop) which persists automatically across application restarts.
* **Three-Column Detail Panel:**
  * **Left:** Cover art `FlipView` gallery with tools to add, replace, delete, save, capture a playback frame, and create a 5x5 thumbnail sheet.
  * **Center:** Editable metadata fields saved to embedded file tags. The tag panel can be displayed in one or two columns, and its save button remains fixed above the fields.
  * **Right:** Read-only technical metadata panel (Duration, Resolution, Frame Rate, Video/Audio Bitrates, Compression formats, etc.).
* **Video Playback Keyboard Controls:** When playback is active, Left/Right seek by five seconds, Up/Down change volume by five percent, and Space toggles play/pause.
* **Metadata Filename Templates:** Generate a filename from the selected video's embedded metadata from the file-list context menu. The original extension is preserved and invalid Windows filename characters are replaced with `_`.
* **Dynamic JSON Localization:** Fully localized in English and Japanese. Includes an in-place language switcher inside the DataGrid context menu that re-localizes the entire UI instantly without requiring an app restart.
* **Persistent Layouts:** Automatically saves and restores window dimensions, grid splitter positions, column widths, and custom column display indices.
* **Thread-Safe Settings:** Fully synchronized settings file I/O protecting against race conditions and concurrent read/write errors.

### Metadata Filename Templates

Right-click a file in the video list and choose **Generate filename from tags**. The template is saved in the application settings and reused on subsequent operations. The default template is:

```text
[{credits.cast.0}]{work.title}[{identifiers.catalog-number}]
```

For a first cast member of `Taro Yamada`, a title of `Northern Country`, and catalog number `MMA-001`, the result is:

```text
[Taro Yamada]Northern Country[MMA-001].mp4
```

Filename templates support all stable IDs registered in `MetadataFieldCatalog`, including work, cast/staff, series, dates, evaluation, identifiers, acquisition, rights, location, video/audio technical information, and custom fields (`custom.*`). Multiple values can be indexed with `.0`, `.1`, and so on. The complete current ID list is maintained in the Japanese section below; it is generated from the same catalog used by the application.

Cast-specific forms include `credits.cast.0` (person name), `credits.cast.0.character` (role name), and `credits.cast.0.lead` (lead flag).

Template syntax:

* `{work.title}` replaces the ID and does not output the braces.
* `\\work.title` replaces the ID without outputting a wrapper symbol.
* `[\\credits.cast.0]` keeps the square brackets as literal characters and replaces the escaped ID inside them.
* `{\\credits.cast.0}` keeps the curly braces as literal characters and replaces the escaped ID inside them.

The compatibility aliases `出演者0`, `タイトル`, and `品番` are also accepted. The default template uses stable IDs so it is not tied to display names.

---

## FFmpeg & FFprobe Integration & Handling

Archivio utilizes the official **FFmpeg** and **FFprobe** command-line utilities to perform technical analysis and file operations. They are usually distributed together (e.g. from static builds of FFmpeg). Adding FFmpeg's bin folder to the system's `PATH` environment variable immediately enables both features in Archivio.

### 1. FFprobe: Technical Property Extraction
FFprobe is prioritized to extract high-precision technical properties (such as compression codec, audio stream format, accurate bitrates, dimensions, etc.) from video files.
* **When FFprobe is Present:** Archivio launches the utility in the background, parses its JSON output, and populates the technical fields with detailed stream information.
* **When FFprobe is Absent (Graceful Fallback):** 
  * If FFprobe is not found in the `PATH` or configured path, **Archivio gracefully falls back to using the native Windows Shell API**.
  * It retrieves standard Windows file properties (such as frame size and duration) so the user still sees key details.
  * It caches the missing status of FFprobe (`_ffprobeAvailability = -1`) to avoid spawning redundant failing processes, ensuring smooth performance.
  * *Tip:* Users can specify a custom path to the FFprobe executable in the `FfprobePath` field inside `settings.json` (located in `AppData/Local/Archivio`).

### 3. FFmpeg: Captures and Thumbnail Sheets
* A single frame can be captured from the current playback position through FFmpeg.
* A thumbnail sheet is generated by seeking to 25 evenly spaced positions and combining the frames into a 5x5 image. FFmpeg is detected from the configured FFprobe directory, the current `PATH`, or `where.exe`.

### 2. FFmpeg: Container Re-muxing
FFmpeg is used for the **"Re-mux with FFmpeg"** feature available in the context menu of the video list, which quickly re-muxes files into standard streamable containers with the `-movflags +faststart` flag (optimizing them for instant web playback).
* **When FFmpeg is Present:** Re-muxing runs in the background without modifying the source. The output is inserted next to the original, then analyzed and cached. If an output name already exists, a numbered name is used; existing files are not overwritten.
* **When FFmpeg is Absent:**
  * **No Crashes:** The application will **never** crash or freeze.
  * **Graceful Handling:** Archivio intercepts the missing executable exception (`Win32Exception`) gracefully.
  * **User Guidance:** It displays a clear, friendly, and fully localized error message in the status bar:
    * *English:* `"FFmpeg is not installed on the system, or not registered in the PATH environment variable."`
    * *Japanese:* `"ffmpegがシステムにインストールされていないか、環境変数 PATH に登録されていません。"`
  * **Safe State:** It resets the busy indicators safely and logs the diagnostic error under `C:\Users\<User>\AppData\Local\Archivio\Archivio.log`.

---

## System Requirements
* **Operating System:** Windows 10 (version 1809, build 17763 or later) or Windows 11
* **Runtime:** .NET 10.0 Desktop Runtime (x64)
* **External Tool (Optional):** FFmpeg/FFprobe (Required for Re-muxing and precision metadata extraction)

## Dynamic Translation Support
Archivio supports easy community-led translation additions. You can add a new language simply by:
1. Navigating to the `Assets/Locale/` folder in the installation directory.
2. Dropping a new `[language_code].json` file (e.g. `fr.json` for French).
3. Defining `"LanguageName": "Français"` inside the JSON, along with your translations.
The language will automatically appear in the right-click "Language / 表示言語" menu on next launch!

---

<a name="日本語"></a>
# 日本語

Archivio（アルキーヴィオ）は、WinUI 3 (Windows App SDK) と .NET 10 を採用した Windows 用の動画メタデータ管理ツールです。
動画ファイル内のタグ（タイトル、出演者、発売日、評価など）をTagLib経由で読み書きします。動画ファイルを正本とし、Windows APIとFFprobeは技術情報の取得に利用します。

## はじめに

Archivioは、動画ファイルを一覧で確認し、動画に埋め込まれたメタデータやカバーアートを整理するための無料アプリです。
動画ファイルそのものを管理対象とするため、別のデータベースを用意せずに、ファイルと一緒にメタデータを持ち運べます。

### インストール

1. `Archivio_Setup.exe`を実行します。
2. 画面の案内に従ってインストールします。
3. スタートメニューまたはデスクトップのArchivioから起動します。

Archivioの実行には **.NET 10.0 Desktop Runtime（x64）** が必要です。インストールされていない場合は、Microsoftの公式ページから.NET Desktop Runtimeをインストールしてください。

### 基本的な使い方

1. 「フォルダを選択」を押し、動画ファイルが入っているフォルダを選択します。
2. 必要に応じて「サブフォルダーも検索」を有効にして、下位フォルダーも検索します。
3. 動画一覧からファイルを選択すると、メタデータ、カバーアート、技術情報が表示されます。
4. 中央のタグ情報欄を編集し、タグ情報欄上部の「タグ保存」を押すと動画ファイルへ書き込みます。タグ情報欄は1列／2列を切り替えられます。
5. 一覧の右クリックメニューから、ファイル名の変更、再読み込み、再MUX、READMEの表示などを実行できます。

再生画面では、キーボードの左右矢印で5秒シーク、上下矢印で音量を5%変更、スペースキーで再生／一時停止を切り替えられます。

### 大切な注意事項

メタデータの保存やファイル名の変更を行う前に、必要な動画ファイルのバックアップを作成してください。
特に、独自項目（評価・発売日）はMP4/MKV/WebM以外の形式には埋め込み保存できず、カバーアートも動画形式によって保存できない場合があります。
保存後は、対象ファイルを再度読み込んで変更内容を確認してください。

.mp4,.mkvでもメタデータの保存に失敗した場合、FFmpegでの再MUXを試してみてください。
動画本体の情報はそのままメタデータ領域を再構成して保存可能となる可能性があります。

### FFmpegについて

動画の技術情報をより正確に取得したり、再MUX機能を使用したりするにはFFmpegとFFprobeが必要です。
FFmpegの`bin`フォルダーを環境変数`PATH`へ追加すると、Archivioから自動的に利用できます。
未設定の場合も、Windowsが提供する基本的なファイル情報を使って動作します。

### READMEの表示

インストール後は、動画一覧を右クリックして「READMEを表示」を選択すると、この利用案内を既定のMarkdown対応アプリまたは関連付けられたアプリで開けます。

Archivioは無料で利用できます。もしArchivioがお役に立ちましたら、今後の開発を応援していただけると嬉しいです。

応援はこちら: <https://ofuse.me/8679942e>

## 主な機能
* **動的グリッドビュー:** 動画ファイル一覧を表示するインタラクティブな `DataGrid`。カラムの並び替え（ドラッグ＆ドロップ）、リサイズ、ソートに対応し、その順序や幅はアプリ再起動後も自動で復元されます。
* **高密度3カラム詳細パネル:**
  * **左カラム:** カバーアート（表紙）の `FlipView` ギャラリー。画像の追加・変更・削除・保存、再生位置の1枚キャプチャ、5×5サムネイル一覧作成に対応。
  * **中央カラム:** 動画ファイル内タグへ保存するメタデータ入力（タイトル、出演者、発売日、品番、評価、レーベル、発行元、カテゴリ、コメント）。1列／2列表示を選択できます。
  * **右カラム:** 読み取り専用の技術プロパティ表示（再生時間、解像度、フレームレート、映像/音声ビットレート、圧縮形式など）。
* **動画再生キーボード操作:** 再生中は左右矢印で5秒シーク、上下矢印で音量を5%変更、スペースキーで再生／一時停止を切り替えます。
* **タグ情報からのファイル名生成:** 動画一覧の右クリックメニューから、埋め込みタグを使ったファイル名を生成できます。元の拡張子は維持され、Windowsで使用できない文字は `_` に置換されます。
* **動的 JSON ローカライズ:** 日本語と英語に完全対応。右クリックのコンテキストメニューからアプリを再起動することなく、UI表示言語をその場で一瞬で切り替えられます。
* **レイアウト設定の自動保存:** ウィンドウサイズ、分割バー位置、各カラム幅、カラムの表示順序を終了時に自動保存し、次回起動時に再現します。
* **スレッドセーフな設定管理:** 並列バックグラウンド処理でのファイル書き込み競合を防止する静的ロック（FileLock）を搭載した安全な設計。

### タグ情報からファイル名を生成するテンプレート

ファイル一覧で対象ファイルを右クリックし、「タグからファイル名を生成」を選択します。テンプレートは設定ファイルに保存され、次回以降も利用できます。既定値は次のとおりです。

```text
[{credits.cast.0}]{work.title}[{identifiers.catalog-number}]
```

出演者が「山田太郎」、タイトルが「北の国から」、品番が「MMA-001」の場合、次のようになります。

```text
[山田太郎]北の国から[MMA-001].mp4
```

ファイル名生成は、`MetadataFieldCatalog` に定義された安定IDを基本的にすべて使用できます。複数値は `.0`、`.1` のように添字を付けて指定できます。出演者は `credits.cast.0`（人物名）、`credits.cast.0.character`（役名）、`credits.cast.0.lead`（主演フラグ）も指定できます。カスタム項目は設定で定義した `custom.` から始まるIDを使用します。

以下は現在の標準カタログに登録され、ファイル名生成で指定できるIDの一覧です。項目追加時はカタログの安定IDも利用対象になります。

標準項目IDは次のとおりです。

| 分類 | 項目ID |
| --- | --- |
| ファイル・基本情報 | `file.status`, `file.name`, `file.size`, `work.title`, `credits.participants`, `dates.release`, `identifiers.catalog-number`, `evaluation.rating`, `publication.publisher`, `publication.label`, `work.category`, `work.comment` |
| 作品 | `work.subtitle`, `work.original-title`, `work.alternate-titles`, `work.description`, `work.synopsis`, `work.keywords`, `work.content-type` |
| 人物・スタッフ | `credits.cast`, `credits.crew`, `credits.production-companies`, `credits.distributors`, `credits.publishers` |
| シリーズ | `series.name`, `series.season-number`, `series.episode-number`, `series.episode-title`, `series.episode-id`, `series.season-count`, `series.episode-count`, `series.publication-year`, `series.part-number` |
| 日付・評価 | `dates.recording`, `dates.filming`, `dates.purchased`, `dates.archivio-registered`, `dates.archivio-updated`, `evaluation.content-rating`, `evaluation.personal-notes` |
| 識別・入手 | `identifiers.barcode`, `identifiers.imdb`, `identifiers.tmdb`, `identifiers.tvdb`, `acquisition.price`, `acquisition.currency`, `acquisition.seller`, `acquisition.url`, `acquisition.source`, `acquisition.original-media` |
| 権利・場所 | `rights.copyright-holder`, `rights.license`, `rights.source`, `location.filming`, `location.country` |
| 動画技術 | `technical.duration`, `technical.frame-width`, `technical.frame-height`, `technical.frame-rate`, `technical.video-bitrate`, `technical.video-codec`, `technical.video-profile`, `technical.video-level`, `technical.video-fourcc`, `technical.total-bitrate`, `technical.video-aspect-ratio`, `technical.video-pixel-aspect-ratio`, `technical.video-rotation`, `technical.video-hdr`, `technical.video-color-space`, `technical.video-color-primaries`, `technical.video-transfer`, `technical.video-matrix`, `technical.video-mastering-display`, `technical.video-maxcll`, `technical.video-maxfall`, `technical.video-stereo-3d`, `technical.video-360`, `technical.video-encoder`, `technical.video-encoding-settings`, `technical.video-stream-count`, `technical.video-track-id`, `technical.video-track-name` |
| 音声技術 | `technical.audio-sample-rate`, `technical.audio-bitrate`, `technical.audio-codec`, `technical.audio-profile`, `technical.audio-channels`, `technical.audio-channel-layout`, `technical.audio-language`, `technical.audio-loudness`, `technical.audio-replaygain`, `technical.audio-ebu-r128`, `technical.audio-true-peak`, `technical.audio-stream-count`, `technical.audio-track-id`, `technical.audio-track-name` |
| 画像 | `artwork.cover`, `artwork.thumbnail`（画像バイナリ自体は文字列化しないため空値） |

記法は次のとおりです。

* `{work.title}`：IDを置換し、`{}`は出力しません。
* `\\work.title`：IDを置換し、記号は出力しません。
* `[\\credits.cast.0]`：`[]`を通常の記号として残し、内部のIDを置換します。
* `{\\credits.cast.0}`：`{}`を通常の記号として残し、内部のIDを置換します。

値が空の項目は空文字として置換されます。画像のように文字列値を持たない項目は安全のため空文字になります。Windowsのファイル名に使用できない文字は `_` に置換され、元の拡張子は維持されます。

互換入力として、`出演者0`、`タイトル`、`品番`も使用できます。ただし、保存される既定テンプレートでは将来変更されない安定IDを使用します。

---

## FFmpeg & FFprobe の有無に関する処理と統合仕様

Archivioは、動画ファイルの技術分析とコンテナ処理に公式のコマンドラインユーティリティである **FFmpeg** および **FFprobe** を利用します。
これらは通常セットで配布されており（公式のFFmpegスタティックビルド等）、FFmpegの `bin` フォルダをシステムの環境変数 `PATH` に登録するだけで、Archivio側のすべての機能が有効化されます。

### 1. FFprobe: 技術仕様メタデータの抽出
動画ファイルから圧縮コーデック、音声ストリーム形式、高精度なビットレート、解像度などの詳細な技術仕様情報を抽出するために、FFprobeを優先的に使用します。
* **FFprobe が利用可能な場合:** Archivioはバックグラウンドでプロセスを起動し、そのJSON出力を解析して詳細なストリーム情報を技術仕様欄に表示します。
* **FFprobe が利用不可能な場合（安全な代替処理）:**
  * システム上でFFprobeが検出できない場合、**Archivioはネイティブの Windows Shell API による情報取得へ自動でフォールバックします。**
  * Windowsがファイル情報として持っている基本的な情報（再生時間や解像度など）を代替取得し、ユーザーに表示します。
  * FFprobeが不在であるというステータス（`_ffprobeAvailability = -1`）をスレッド安全にキャッシュすることで、存在しないプロセスを何度も起動しようとする「無駄なプロセス生成」を防ぎ、アプリ全体の描画パフォーマンスを維持します。
  * *Tip:* ユーザーは `AppData/Local/Archivio/settings.json` 内の `FfprobePath` 項目に直接FFprobeの実行ファイルパスを指定することも可能です。

### 2. FFmpeg: 動画コンテナの超高速再MUX
動画一覧の右クリックメニューから利用できる **「Ffmpegでの再MUX」** 機能にFFmpegを使用します。動画コンテナに `-movflags +faststart` を適用して再マウントし、ウェブでの即時再生に最適化されたストリーマブルな動画ファイルを作成します。
* **FFmpeg が利用可能な場合:** 元ファイルを変更せずバックグラウンドで再MUXします。新しいファイルは再走査を待たず一覧の隣に挿入され、非同期に解析・キャッシュされます。出力名が既に存在する場合は連番を付け、既存ファイルを上書きしません。
* **FFmpeg が利用不可能な場合:**
  * **強制終了の防止:** プロセス起動エラー（`Win32Exception` 等）を完全に例外処理でキャッチし、アプリがフリーズしたりクラッシュしたりするのを絶対に防ぎます。
  * **ユーザーへの親切な案内:** 処理を安全に終了したうえで、ステータスバー上に以下のローカライズされたわかりやすいエラーメッセージを提示します：
    * *日本語:* `"ffmpegがシステムにインストールされていないか、環境変数 PATH に登録されていません。"`
    * *英語:* `"FFmpeg is not installed on the system, or not registered in the PATH environment variable."`
  * **安全な状態リセットとログ:** 処理中状態を示すインジケーターを安全に解除し、ログファイル `C:\Users\<ユーザー名>\AppData\Local\Archivio\Archivio.log` に診断エラーの詳細を出力します。

---

## システム要件
* **対応OS:** Windows 10 (バージョン 1809、ビルド 17763 以降) または Windows 11
* **ランタイム:** .NET 10.0 Desktop Runtime（x64）
* **外部ツール (任意):** FFmpeg/FFprobe (高精度な仕様分析、および「再MUX」機能の利用に必要)

## 外部言語ファイルの追加（ボランティア翻訳）
Archivioは、外部ファイルを配置するだけで簡単に新しい表示言語を追加できます：
1. アプリインストールディレクトリの `Assets/Locale/` フォルダを開きます。
2. 新しい言語ファイル `[言語コード].json`（例：フランス語なら `fr.json`）を配置します。
3. JSONファイル内に `\"LanguageName\": \"Français\"` と各キーの翻訳を記述します。
次回起動時、右クリックの「表示言語 / Language」メニューに新しい言語が自動的に追加され、選択可能になります。
