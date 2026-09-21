using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace Archivio.ViewModels
{
    public interface IVideoFileService
    {
        Task<IReadOnlyList<StorageFile>> EnumerateVideoFilesAsync(StorageFolder folder, bool includeSubfolders, CancellationToken cancellationToken);
        Task<ulong> GetFileSizeAsync(StorageFile file);
        void ClearEnumerationCache();
    }

    public sealed class VideoFileService : IVideoFileService
    {
        private static readonly HashSet<string> SupportedVideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".m4v", ".mov", ".mkv", ".avi", ".wmv", ".webm", ".mpeg", ".mpg",
            ".ts", ".m2ts", ".mts", ".3gp", ".3g2", ".flv", ".ogv", ".vob", ".asf"
        };

        // ファイル列挙結果のキャッシュ
        private string _cachedFolderPath = string.Empty;
        private bool _cachedIncludeSubfolders = false;
        private IReadOnlyList<StorageFile>? _cachedFileList = null;

        public async Task<IReadOnlyList<StorageFile>> EnumerateVideoFilesAsync(StorageFolder folder, bool includeSubfolders, CancellationToken cancellationToken)
        {
            // キャッシュが有効ならそれを返す
            if (_cachedFileList != null && _cachedFolderPath == folder.Path && _cachedIncludeSubfolders == includeSubfolders)
            {
                return _cachedFileList;
            }

            var result = new List<StorageFile>();
            var folders = new Queue<StorageFolder>();
            folders.Enqueue(folder);

            while (folders.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var currentFolder = folders.Dequeue();
                try
                {
                    foreach (var item in await currentFolder.GetFilesAsync())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (IsSupportedVideoFile(item))
                        {
                            result.Add(item);
                        }
                    }

                    if (includeSubfolders)
                    {
                        foreach (var childFolder in await currentFolder.GetFoldersAsync())
                        {
                            folders.Enqueue(childFolder);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    AppLogger.Error("動画フォルダーの列挙に失敗しました", ex, currentFolder.Path);
                }
            }

            // キャッシュに保存
            _cachedFolderPath = folder.Path;
            _cachedIncludeSubfolders = includeSubfolders;
            _cachedFileList = result;

            return result;
        }

        public async Task<ulong> GetFileSizeAsync(StorageFile file)
        {
            try
            {
                var basicProperties = await file.GetBasicPropertiesAsync();
                return basicProperties.Size;
            }
            catch (Exception ex)
            {
                AppLogger.Error("ファイルサイズの取得に失敗しました", ex, file.Path);
                return 0;
            }
        }

        public void ClearEnumerationCache()
        {
            _cachedFileList = null;
            _cachedFolderPath = string.Empty;
            _cachedIncludeSubfolders = false;
        }

        private bool IsSupportedVideoFile(StorageFile file)
        {
            var extension = System.IO.Path.GetExtension(file.Path);
            return SupportedVideoExtensions.Contains(extension);
        }
    }
}
