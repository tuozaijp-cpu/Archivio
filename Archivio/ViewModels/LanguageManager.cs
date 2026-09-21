using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Archivio.ViewModels
{
    public static class LanguageManager
    {
        private static readonly Dictionary<string, string> Strings = new(StringComparer.OrdinalIgnoreCase);
        private static string _currentLanguage = "en";

        public static string CurrentLanguage => _currentLanguage;

        public static void Initialize()
        {
            var settings = SettingsManager.LoadSettings();
            string lang = settings.Language;

            if (string.IsNullOrWhiteSpace(lang))
            {
                // Auto-detect from system culture
                lang = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
                if (lang != "ja")
                {
                    lang = "en"; // default fallback
                }
            }

            LoadLanguage(lang);
        }

        public static void LoadLanguage(string langCode)
        {
            Strings.Clear();
            _currentLanguage = langCode;

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string enFile = Path.Combine(baseDir, "Assets", "Locale", "en.json");
            string targetFile = Path.Combine(baseDir, "Assets", "Locale", $"{langCode}.json");

            // 1. Load English as base fallback
            LoadJsonFile(enFile);

            // 2. Overlay target language if different
            if (langCode != "en")
            {
                LoadJsonFile(targetFile);
            }
        }

        private static void LoadJsonFile(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    var json = File.ReadAllText(filePath);
                    using var doc = JsonDocument.Parse(json);
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.String)
                        {
                            Strings[prop.Name] = prop.Value.GetString() ?? string.Empty;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error($"言語ファイルのロードに失敗しました: {filePath}", ex);
            }
        }

        public static string GetString(string key)
        {
            if (Strings.TryGetValue(key, out var value))
            {
                return value;
            }
            return key; // Fallback to key itself
        }

        public static string GetString(string key, params object[] args)
        {
            var val = GetString(key);
            try
            {
                return string.Format(val, args);
            }
            catch
            {
                return val;
            }
        }
    }
}
