using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Archivio.Models;
using TagLib;
using Windows.Storage;

namespace Archivio.ViewModels
{
    internal sealed class TagLibTechnicalProperties
    {
        public TimeSpan? Duration { get; init; }
        public string FrameWidth { get; init; } = string.Empty;
        public string FrameHeight { get; init; } = string.Empty;
        public string FrameRate { get; init; } = string.Empty;
        public string VideoBitrate { get; init; } = string.Empty;
        public string AudioSampleRate { get; init; } = string.Empty;
        public string AudioBitrate { get; init; } = string.Empty;
        public string AudioFormat { get; init; } = string.Empty;
    }

    internal sealed class EmbeddedVideoMetadataLoadResult
    {
        public VideoMetadataSnapshot Metadata { get; init; } = new();
        public TagLibTechnicalProperties TechnicalProperties { get; init; } = new();
        public string? ArchivioMetadataError { get; init; }
    }

    internal interface IEmbeddedVideoMetadataStore
    {
        Task<EmbeddedVideoMetadataLoadResult> LoadAsync(StorageFile file);
        Task<VideoMetadataOperationResult> SaveAsync(
            StorageFile file,
            VideoMetadataSnapshot metadata,
            VideoMetadataSnapshot? originalMetadata,
            IEnumerable<string> changedProperties);
        Task<IReadOnlyList<CoverArtImageData>> LoadCoverArtImagesAsync(StorageFile file);
        Task<CoverArtImageData?> LoadThumbnailImageAsync(StorageFile file, CancellationToken cancellationToken);
        Task<VideoMetadataOperationResult> SaveCoverArtAsync(StorageFile file, IEnumerable<CoverArtImageData> coverArtImages);
    }

    internal sealed class EmbeddedVideoMetadataStore : IEmbeddedVideoMetadataStore
    {
        public Task<EmbeddedVideoMetadataLoadResult> LoadAsync(StorageFile file)
        {
            return Task.Run(() =>
            {
                var snapshot = new VideoMetadataSnapshot();
                var technicalProperties = new TagLibTechnicalProperties();
                var adapter = EmbeddedMetadataFormatAdapterSelector.Select(file.Path);
                try
                {
                    using var tagFile = TagLib.File.Create(file.Path);
                    if (tagFile.Tag is null)
                    {
                        return new EmbeddedVideoMetadataLoadResult { Metadata = snapshot };
                    }

                    var document = adapter.ReadMetadataDocument(tagFile);
                    var archivioResult = adapter.ReadArchivioMetadata(tagFile);
                    if (archivioResult.Payload is not null)
                    {
                        ArchivioMetadataPayloadMapper.ApplyToDocument(document, archivioResult.Payload);
                    }
                    snapshot = MetadataDocumentMapper.ToSnapshot(document);
                    technicalProperties = ReadTechnicalProperties(tagFile);
                    return new EmbeddedVideoMetadataLoadResult
                    {
                        Metadata = snapshot,
                        TechnicalProperties = technicalProperties,
                        ArchivioMetadataError = archivioResult.Error
                    };
                }
                catch (Exception ex)
                {
                    AppLogger.Error("ファイル内タグの読み込みに失敗しました", ex, file.Path);
                }

                return new EmbeddedVideoMetadataLoadResult
                {
                    Metadata = snapshot,
                    TechnicalProperties = technicalProperties
                };
            });
        }

        public Task<VideoMetadataOperationResult> SaveAsync(
            StorageFile file,
            VideoMetadataSnapshot metadata,
            VideoMetadataSnapshot? originalMetadata,
            IEnumerable<string> changedProperties)
        {
            return MetadataSaveCoordinator.ExecuteAsync(file, metadata, originalMetadata, changedProperties);
        }

        public async Task<IReadOnlyList<CoverArtImageData>> LoadCoverArtImagesAsync(StorageFile file)
        {
            return await Task.Run<IReadOnlyList<CoverArtImageData>>(() =>
            {
                var images = new List<CoverArtImageData>();
                try
                {
                    using var tagFile = TagLib.File.Create(file.Path);
                    foreach (var picture in tagFile.Tag.Pictures)
                    {
                        if (picture?.Data?.Data is null)
                        {
                            continue;
                        }

                        images.Add(new CoverArtImageData
                        {
                            Data = picture.Data.Data,
                            MimeType = picture.MimeType ?? "image/jpeg",
                            Description = picture.Description ?? string.Empty,
                            Type = picture.Type
                        });
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Error("カバー画像の読み込みに失敗しました", ex, file.Path);
                }

                return images;
            });
        }

        public async Task<CoverArtImageData?> LoadThumbnailImageAsync(StorageFile file, CancellationToken cancellationToken)
        {
            return await Task.Run(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var tagFile = TagLib.File.Create(file.Path);
                foreach (var picture in tagFile.Tag.Pictures)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (picture?.Data?.Data is null || picture.Data.Data.Length == 0)
                    {
                        continue;
                    }

                    var mimeType = picture.MimeType ?? "image/jpeg";
                    var (thumbnailData, thumbnailMimeType) = await VideoThumbnailImageProcessor.ResizeAsync(
                        picture.Data.Data,
                        mimeType,
                        cancellationToken);
                    return new CoverArtImageData
                    {
                        Data = thumbnailData,
                        MimeType = thumbnailMimeType,
                        Description = picture.Description ?? string.Empty,
                        Type = picture.Type
                    };
                }

                return null;
            }, cancellationToken);
        }

        public async Task<VideoMetadataOperationResult> SaveCoverArtAsync(
            StorageFile file,
            IEnumerable<CoverArtImageData> coverArtImages)
        {
            return await Task.Run(() =>
            {
                var directWriteMode = SettingsManager.LoadSettings().UseDirectFileWriteMode;
                try
                {
                    using var commit = FileCommitService.Open(file.Path, directWriteMode);

                    using (var tagFile = TagLib.File.Create(commit.WorkingPath))
                    {
                        var pictures = new List<IPicture>();
                        foreach (var coverArt in coverArtImages ?? Array.Empty<CoverArtImageData>())
                        {
                            pictures.Add(new TagLib.Picture(new ByteVector(coverArt?.Data ?? Array.Empty<byte>()))
                            {
                                MimeType = coverArt?.MimeType ?? "image/jpeg",
                                Description = coverArt?.Description ?? string.Empty,
                                Type = coverArt?.Type ?? PictureType.FrontCover
                            });
                        }

                        tagFile.Tag.Pictures = pictures.ToArray();
                        tagFile.Save();
                    }
                    // 一時ファイルからの読み込みを検証と保存後の表示データ生成に兼用する。
                    var reloaded = ReadCoverArtImages(commit.WorkingPath);
                    commit.Commit();
                    return new VideoMetadataOperationResult
                    {
                        Succeeded = true,
                        Message = LanguageManager.GetString("Msg_CoverImageSaved"),
                        ReloadedCoverArtImages = reloaded
                    };
                }
                catch (Exception ex)
                {
                    AppLogger.Error("カバー画像の保存に失敗しました", ex, file.Path);
                    return new VideoMetadataOperationResult
                    {
                        Succeeded = false,
                        Message = LanguageManager.GetString("Msg_CoverImageSaveFailed"),
                        Details = ex.Message
                    };
                }
            });
        }

        private static IReadOnlyList<CoverArtImageData> ReadCoverArtImages(string path)
        {
            using var savedFile = TagLib.File.Create(path);
            return savedFile.Tag.Pictures
                .Where(picture => picture?.Data?.Data is not null)
                .Select(picture => new CoverArtImageData
                {
                    Data = picture!.Data.Data,
                    MimeType = picture.MimeType ?? "image/jpeg",
                    Description = picture.Description ?? string.Empty,
                    Type = picture.Type
                })
                .ToList();
        }

        private static TagLibTechnicalProperties ReadTechnicalProperties(TagLib.File tagFile)
        {
            if (tagFile.Properties is null)
            {
                return new TagLibTechnicalProperties();
            }

            var properties = tagFile.Properties;
            return new TagLibTechnicalProperties
            {
                Duration = properties.Duration > TimeSpan.Zero ? properties.Duration : null,
                FrameWidth = GetNumericStringProperty(properties, "VideoWidth"),
                FrameHeight = GetNumericStringProperty(properties, "VideoHeight"),
                FrameRate = GetNumericStringProperty(properties, "VideoFrameRate", "0.##"),
                VideoBitrate = GetNumericStringProperty(properties, "VideoBitrate"),
                AudioSampleRate = GetNumericStringProperty(properties, "AudioSampleRate"),
                AudioBitrate = GetNumericStringProperty(properties, "AudioBitrate"),
                AudioFormat = GetStringProperty(properties, "AudioFormat")
            };
        }

        private static string GetStringProperty(object instance, string propertyName)
        {
            try
            {
                var property = instance.GetType().GetProperty(propertyName);
                return property?.GetValue(instance)?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetNumericStringProperty(object instance, string propertyName, string format = "0")
        {
            try
            {
                var property = instance.GetType().GetProperty(propertyName);
                if (property?.GetValue(instance) is not { } value)
                {
                    return string.Empty;
                }

                return value switch
                {
                    int number when number > 0 => number.ToString(format),
                    uint number when number > 0 => number.ToString(format),
                    long number when number > 0 => number.ToString(format),
                    ulong number when number > 0 => number.ToString(format),
                    double number when number > 0 => number.ToString(format),
                    float number when number > 0 => number.ToString(format),
                    _ => string.Empty
                };
            }
            catch
            {
                return string.Empty;
            }
        }

    }
}
