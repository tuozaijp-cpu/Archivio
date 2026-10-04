using System;
using System.Collections.Generic;
using System.Globalization;
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
        private sealed record FileVersion(long Length, DateTime LastWriteTimeUtc);

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

        public async Task<VideoMetadataOperationResult> SaveAsync(
            StorageFile file,
            VideoMetadataSnapshot metadata,
            VideoMetadataSnapshot? originalMetadata,
            IEnumerable<string> changedProperties)
        {
            var changedPropertyList = changedProperties?
                .Where(property => !string.IsNullOrWhiteSpace(property))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();
            if (changedPropertyList.Count == 0)
            {
                return new VideoMetadataOperationResult { Succeeded = true, Message = LanguageManager.GetString("Msg_NoChanges") };
            }

            return await Task.Run(() =>
            {
                var adapter = EmbeddedMetadataFormatAdapterSelector.Select(file.Path);
                var fieldsToPersist = new List<string>();
                AddChangedField(fieldsToPersist, changedPropertyList, "Title", metadata.Title, originalMetadata?.Title);
                AddChangedField(fieldsToPersist, changedPropertyList, "Participants", metadata.Participants, originalMetadata?.Participants);
                AddChangedField(fieldsToPersist, changedPropertyList, "Comment", metadata.Comment, originalMetadata?.Comment);
                AddChangedField(fieldsToPersist, changedPropertyList, "Category", metadata.Category, originalMetadata?.Category);
                AddChangedField(fieldsToPersist, changedPropertyList, "CatalogNumber", metadata.CatalogNumber, originalMetadata?.CatalogNumber);
                AddChangedField(fieldsToPersist, changedPropertyList, "Rating", metadata.Rating, originalMetadata?.Rating);
                AddChangedField(fieldsToPersist, changedPropertyList, "ContentDistributor", metadata.ContentDistributor, originalMetadata?.ContentDistributor);
                AddChangedField(fieldsToPersist, changedPropertyList, "Publisher", metadata.Publisher, originalMetadata?.Publisher);
                if (changedPropertyList.Contains("ReleaseDate")
                    && HasReleaseDateChanged(metadata.ReleaseDate, originalMetadata?.ReleaseDate))
                {
                    fieldsToPersist.Add("ReleaseDate");
                }

                // 将来の独自項目UIからは安定した項目IDをそのまま受け取れるようにする。
                fieldsToPersist.AddRange(changedPropertyList
                    .Where(ArchivioMetadataPayloadMapper.IsPayloadFieldId));

                if (fieldsToPersist.Count == 0)
                {
                    return new VideoMetadataOperationResult { Succeeded = true, Message = LanguageManager.GetString("Msg_NoChanges") };
                }

                var warningErrors = new List<string>();
                var fatalErrors = new List<string>();
                var archivioMetadataErrors = new List<string>();
                ArchivioMetadataPayload? archivioPayloadToVerify = null;
                VideoMetadataSnapshot? reloadedMetadata = null;
                string? temporaryPath = null;
                FileVersion? originalVersion = null;

                try
                {
                    originalVersion = ReadFileVersion(file.Path);
                    temporaryPath = CreateTemporaryPath(file.Path);
                    System.IO.File.Copy(file.Path, temporaryPath, overwrite: false);
                }
                catch (Exception ex)
                {
                    fatalErrors.Add(LanguageManager.GetString("Msg_SaveError", ex.Message));
                }

                var fieldCapabilities = fieldsToPersist
                    .Select(field =>
                    {
                        var fieldId = ResolveFieldId(field);
                        var support = adapter.GetFieldSupport(fieldId);
                        return new MetadataFieldCapabilityResult
                        {
                            FieldId = fieldId,
                            Capability = support.Capability,
                            CanRead = support.CanRead,
                            CanWrite = support.CanWrite,
                            FailureReason = support.FailureReason
                        };
                    })
                    .ToList();
                var unsupportedFields = fieldCapabilities
                    .Where(result => !result.CanWrite)
                    .Select(result => result.FieldId)
                    .ToList();
                if (unsupportedFields.Count > 0)
                {
                    warningErrors.Add(LanguageManager.GetString(
                        "Msg_UnsupportedCustomMetadata",
                        Path.GetExtension(file.Path),
                        string.Join("、", unsupportedFields)));
                    var writableFieldIds = fieldCapabilities
                        .Where(result => result.CanWrite)
                        .Select(result => result.FieldId)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    fieldsToPersist = fieldsToPersist
                        .Where(field => writableFieldIds.Contains(ResolveFieldId(field)))
                        .ToList();
                }

                try
                {
                    if (temporaryPath is null)
                    {
                        throw new InvalidOperationException("保存用一時ファイルを作成できませんでした。");
                    }

                    using var tagFile = TagLib.File.Create(temporaryPath);
                    if (tagFile.Tag is null)
                    {
                        fatalErrors.Add(LanguageManager.GetString("Msg_EmbeddedTagsUnavailable"));
                    }
                    else
                    {
                        var document = MetadataDocumentMapper.FromSnapshot(metadata);
                        var fieldIds = fieldsToPersist.Select(ResolveFieldId).ToArray();
                        adapter.WriteMetadataDocument(tagFile, document, fieldIds);

                        // 独自ペイロードは標準タグとは別領域へ保存する。空の互換スナップショットで
                        // 既存の独自データを上書きしないよう、独自データがある場合だけ書き込む。
                        if (ArchivioMetadataPayloadMapper.HasData(document)
                            || changedPropertyList.Any(ArchivioMetadataPayloadMapper.IsPayloadFieldId))
                        {
                            var payload = ArchivioMetadataPayloadMapper.FromDocument(document);
                            try
                            {
                                adapter.WriteArchivioMetadata(tagFile, payload);
                                archivioPayloadToVerify = payload;
                            }
                            catch (Exception ex)
                            {
                                AppLogger.Error("Archivio独自メタデータの保存に失敗しました", ex, file.Path);
                                archivioMetadataErrors.Add(ex.Message);
                                fatalErrors.Add(ex.Message);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Error("ファイル内タグの保存に失敗しました", ex, file.Path);
                    fatalErrors.Add(LanguageManager.GetString("Msg_SaveError", ex.Message));
                }

                if (archivioPayloadToVerify is not null && fatalErrors.Count == 0)
                {
                    try
                    {
                        VerifyArchivioMetadata(temporaryPath!, adapter, archivioPayloadToVerify);
                    }
                    catch (Exception ex)
                    {
                        archivioMetadataErrors.Add(ex.Message);
                    }
                }

                if (fieldsToPersist.Count > 0 && fatalErrors.Count == 0)
                {
                    try
                    {
                        VerifyEmbeddedMetadata(temporaryPath!, metadata, fieldsToPersist, adapter);
                    }
                    catch (Exception ex)
                    {
                        fatalErrors.Add(LanguageManager.GetString("Msg_VerifyMetadataFailed", ex.Message));
                    }
                }

                if (fatalErrors.Count == 0)
                {
                    try
                    {
                        EnsureFileUnchanged(file.Path, originalVersion!);
                        CommitTemporaryFile(temporaryPath!, file.Path);
                        reloadedMetadata = ReadMetadataSnapshot(file.Path, adapter);
                    }
                    catch (Exception ex)
                    {
                        fatalErrors.Add(LanguageManager.GetString("Msg_SaveError", ex.Message));
                    }
                }

                if (temporaryPath is not null)
                {
                    TryDeleteTemporaryFile(temporaryPath);
                }

                if (fatalErrors.Count > 0)
                {
                    fieldCapabilities = fieldCapabilities
                        .Select(result => result.CanWrite
                            ? new MetadataFieldCapabilityResult
                            {
                                FieldId = result.FieldId,
                                Capability = MetadataFieldCapability.Failed,
                                CanRead = result.CanRead,
                                CanWrite = false,
                                FailureReason = fatalErrors[0]
                            }
                            : result)
                        .ToList();
                }

                var succeeded = fatalErrors.Count == 0;
                var errors = warningErrors.Concat(archivioMetadataErrors).Concat(fatalErrors).ToList();
                return new VideoMetadataOperationResult
                {
                    Succeeded = succeeded,
                    HasWarnings = succeeded && (warningErrors.Count > 0 || archivioMetadataErrors.Count > 0),
                    Message = succeeded
                        ? warningErrors.Count > 0 || archivioMetadataErrors.Count > 0
                            ? LanguageManager.GetString("Msg_PartialSaveSuccess")
                            : LanguageManager.GetString("Msg_SaveSuccess")
                        : LanguageManager.GetString("Msg_SaveFailed"),
                    Details = errors.Count == 0 ? null : string.Join(Environment.NewLine, errors),
                    SavedProperties = succeeded ? fieldsToPersist : Array.Empty<string>(),
                    FailedProperties = succeeded ? Array.Empty<string>() : fieldsToPersist,
                    UnsupportedProperties = unsupportedFields,
                    FieldCapabilities = fieldCapabilities,
                    ArchivioMetadataErrors = archivioMetadataErrors,
                    ReloadedMetadata = succeeded ? reloadedMetadata : null
                };
            });
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
                string? temporaryPath = null;
                try
                {
                    var originalVersion = ReadFileVersion(file.Path);
                    temporaryPath = CreateTemporaryPath(file.Path);
                    System.IO.File.Copy(file.Path, temporaryPath, overwrite: false);
                    using (var tagFile = TagLib.File.Create(temporaryPath))
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
                    VerifyCoverArt(temporaryPath);
                    EnsureFileUnchanged(file.Path, originalVersion);
                    CommitTemporaryFile(temporaryPath, file.Path);
                    var reloaded = ReadCoverArtImages(file.Path);
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
                finally
                {
                    if (temporaryPath is not null)
                    {
                        TryDeleteTemporaryFile(temporaryPath);
                    }
                }
            });
        }

        private static FileVersion ReadFileVersion(string path)
        {
            var fileInfo = new FileInfo(path);
            if (!fileInfo.Exists)
            {
                throw new FileNotFoundException("対象ファイルが存在しません。", path);
            }

            return new FileVersion(fileInfo.Length, fileInfo.LastWriteTimeUtc);
        }

        private static string CreateTemporaryPath(string path)
        {
            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidOperationException("対象ファイルの保存先を特定できません。");
            }

            return Path.Combine(directory, $".{Path.GetFileNameWithoutExtension(path)}.archivio-{Guid.NewGuid():N}{Path.GetExtension(path)}");
        }

        private static void EnsureFileUnchanged(string path, FileVersion expected)
        {
            var actual = ReadFileVersion(path);
            if (actual.Length != expected.Length || actual.LastWriteTimeUtc != expected.LastWriteTimeUtc)
            {
                throw new InvalidOperationException("保存中に外部アプリケーションがファイルを変更したため、上書きしませんでした。再読み込みしてください。");
            }
        }

        private static void CommitTemporaryFile(string temporaryPath, string targetPath)
        {
            // 一時ファイルは同一フォルダー・同一拡張子で作成し、Windowsの置換操作を使う。
            System.IO.File.Replace(temporaryPath, targetPath, null, ignoreMetadataErrors: true);
        }

        private static void TryDeleteTemporaryFile(string path)
        {
            try
            {
                if (System.IO.File.Exists(path))
                {
                    System.IO.File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("保存用一時ファイルの削除に失敗しました", ex, path);
            }
        }

        private static VideoMetadataSnapshot ReadMetadataSnapshot(string path, IEmbeddedMetadataFormatAdapter adapter)
        {
            using var savedFile = TagLib.File.Create(path);
            var document = adapter.ReadMetadataDocument(savedFile);
            var archivioResult = adapter.ReadArchivioMetadata(savedFile);
            if (archivioResult.Payload is not null)
            {
                ArchivioMetadataPayloadMapper.ApplyToDocument(document, archivioResult.Payload);
            }

            return MetadataDocumentMapper.ToSnapshot(document);
        }

        private static void VerifyCoverArt(string path)
        {
            using var savedFile = TagLib.File.Create(path);
            _ = savedFile.Tag.Pictures.Count();
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

        private static void AddChangedField(
            ICollection<string> fields,
            ICollection<string> requestedFields,
            string field,
            string? currentValue,
            string? originalValue)
        {
            if (requestedFields.Contains(field) && HasValueChanged(currentValue, originalValue))
            {
                fields.Add(field);
            }
        }

        private static bool HasValueChanged(string? currentValue, string? originalValue)
        {
            return !string.Equals(currentValue ?? string.Empty, originalValue ?? string.Empty, StringComparison.Ordinal);
        }

        private static bool HasReleaseDateChanged(DateTimeOffset currentValue, DateTimeOffset? originalValue)
        {
            if (originalValue is null)
            {
                return currentValue.Year > 1900;
            }

            return currentValue.Date != originalValue.Value.Date;
        }

        private static void VerifyEmbeddedMetadata(
            string path,
            VideoMetadataSnapshot expected,
            IEnumerable<string> fields,
            IEmbeddedMetadataFormatAdapter adapter)
        {
            using var savedFile = TagLib.File.Create(path);
            var actual = MetadataDocumentMapper.ToSnapshot(adapter.ReadMetadataDocument(savedFile));

            foreach (var field in fields)
            {
                var matched = field switch
                {
                    "Title" => string.Equals(actual.Title, expected.Title, StringComparison.Ordinal),
                    "Participants" => string.Equals(actual.Participants, expected.Participants, StringComparison.Ordinal),
                    "Comment" => string.Equals(actual.Comment, expected.Comment, StringComparison.Ordinal),
                    "Category" => string.Equals(actual.Category, expected.Category, StringComparison.Ordinal),
                    "CatalogNumber" => string.Equals(actual.CatalogNumber, expected.CatalogNumber, StringComparison.Ordinal),
                    "Publisher" => string.Equals(actual.Publisher, expected.Publisher, StringComparison.Ordinal),
                    "ContentDistributor" => string.Equals(actual.ContentDistributor, expected.ContentDistributor, StringComparison.Ordinal),
                    "Rating" => string.Equals(actual.Rating, expected.Rating, StringComparison.Ordinal),
                    "ReleaseDate" => actual.ReleaseDate.Date == expected.ReleaseDate.Date,
                    _ => true
                };

                if (!matched)
                {
                    throw new InvalidDataException($"{field} が保存後に一致しません。");
                }
            }
        }

        private static string ResolveFieldId(string propertyName)
        {
            var field = MetadataFieldCatalog.All.FirstOrDefault(definition =>
                string.Equals(definition.DetailsBindingPath, propertyName, StringComparison.Ordinal)
                || string.Equals(definition.ExistingColumnTag, propertyName, StringComparison.Ordinal)
                || string.Equals(definition.ListBindingPath, propertyName, StringComparison.Ordinal));
            return field?.Id ?? propertyName;
        }

        private static void VerifyArchivioMetadata(
            string path,
            IEmbeddedMetadataFormatAdapter adapter,
            ArchivioMetadataPayload expected)
        {
            using var savedFile = TagLib.File.Create(path);
            var result = adapter.ReadArchivioMetadata(savedFile);
            if (result.Error is not null || result.Payload is null)
            {
                throw new InvalidDataException(result.Error ?? "Archivio独自メタデータを読み戻せません。");
            }

            var expectedJson = ArchivioMetadataPayloadMapper.Serialize(expected);
            var actualJson = ArchivioMetadataPayloadMapper.Serialize(result.Payload);
            if (!string.Equals(expectedJson, actualJson, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Archivio独自メタデータの保存後検証に失敗しました。");
            }
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
