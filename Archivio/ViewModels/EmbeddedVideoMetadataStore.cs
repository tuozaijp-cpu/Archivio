using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        private const string ArchivioTagOwner = "com.archivio";
        private const string ReleaseDateTagName = "ReleaseDate";
        private const string RatingTagName = "Rating";

        public Task<EmbeddedVideoMetadataLoadResult> LoadAsync(StorageFile file)
        {
            return Task.Run(() =>
            {
                var snapshot = new VideoMetadataSnapshot();
                var technicalProperties = new TagLibTechnicalProperties();
                try
                {
                    using var tagFile = TagLib.File.Create(file.Path);
                    if (tagFile.Tag is null)
                    {
                        return new EmbeddedVideoMetadataLoadResult { Metadata = snapshot };
                    }

                    snapshot.Title = tagFile.Tag.Title ?? string.Empty;
                    snapshot.Participants = string.Join("; ", tagFile.Tag.Performers ?? Array.Empty<string>());
                    snapshot.Comment = tagFile.Tag.Comment ?? string.Empty;
                    snapshot.Category = string.Join("; ", tagFile.Tag.Genres ?? Array.Empty<string>());
                    snapshot.CatalogNumber = tagFile.Tag.Grouping ?? string.Empty;
                    snapshot.ContentDistributor = tagFile.Tag.Copyright ?? string.Empty;

                    var extension = Path.GetExtension(file.Path);
                    if (IsMatroska(extension))
                    {
                        var matroskaTag = tagFile.GetTag(TagTypes.Matroska, create: false) as TagLib.Matroska.Tag;
                        snapshot.Publisher = GetMatroskaCustomText(matroskaTag, "PUBLISHER");
                        if (string.IsNullOrWhiteSpace(snapshot.Publisher))
                        {
                            snapshot.Publisher = tagFile.Tag.Publisher ?? string.Empty;
                        }
                    }
                    else
                    {
                        snapshot.Publisher = tagFile.Tag.Publisher ?? string.Empty;
                    }

                    snapshot.Rating = ReadCustomText(tagFile, file.Path, RatingTagName);
                    snapshot.ReleaseDate = ReadReleaseDate(tagFile, file.Path);
                    snapshot.ReleaseDateText = snapshot.ReleaseDate.Year > 1900
                        ? snapshot.ReleaseDate.ToString("yyyy-MM-dd")
                        : string.Empty;
                    technicalProperties = ReadTechnicalProperties(tagFile);
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

                if (fieldsToPersist.Count == 0)
                {
                    return new VideoMetadataOperationResult { Succeeded = true, Message = LanguageManager.GetString("Msg_NoChanges") };
                }

                var warningErrors = new List<string>();
                var fatalErrors = new List<string>();
                var unsupportedCustomFields = fieldsToPersist
                    .Where(field => field is "Rating" or "ReleaseDate")
                    .Where(_ => !SupportsCustomArchivioTags(file.Path))
                    .ToList();
                if (unsupportedCustomFields.Count > 0)
                {
                    warningErrors.Add(LanguageManager.GetString(
                        "Msg_UnsupportedCustomMetadata",
                        Path.GetExtension(file.Path),
                        string.Join("、", unsupportedCustomFields)));
                    fieldsToPersist = fieldsToPersist.Except(unsupportedCustomFields, StringComparer.OrdinalIgnoreCase).ToList();
                }

                try
                {
                    using var tagFile = TagLib.File.Create(file.Path);
                    if (tagFile.Tag is null)
                    {
                        fatalErrors.Add(LanguageManager.GetString("Msg_EmbeddedTagsUnavailable"));
                    }
                    else
                    {
                        SaveStandardFields(tagFile, file.Path, metadata, fieldsToPersist);
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Error("ファイル内タグの保存に失敗しました", ex, file.Path);
                    fatalErrors.Add(LanguageManager.GetString("Msg_SaveError", ex.Message));
                }

                if (fieldsToPersist.Count > 0 && fatalErrors.Count == 0)
                {
                    try
                    {
                        VerifyEmbeddedMetadata(file.Path, metadata, fieldsToPersist);
                    }
                    catch (Exception ex)
                    {
                        fatalErrors.Add(LanguageManager.GetString("Msg_VerifyMetadataFailed", ex.Message));
                    }
                }

                var succeeded = fatalErrors.Count == 0;
                var errors = warningErrors.Concat(fatalErrors).ToList();
                return new VideoMetadataOperationResult
                {
                    Succeeded = succeeded,
                    HasWarnings = succeeded && warningErrors.Count > 0,
                    Message = succeeded
                        ? warningErrors.Count > 0
                            ? LanguageManager.GetString("Msg_PartialSaveSuccess")
                            : LanguageManager.GetString("Msg_SaveSuccess")
                        : LanguageManager.GetString("Msg_SaveFailed"),
                    Details = errors.Count == 0 ? null : string.Join(Environment.NewLine, errors),
                    SavedProperties = succeeded ? fieldsToPersist : Array.Empty<string>(),
                    FailedProperties = succeeded ? Array.Empty<string>() : fieldsToPersist,
                    UnsupportedProperties = unsupportedCustomFields
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
                try
                {
                    using var tagFile = TagLib.File.Create(file.Path);
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
                    return new VideoMetadataOperationResult { Succeeded = true, Message = LanguageManager.GetString("Msg_CoverImageSaved") };
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

        private static void SaveStandardFields(
            TagLib.File tagFile,
            string path,
            VideoMetadataSnapshot metadata,
            IReadOnlyCollection<string> fields)
        {
            var isDirty = false;
            if (fields.Contains("Title"))
            {
                tagFile.Tag.Title = metadata.Title;
                isDirty = true;
            }

            if (fields.Contains("Participants"))
            {
                tagFile.Tag.Performers = SplitValues(metadata.Participants);
                isDirty = true;
            }

            if (fields.Contains("Comment"))
            {
                tagFile.Tag.Comment = metadata.Comment;
                isDirty = true;
            }

            if (fields.Contains("ReleaseDate"))
            {
                tagFile.Tag.Year = metadata.ReleaseDate.Year > 1900 ? (uint)metadata.ReleaseDate.Year : 0;
                SetCustomText(tagFile, path, ReleaseDateTagName,
                    metadata.ReleaseDate.Year > 1900
                        ? metadata.ReleaseDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                        : string.Empty);
                isDirty = true;
            }

            if (fields.Contains("Rating"))
            {
                SetCustomText(tagFile, path, RatingTagName, metadata.Rating ?? string.Empty);
                isDirty = true;
            }

            if (fields.Contains("Category"))
            {
                tagFile.Tag.Genres = SplitValues(metadata.Category);
                isDirty = true;
            }

            if (fields.Contains("CatalogNumber"))
            {
                tagFile.Tag.Grouping = metadata.CatalogNumber;
                isDirty = true;
            }

            if (fields.Contains("Publisher"))
            {
                if (IsMatroska(Path.GetExtension(path)))
                {
                    var matroskaTag = tagFile.GetTag(TagTypes.Matroska, create: true) as TagLib.Matroska.Tag
                        ?? throw new NotSupportedException("Matroska タグを作成できません。");
                    SetMatroskaCustomText(matroskaTag, "PUBLISHER", metadata.Publisher ?? string.Empty);
                }

                tagFile.Tag.Publisher = metadata.Publisher;
                isDirty = true;
            }

            if (fields.Contains("ContentDistributor"))
            {
                tagFile.Tag.Copyright = metadata.ContentDistributor;
                isDirty = true;
            }

            if (isDirty)
            {
                tagFile.Save();
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

        private static void VerifyEmbeddedMetadata(string path, VideoMetadataSnapshot expected, IEnumerable<string> fields)
        {
            using var savedFile = TagLib.File.Create(path);
            var tag = savedFile.Tag ?? throw new InvalidDataException("ファイル内タグを読み取れません。");

            foreach (var field in fields)
            {
                var matched = field switch
                {
                    "Title" => string.Equals(tag.Title ?? string.Empty, expected.Title ?? string.Empty, StringComparison.Ordinal),
                    "Participants" => tag.Performers.SequenceEqual(SplitValues(expected.Participants), StringComparer.Ordinal),
                    "Comment" => string.Equals(tag.Comment ?? string.Empty, expected.Comment ?? string.Empty, StringComparison.Ordinal),
                    "Category" => tag.Genres.SequenceEqual(SplitValues(expected.Category), StringComparer.Ordinal),
                    "CatalogNumber" => string.Equals(tag.Grouping ?? string.Empty, expected.CatalogNumber ?? string.Empty, StringComparison.Ordinal),
                    "Publisher" => string.Equals(
                        IsMatroska(Path.GetExtension(path))
                            ? GetMatroskaCustomText(savedFile.GetTag(TagTypes.Matroska, create: false) as TagLib.Matroska.Tag, "PUBLISHER")
                            : tag.Publisher ?? string.Empty,
                        expected.Publisher ?? string.Empty,
                        StringComparison.Ordinal),
                    "ContentDistributor" => string.Equals(tag.Copyright ?? string.Empty, expected.ContentDistributor ?? string.Empty, StringComparison.Ordinal),
                    "Rating" => string.Equals(ReadCustomText(savedFile, path, RatingTagName), expected.Rating ?? string.Empty, StringComparison.Ordinal),
                    "ReleaseDate" => tag.Year == (expected.ReleaseDate.Year > 1900 ? (uint)expected.ReleaseDate.Year : 0)
                        && string.Equals(ReadCustomText(savedFile, path, ReleaseDateTagName),
                            expected.ReleaseDate.Year > 1900 ? expected.ReleaseDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : string.Empty,
                            StringComparison.Ordinal),
                    _ => true
                };

                if (!matched)
                {
                    throw new InvalidDataException($"{field} が保存後に一致しません。");
                }
            }
        }

        private static TagLib.Mpeg4.AppleTag? GetAppleTag(TagLib.File file, bool create)
        {
            return file.GetTag(TagTypes.Apple, create) as TagLib.Mpeg4.AppleTag;
        }

        private static bool SupportsCustomArchivioTags(string path)
        {
            var extension = Path.GetExtension(path);
            return extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".m4v", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".mov", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".webm", StringComparison.OrdinalIgnoreCase);
        }

        private static string ReadCustomText(TagLib.File file, string path, string name)
        {
            var extension = Path.GetExtension(path);
            if (IsMp4Family(extension))
            {
                return GetAppleTag(file, create: false)?.GetDashBox(ArchivioTagOwner, name) ?? string.Empty;
            }

            if (IsMatroska(extension))
            {
                var tag = file.GetTag(TagTypes.Matroska, create: false) as TagLib.Matroska.Tag;
                return GetMatroskaCustomText(tag, $"ARCHIVIO_{name.ToUpperInvariant()}");
            }

            return string.Empty;
        }

        private static void SetCustomText(TagLib.File file, string path, string name, string value)
        {
            var extension = Path.GetExtension(path);
            if (IsMp4Family(extension))
            {
                var appleTag = GetAppleTag(file, create: true)
                    ?? throw new NotSupportedException("Apple タグを作成できません。");
                appleTag.SetDashBox(ArchivioTagOwner, name, value);
                return;
            }

            if (IsMatroska(extension))
            {
                var tag = file.GetTag(TagTypes.Matroska, create: true) as TagLib.Matroska.Tag
                    ?? throw new NotSupportedException("Matroska タグを作成できません。");
                SetMatroskaCustomText(tag, $"ARCHIVIO_{name.ToUpperInvariant()}", value);
                return;
            }

            throw new NotSupportedException("この形式は Archivio 固有タグに対応していません。");
        }

        private static string GetMatroskaCustomText(TagLib.Matroska.Tag? tag, string key)
        {
            return tag?.Get(key, null, true)?.FirstOrDefault() ?? string.Empty;
        }

        private static void SetMatroskaCustomText(TagLib.Matroska.Tag tag, string key, string value)
        {
            tag.Set(key, null, string.IsNullOrWhiteSpace(value) ? null : value);
        }

        private static DateTimeOffset ReadReleaseDate(TagLib.File file, string path)
        {
            var rawReleaseDate = ReadCustomText(file, path, ReleaseDateTagName);
            if (DateTime.TryParseExact(rawReleaseDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                return new DateTimeOffset(parsedDate.Year, parsedDate.Month, parsedDate.Day, 0, 0, 0, TimeSpan.Zero);
            }

            return file.Tag.Year > 0
                ? new DateTimeOffset((int)file.Tag.Year, 1, 1, 0, 0, 0, TimeSpan.Zero)
                : new DateTimeOffset(1900, 1, 1, 0, 0, 0, TimeSpan.Zero);
        }

        private static string[] SplitValues(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? Array.Empty<string>()
                : value.Split(new[] { ';', ',', '，', '；' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(item => item.Trim())
                    .Where(item => item.Length > 0)
                    .ToArray();
        }

        private static bool IsMp4Family(string extension)
        {
            return extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".m4v", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".mov", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsMatroska(string extension)
        {
            return extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".webm", StringComparison.OrdinalIgnoreCase);
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