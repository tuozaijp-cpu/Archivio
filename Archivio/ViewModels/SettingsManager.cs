using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Linq;
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
        /// <summary>タグ情報パネルの列数。1列または2列。</summary>
        public int DetailFieldColumnCount { get; set; } = 1;
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
        /// <summary>メタデータからファイル名を生成するテンプレート。安定した項目IDを使用します。</summary>
        public string FileNameTemplate { get; set; } = "[{credits.cast.0}]{work.title}[{identifiers.catalog-number}]";
        /// <summary>動画一覧をサムネイル表示するかどうか。既存設定では詳細表示になる。</summary>
        public bool IsThumbnailView { get; set; } = false;
        /// <summary>タイルサイズ。0=小、1=標準、2=大、3=特大。</summary>
        public int ThumbnailTileSizeIndex { get; set; } = 1;
        /// <summary>任意の FFprobe 実行ファイル。空欄の場合は PATH 上の ffprobe を試す。</summary>
        public string FfprobePath { get; set; } = string.Empty;
        /// <summary>動画ファイルを一時ファイルへ複製せず、実ファイルへ直接保存する危険な高速モード。</summary>
        public bool UseDirectFileWriteMode { get; set; } = false;
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
                            NormalizeMetadataLayoutSettings(settings);
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

        /// <summary>
        /// 旧設定の表示名ベースの列設定を、変更されないメタデータ項目IDへ移行します。
        /// 不明なIDは保存済み設定全体を壊さず、メタデータ表示設定からだけ除外します。
        /// </summary>
        internal static void NormalizeMetadataLayoutSettings(AppSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            settings.ColumnOrder ??= new List<string>();
            settings.ColumnWidths ??= new Dictionary<string, double>();
            settings.ListFieldOrder ??= new List<string>();
            settings.ListFieldWidths ??= new Dictionary<string, double>();
            settings.DetailFieldOrder ??= new List<string>();

            // All を初回参照する際に SettingsManager.LoadSettings() を再入しないよう、
            // この設定に含まれるカスタム項目を先にカタログへ反映します。
            MetadataFieldCatalog.RefreshCustomFields(settings.CustomMetadataFields);
            var listFields = MetadataFieldCatalog.ListFields;
            var legacyIds = listFields
                .Where(field => field.ExistingColumnTag is not null)
                .ToDictionary(field => field.ExistingColumnTag!, field => field.Id, StringComparer.Ordinal);

            if (settings.ListFieldOrder.Count == 0 && settings.ColumnOrder.Count > 0)
            {
                settings.ListFieldOrder = settings.ColumnOrder
                    .Where(legacyIds.ContainsKey)
                    .Select(tag => legacyIds[tag])
                    .ToList();
            }

            foreach (var field in listFields)
            {
                if (field.ExistingColumnTag is { } legacyTag
                    && !settings.ListFieldWidths.ContainsKey(field.Id)
                    && settings.ColumnWidths.TryGetValue(legacyTag, out var width))
                {
                    settings.ListFieldWidths[field.Id] = width;
                }
            }

            var knownListIds = listFields.Select(field => field.Id).ToHashSet(StringComparer.Ordinal);
            settings.ListFieldOrder = settings.ListFieldOrder
                .Where(knownListIds.Contains)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            settings.VisibleListFieldIds = settings.VisibleListFieldIds is null
                ? null
                : settings.VisibleListFieldIds
                    .Where(knownListIds.Contains)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

            var knownDetailIds = MetadataFieldCatalog.DetailFields.Select(field => field.Id).ToHashSet(StringComparer.Ordinal);
            settings.DetailFieldOrder = settings.DetailFieldOrder
                .Where(knownDetailIds.Contains)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            settings.VisibleDetailFieldIds = settings.VisibleDetailFieldIds is null
                ? null
                : settings.VisibleDetailFieldIds
                    .Where(knownDetailIds.Contains)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
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
