# Archivio

An elegant Windows WinUI 3 desktop application for listing video files, managing Windows property system metadata, and updating cover arts.

動画ファイルの一覧表示、Windowsプロパティシステムを介したメタデータの管理、およびカバーアート（表紙画像）の編集を行う、洗練されたWinUI 3デスクトップアプリケーションです。

---

## Language / 言語
* [English (#english)](#english)
* [日本語 (#日本語)](#日本語)

---

<a name="english"></a>
# English

Archivio is a high-performance video metadata manager built natively for Windows using WinUI 3 (Windows App SDK) and .NET 10. It reads, displays, and writes video metadata (such as title, actors, release date, and ratings) directly from and to the Windows Property System, allowing seamless integration with Windows Search and File Explorer.

## Features
* **Dynamic Grid View:** An interactive `DataGrid` listing video files, with support for column resizing, sorting, and user-led column reordering (drag-and-drop) which persists automatically across application restarts.
* **Three-Column Detail Panel:** 
  * **Left:** Cover art `FlipView` gallery with tools to add, change, delete, and save images.
  * **Center:** Editable metadata fields (Title, Participants, Release Date, Catalog Number, Rating, Publisher, Label, Category, Comment) saving directly to Windows shell properties.
  * **Right:** Read-only technical metadata panel (Duration, Resolution, Frame Rate, Video/Audio Bitrates, Compression formats, etc.).
* **Dynamic JSON Localization:** Fully localized in English and Japanese. Includes an in-place language switcher inside the DataGrid context menu that re-localizes the entire UI instantly without requiring an app restart.
* **Persistent Layouts:** Automatically saves and restores window dimensions, grid splitter positions, column widths, and custom column display indices.
* **Thread-Safe Settings:** Fully synchronized settings file I/O protecting against race conditions and concurrent read/write errors.

---

## FFmpeg & FFprobe Integration & Handling

Archivio utilizes the official **FFmpeg** and **FFprobe** command-line utilities to perform technical analysis and file operations. They are usually distributed together (e.g. from static builds of FFmpeg). Adding FFmpeg's bin folder to the system's `PATH` environment variable immediately enables both features in Archivio.

### 1. FFprobe: Technical Property Extraction
FFprobe is prioritized to extract high-precision technical properties (such as compression codec, audio stream format, accurate bitrates, dimensions, etc.) from video files.
* **When FFprobe is Present:** Archivio launches the utility as a quick, silent background process, parses its JSON output, and populates the technical fields with 100% precision.
* **When FFprobe is Absent (Graceful Fallback):** 
  * If FFprobe is not found in the `PATH` or configured path, **Archivio gracefully falls back to using the native Windows Shell API**.
  * It retrieves standard Windows file properties (such as frame size and duration) so the user still sees key details.
  * It caches the missing status of FFprobe (`_ffprobeAvailability = -1`) to avoid spawning redundant failing processes, ensuring smooth performance.
  * *Tip:* Users can specify a custom path to the FFprobe executable in the `FfprobePath` field inside `settings.json` (located in `AppData/Local/Archivio`).

### 2. FFmpeg: Container Re-muxing
FFmpeg is used for the **"Re-mux with FFmpeg"** feature available in the context menu of the video list, which quickly re-muxes files into standard streamable containers with the `-movflags +faststart` flag (optimizing them for instant web playback).
* **When FFmpeg is Present:** Re-muxing runs as a non-destructive background process. The newly created file is inserted directly into your active list right next to the original, asynchronously analyzed, and cached.
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
* **Runtime:** .NET 10.0 Runtime
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

Archivio（アルキーヴィオ）は、WinUI 3 (Windows App SDK) と .NET 10 を採用した Windows 用の高性能な動画メタデータ管理ツールです。動画ファイルの属性（タイトル、出演者、発売日、評価など）をWindowsのプロパティシステムと直接同期させ、エクスプローラーやWindows検索とシームレスに連動した管理が行えます。

## 主な機能
* **動的グリッドビュー:** 動画ファイル一覧を表示するインタラクティブな `DataGrid`。カラムの並び替え（ドラッグ＆ドロップ）、リサイズ、ソートに対応し、その順序や幅はアプリ再起動後も自動で復元されます。
* **高密度3カラム詳細パネル:**
  * **左カラム:** カバーアート（表紙）の `FlipView` ギャラリー。画像の追加・変更・削除、タグへの書き込み保存に対応。
  * **中央カラム:** 編集可能なメタデータ入力（タイトル、出演者、発売日、品番、評価、レーベル、発行元、カテゴリ、コメント）。
  * **右カラム:** 読み取り専用の技術プロパティ表示（再生時間、解像度、フレームレート、映像/音声ビットレート、圧縮形式など）。
* **動的 JSON ローカライズ:** 日本語と英語に完全対応。右クリックのコンテキストメニューからアプリを再起動することなく、UI表示言語をその場で一瞬で切り替えられます。
* **レイアウト設定の自動保存:** ウィンドウサイズ、分割バー位置、各カラム幅、カラムの表示順序を終了時に自動保存し、次回起動時に再現します。
* **スレッドセーフな設定管理:** 並列バックグラウンド処理でのファイル書き込み競合を防止する静的ロック（FileLock）を搭載した安全な設計。

---

## FFmpeg & FFprobe の有無に関する処理と統合仕様

Archivioは、動画ファイルの技術分析とコンテナ処理に公式のコマンドラインユーティリティである **FFmpeg** および **FFprobe** を利用します。これらは通常セットで配布されており（公式のFFmpegスタティックビルド等）、FFmpegの `bin` フォルダをシステムの環境変数 `PATH` に登録するだけで、Archivio側のすべての機能が有効化されます。

### 1. FFprobe: 技術仕様メタデータの抽出
動画ファイルから圧縮コーデック、音声ストリーム形式、高精度なビットレート、解像度などの詳細な技術仕様情報を抽出するために、FFprobeを優先的に使用します。
* **FFprobe が利用可能な場合:** Archivioはバックグラウンドでサイレントかつ高速にプロセスを起動し、そのJSON出力を自動解析して技術仕様フィールドへ100%正確な値を適用します。
* **FFprobe が利用不可能な場合（安全な代替処理）:**
  * システム上でFFprobeが検出できない場合、**Archivioはネイティブの Windows Shell API による情報取得へ自動でフォールバックします。**
  * Windowsがファイル情報として持っている基本的な情報（再生時間や解像度など）を代替取得し、ユーザーに表示します。
  * FFprobeが不在であるというステータス（`_ffprobeAvailability = -1`）をスレッド安全にキャッシュすることで、存在しないプロセスを何度も起動しようとする「無駄なプロセス生成」を防ぎ、アプリ全体の描画パフォーマンスを維持します。
  * *Tip:* ユーザーは `AppData/Local/Archivio/settings.json` 内の `FfprobePath` 項目に直接FFprobeの実行ファイルパスを指定することも可能です。

### 2. FFmpeg: 動画コンテナの超高速再MUX
動画一覧の右クリックメニューから利用できる **「Ffmpegでの再MUX」** 機能にFFmpegを使用します。動画コンテナに `-movflags +faststart` を適用して再マウントし、ウェブでの即時再生に最適化されたストリーマブルな動画ファイルを作成します。
* **FFmpeg が利用可能な場合:** バックグラウンドで非破壊の高速な再MUXを実行します。出来上がった新規ファイルはフォルダ再走査を待たずに一覧のすぐ下に直接挿入され、非同期的に解析されてキャッシュに即時保存されます。
* **FFmpeg が利用不可能な場合:**
  * **強制終了の防止:** プロセス起動エラー（`Win32Exception` 等）を完全に例外処理でキャッチし、アプリがフリーズしたりクラッシュしたりするのを絶対に防ぎます。
  * **ユーザーへの親切な案内:** 処理を安全に終了したうえで、ステータスバー上に以下のローカライズされたわかりやすいエラーメッセージを提示します：
    * *日本語:* `"ffmpegがシステムにインストールされていないか、環境変数 PATH に登録されていません。"`
    * *英語:* `"FFmpeg is not installed on the system, or not registered in the PATH environment variable."`
  * **安全な状態リセットとログ:** 処理中状態を示すインジケーターを安全に解除し、ログファイル `C:\Users\<ユーザー名>\AppData\Local\Archivio\Archivio.log` に診断エラーの詳細を出力します。

---

## システム要件
* **対応OS:** Windows 10 (バージョン 1809、ビルド 17763 以降) または Windows 11
* **ランタイム:** .NET 10.0 ランタイム
* **外部ツール (任意):** FFmpeg/FFprobe (高精度な仕様分析、および「再MUX」機能の利用に必要)

## 外部言語ファイルの追加（ボランティア翻訳）
Archivioは、外部ファイルを配置するだけで簡単に新しい表示言語を追加できます：
1. アプリインストールディレクトリの `Assets/Locale/` フォルダを開きます。
2. 新しい言語ファイル `[言語コード].json`（例：フランス語なら `fr.json`）を配置します。
3. JSONファイル内に `\"LanguageName\": \"Français\"` と各キーの翻訳を記述します。
次回起動時、右クリックの「表示言語 / Language」メニューに新しい言語が自動的に追加され、選択可能になります！
