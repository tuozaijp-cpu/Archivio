using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Archivio.Models;

namespace Archivio.ViewModels
{
    /// <summary>
    /// アプリケーションの設定（ウィンドウサイズ、分割バーの位置など）を保持するモデル。
    /// </summary>
    public class AppSettings
    {
        public int WindowWidth { get; set; } = 1400;
        public int WindowHeight { get; set; } = 900;
        public double FileListRowHeight { get; set; } = -1;
        public double DetailsRowHeight { get; set; } = -1;
        public double CoverArtColumnWidth { get; set; } = -1;
        public double PropertiesColumnWidth { get; set; } = -1;
        public double TechnicalColumnWidth { get; set; } = -1;
        public Dictionary<string, double> ColumnWidths { get; set; } = new();
        public List<string> ColumnOrder { get; set; } = new();
        /// <summary>一覧に表示する項目ID。nullは旧設定からの移行または既定値を表す。</summary>
        public List<string>? VisibleListFieldIds { get; set; }
        /// <summary>カタログの安定IDによる一覧列順。</summary>
        public List<string> ListFieldOrder { get; set; } = new();
        /// <summary>カタログの安定IDによる一覧列幅。</summary>
        public Dictionary<string, double> ListFieldWidths { get; set; } = new();
        /// <summary>詳細画面で表示する項目ID。nullは既定表示を表す。</summary>
        public List<string>? VisibleDetailFieldIds { get; set; }
        /// <summary>カタログの安定IDによる詳細項目順。</summary>
        public List<string> DetailFieldOrder { get; set; } = new();
        public string Language { get; set; } = string.Empty;
        public bool IncludeSubfolders { get; set; } = true;
        /// <summary>前回開いていた動画フォルダのパス。</summary>
        public string LastFolderPath { get; set; } = string.Empty;
        /// <summary>動画一覧をサムネイル表示するかどうか。既存設定では詳細表示になる。</summary>
        public bool IsThumbnailView { get; set; } = false;
        /// <summary>タイルサイズ。0=小、1=標準、2=大、3=特大。</summary>
        public int ThumbnailTileSizeIndex { get; set; } = 1;
        /// <summary>任意の FFprobe 実行ファイル。空欄の場合は PATH 上の ffprobe を試す。</summary>
        public string FfprobePath { get; set; } = string.Empty;
        /// <summary>カスタム項目の定義のみを保持する。作品ごとの値は動画ファイル内に保存する。</summary>
        public List<CustomMetadataFieldDefinition> CustomMetadataFields { get; set; } = new();
    }

    /// <summary>
    /// ローカルの AppData フォルダに JSON 形式で設定を保存・読み込みするマネージャー。
    /// </summary>
    public static class SettingsManager
    {
        private static readonly object FileLock = new();

        private static readonly string SettingsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Archivio"
        );
        private static readonly string SettingsFile = Path.Combine(SettingsFolder, "settings.json");

        public static AppSettings LoadSettings()
        {
            lock (FileLock)
            {
                try
                {
                    if (File.Exists(SettingsFile))
                    {
                        var json = File.ReadAllText(SettingsFile);
                        var settings = JsonSerializer.Deserialize<AppSettings>(json);
                        if (settings != null)
                        {
                            return settings;
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Error("設定ファイルの読み込みに失敗しました", ex, SettingsFile);
                }

                return new AppSettings();
            }
        }

        public static void SaveSettings(AppSettings settings)
        {
            lock (FileLock)
            {
                try
                {
                    Directory.CreateDirectory(SettingsFolder);
                    var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(SettingsFile, json);
                }
                catch (Exception ex)
                {
                    AppLogger.Error("設定ファイルの保存に失敗しました", ex, SettingsFile);
                }
            }
        }
    }
}
