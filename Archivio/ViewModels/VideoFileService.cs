using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace Archivio.ViewModels
{
    public interface IVideoFileService
    {
        Task<IReadOnlyList<string>> EnumerateVideoFilePathsAsync(StorageFolder folder, bool includeSubfolders, CancellationToken cancellationToken);
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
        private IReadOnlyList<string>? _cachedPathList;

        public Task<IReadOnlyList<string>> EnumerateVideoFilePathsAsync(StorageFolder folder, bool includeSubfolders, CancellationToken cancellationToken)
        {
            // キャッシュが有効ならそれを返す
            if (_cachedPathList != null && _cachedFolderPath == folder.Path && _cachedIncludeSubfolders == includeSubfolders)
            {
                return Task.FromResult(_cachedPathList);
            }

            var result = new List<string>();
            try
            {
                var enumerationOption = includeSubfolders
                    ? System.IO.SearchOption.AllDirectories
                    : System.IO.SearchOption.TopDirectoryOnly;

                foreach (var path in Directory.EnumerateFiles(folder.Path, "*", enumerationOption))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (SupportedVideoExtensions.Contains(Path.GetExtension(path)))
                    {
                        result.Add(path);
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                AppLogger.Error("動画フォルダーの列挙に失敗しました", ex, folder.Path);
            }

            // 既存のStorageFileキャッシュは使用せず、パスだけをキャッシュする。
            _cachedFolderPath = folder.Path;
            _cachedIncludeSubfolders = includeSubfolders;
            _cachedPathList = result;
            return Task.FromResult<IReadOnlyList<string>>(result);
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
            _cachedPathList = null;
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
