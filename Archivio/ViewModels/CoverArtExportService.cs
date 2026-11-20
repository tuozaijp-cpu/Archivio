using Windows.Storage;
using Windows.Storage.Streams;

namespace Archivio.ViewModels;

/// <summary>カバー画像の保存先への書き出しだけを担当する。</summary>
public sealed class CoverArtExportService
{
    public Task ExportAsync(StorageFile destination, byte[] imageData)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(imageData);
        return FileIO.WriteBytesAsync(destination, imageData).AsTask();
    }
}
