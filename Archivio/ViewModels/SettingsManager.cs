using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

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
        public Dictionary<string, double> ColumnWidths { get; set; } = new();
        public bool IncludeSubfolders { get; set; } = true;
        /// <summary>任意の FFprobe 実行ファイル。空欄の場合は PATH 上の ffprobe を試す。</summary>
        public string FfprobePath { get; set; } = string.Empty;
    }

    /// <summary>
    /// ローカルの AppData フォルダに JSON 形式で設定を保存・読み込みするマネージャー。
    /// </summary>
    public static class SettingsManager
    {
        private static readonly string SettingsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Archivio"
        );
        private static readonly string SettingsFile = Path.Combine(SettingsFolder, "settings.json");

        public static AppSettings LoadSettings()
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

        public static void SaveSettings(AppSettings settings)
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
