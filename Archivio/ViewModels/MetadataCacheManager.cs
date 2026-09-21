using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Archivio.ViewModels
{
    public sealed class FolderCacheData
    {
        public int Version { get; set; } = 1;
        public string FolderPath { get; set; } = string.Empty;
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
        public Dictionary<string, CacheEntry> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class CacheEntry
    {
        public DateTimeOffset LastWriteTime { get; set; }
        public ulong FileSize { get; set; }
        public VideoMetadataSnapshot Metadata { get; set; } = new();
    }

    public static class MetadataCacheManager
    {
        private static readonly string CacheFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Archivio",
            "Cache"
        );

        private static readonly SemaphoreSlim CacheLock = new(1, 1);
        private static bool _isGcExecuted;

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = false
        };

        public static string GetCacheFilePath(string folderPath)
        {
            var normalizedPath = folderPath.Trim().Replace('/', '\\');
            if (normalizedPath.Length > 3 || !normalizedPath.EndsWith(":\\"))
            {
                normalizedPath = normalizedPath.TrimEnd('\\');
            }
            normalizedPath = normalizedPath.ToLowerInvariant();

            var pathBytes = Encoding.UTF8.GetBytes(normalizedPath);
            var hashBytes = System.Security.Cryptography.MD5.HashData(pathBytes);
            var fileName = Convert.ToHexString(hashBytes).ToLowerInvariant() + ".json";
            return Path.Combine(CacheFolder, fileName);
        }

        // ロックフリーの内部用メソッド（データ一貫性を保証するためトランザクション内で呼び出される）
        private static async Task<FolderCacheData?> LoadCacheInternalAsync(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath)) return null;

            try
            {
                var cacheFile = GetCacheFilePath(folderPath);
                if (!File.Exists(cacheFile))
                {
                    return null;
                }

                var json = await File.ReadAllTextAsync(cacheFile);
                var cacheData = JsonSerializer.Deserialize<FolderCacheData>(json, SerializerOptions);
                return cacheData;
            }
            catch (Exception ex)
            {
                AppLogger.Error("キャッシュファイルの読み込みに失敗しました", ex, folderPath);
                return null;
            }
        }

        private static async Task SaveCacheInternalAsync(string folderPath, FolderCacheData cacheData)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || cacheData == null) return;

            string? tempFile = null;
            try
            {
                if (!Directory.Exists(CacheFolder))
                {
                    Directory.CreateDirectory(CacheFolder);
                }

                var cacheFile = GetCacheFilePath(folderPath);
                tempFile = cacheFile + ".tmp";

                var json = JsonSerializer.Serialize(cacheData, SerializerOptions);

                await File.WriteAllTextAsync(tempFile, json, Encoding.UTF8);

                if (File.Exists(cacheFile))
                {
                    File.Delete(cacheFile);
                }
                File.Move(tempFile, cacheFile);
            }
            catch (Exception ex)
            {
                AppLogger.Error("キャッシュファイルの書き込みに失敗しました", ex, folderPath);
                if (tempFile != null && File.Exists(tempFile))
                {
                    try { File.Delete(tempFile); } catch { }
                }
            }
        }

        // スレッドセーフロックを持つ公開メソッド
        public static async Task<FolderCacheData?> LoadCacheAsync(string folderPath)
        {
            await CacheLock.WaitAsync();
            try
            {
                return await LoadCacheInternalAsync(folderPath);
            }
            finally
            {
                CacheLock.Release();
            }
        }

        public static async Task SaveCacheAsync(string folderPath, FolderCacheData cacheData)
        {
            await CacheLock.WaitAsync();
            try
            {
                await SaveCacheInternalAsync(folderPath, cacheData);
            }
            finally
            {
                CacheLock.Release();
            }
        }

        public static async Task UpdateEntryAsync(string folderPath, string filePath, DateTimeOffset lastWriteTime, ulong fileSize, VideoMetadataSnapshot metadata)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || string.IsNullOrWhiteSpace(filePath)) return;

            await CacheLock.WaitAsync();
            try
            {
                var cacheData = await LoadCacheInternalAsync(folderPath) ?? new FolderCacheData { FolderPath = folderPath };
                cacheData.LastUpdated = DateTime.UtcNow;
                cacheData.Entries[filePath] = new CacheEntry
                {
                    LastWriteTime = lastWriteTime,
                    FileSize = fileSize,
                    Metadata = metadata
                };

                await SaveCacheInternalAsync(folderPath, cacheData);
            }
            finally
            {
                CacheLock.Release();
            }
        }

        public static async Task RenameEntryAsync(string folderPath, string oldFilePath, string newFilePath, DateTimeOffset lastWriteTime, ulong fileSize, VideoMetadataSnapshot metadata)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || string.IsNullOrWhiteSpace(oldFilePath) || string.IsNullOrWhiteSpace(newFilePath)) return;

            await CacheLock.WaitAsync();
            try
            {
                var cacheData = await LoadCacheInternalAsync(folderPath) ?? new FolderCacheData { FolderPath = folderPath };
                cacheData.LastUpdated = DateTime.UtcNow;
                
                // 古いエントリを消去
                cacheData.Entries.Remove(oldFilePath);

                // 新しいエントリを追加
                cacheData.Entries[newFilePath] = new CacheEntry
                {
                    LastWriteTime = lastWriteTime,
                    FileSize = fileSize,
                    Metadata = metadata
                };

                await SaveCacheInternalAsync(folderPath, cacheData);
            }
            finally
            {
                CacheLock.Release();
            }
        }

        public static async Task RemoveEntryAsync(string folderPath, string filePath)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || string.IsNullOrWhiteSpace(filePath)) return;

            await CacheLock.WaitAsync();
            try
            {
                var cacheData = await LoadCacheInternalAsync(folderPath);
                if (cacheData != null && cacheData.Entries.Remove(filePath))
                {
                    cacheData.LastUpdated = DateTime.UtcNow;
                    await SaveCacheInternalAsync(folderPath, cacheData);
                }
            }
            finally
            {
                CacheLock.Release();
            }
        }

        public static async Task ClearCacheForFolderAsync(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath)) return;

            await CacheLock.WaitAsync();
            try
            {
                var cacheFile = GetCacheFilePath(folderPath);
                if (File.Exists(cacheFile))
                {
                    File.Delete(cacheFile);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("キャッシュファイルの削除に失敗しました", ex, folderPath);
            }
            finally
            {
                CacheLock.Release();
            }
        }

        public static async Task GarbageCollectCacheAsync()
        {
            await CacheLock.WaitAsync();
            try
            {
                if (_isGcExecuted)
                {
                    return;
                }
                _isGcExecuted = true;

                if (!Directory.Exists(CacheFolder))
                {
                    return;
                }

                var files = Directory.GetFiles(CacheFolder, "*.json");
                foreach (var file in files)
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(file);
                        using var doc = JsonDocument.Parse(json);
                        // 単にJSONとして破損していないか（パース可能か）のみ確認し、
                        // 外付けHDDなどのフォルダ一時不在時には自動削除しないようにします。
                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            File.Delete(file);
                            AppLogger.Error("破損したキャッシュファイルを削除しました", ex, file);
                        }
                        catch
                        {
                            // Ignore
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("キャッシュのガベージコレクション中にエラーが発生しました", ex);
            }
            finally
            {
                CacheLock.Release();
            }
        }
    }
}