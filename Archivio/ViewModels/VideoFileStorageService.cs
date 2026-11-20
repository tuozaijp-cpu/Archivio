using Archivio.Models;
using Windows.Storage;

namespace Archivio.ViewModels;

/// <summary>StorageFileの取得とファイルシステムアクセスをVideoFileItemから分離する。</summary>
public static class VideoFileStorageService
{
    public static Task<StorageFile> GetFileAsync(VideoFileItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return StorageFile.GetFileFromPathAsync(item.FullPath).AsTask();
    }
}
