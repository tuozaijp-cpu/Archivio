using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Archivio.ViewModels
{
    internal static class VideoThumbnailImageProcessor
    {
        private const uint MaxWidth = 360;
        private const uint MaxHeight = 320;

        public static async Task<(byte[] Data, string MimeType)> ResizeAsync(
            byte[] imageData,
            string mimeType,
            CancellationToken cancellationToken)
        {
            try
            {
                using var inputStream = new InMemoryRandomAccessStream();
                using (var writer = new DataWriter(inputStream.GetOutputStreamAt(0)))
                {
                    writer.WriteBytes(imageData);
                    await writer.StoreAsync();
                    await writer.FlushAsync();
                }

                inputStream.Seek(0);
                var decoder = await BitmapDecoder.CreateAsync(inputStream);
                var sourceWidth = decoder.OrientedPixelWidth;
                var sourceHeight = decoder.OrientedPixelHeight;
                if (sourceWidth == 0 || sourceHeight == 0 || (sourceWidth <= MaxWidth && sourceHeight <= MaxHeight))
                {
                    return (imageData, mimeType);
                }

                cancellationToken.ThrowIfCancellationRequested();
                var scale = Math.Min((double)MaxWidth / sourceWidth, (double)MaxHeight / sourceHeight);
                var scaledWidth = (uint)Math.Max(1, (int)Math.Round(sourceWidth * scale));
                var scaledHeight = (uint)Math.Max(1, (int)Math.Round(sourceHeight * scale));
                var transform = new BitmapTransform
                {
                    ScaledWidth = scaledWidth,
                    ScaledHeight = scaledHeight,
                    InterpolationMode = BitmapInterpolationMode.Fant
                };
                var pixelData = await decoder.GetPixelDataAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied,
                    transform,
                    ExifOrientationMode.RespectExifOrientation,
                    ColorManagementMode.DoNotColorManage);

                using var outputStream = new InMemoryRandomAccessStream();
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, outputStream);
                encoder.SetPixelData(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied,
                    scaledWidth,
                    scaledHeight,
                    decoder.DpiX,
                    decoder.DpiY,
                    pixelData.DetachPixelData());
                await encoder.FlushAsync();
                cancellationToken.ThrowIfCancellationRequested();

                outputStream.Seek(0);
                var thumbnailData = new byte[(int)outputStream.Size];
                using var reader = new DataReader(outputStream.GetInputStreamAt(0));
                await reader.LoadAsync((uint)thumbnailData.Length);
                reader.ReadBytes(thumbnailData);
                return (thumbnailData, "image/png");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLogger.Error("一覧用サムネイルの縮小に失敗しました", ex);
                return (imageData, mimeType);
            }
        }
    }
}