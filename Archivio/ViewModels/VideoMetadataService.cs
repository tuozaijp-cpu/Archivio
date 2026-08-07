using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.WindowsAPICodePack.Shell;
using Microsoft.WindowsAPICodePack.Shell.PropertySystem;
using TagLib;
using Windows.Storage;

namespace Archivio.ViewModels
{
    public interface IVideoMetadataService
    {
        Task<VideoMetadataLoadResult> LoadMetadataAsync(StorageFile file);
        Task<VideoMetadataOperationResult> SaveMetadataAsync(StorageFile file, VideoMetadataSnapshot metadata, VideoMetadataSnapshot? originalMetadata, IEnumerable<string> changedProperties);
        Task<IReadOnlyList<CoverArtImageData>> LoadCoverArtImagesAsync(StorageFile file);
        Task<VideoMetadataOperationResult> SaveCoverArtAsync(StorageFile file, IEnumerable<CoverArtImageData> coverArtImages);
    }

    public sealed class VideoMetadataOperationResult
    {
        public bool Succeeded { get; init; }
        /// <summary>正本であるファイル内タグの保存には成功したが、Windows 表示用プロパティの同期で問題があった場合に true。</summary>
        public bool HasWarnings { get; init; }
        public string Message { get; init; } = string.Empty;
        public string? Details { get; init; }
    }

    public sealed class VideoMetadataLoadResult
    {
        public VideoMetadataSnapshot Metadata { get; init; } = new();
        public bool WindowsPropertiesLoaded { get; init; }
        public IReadOnlyList<string> WindowsPropertyErrors { get; init; } = Array.Empty<string>();
    }

    public sealed class VideoMetadataSnapshot
    {
        public string Title { get; set; } = string.Empty;
        public string Participants { get; set; } = string.Empty;
        public string CatalogNumber { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Rating { get; set; } = string.Empty;
        public string ContentDistributor { get; set; } = string.Empty;
        public string Publisher { get; set; } = string.Empty;
        public string Duration { get; set; } = string.Empty;
        public string FrameWidth { get; set; } = string.Empty;
        public string FrameHeight { get; set; } = string.Empty;
        public string FrameRate { get; set; } = string.Empty;
        public string VideoBitrate { get; set; } = string.Empty;
        public string VideoCompression { get; set; } = string.Empty;
        public string AudioSampleRate { get; set; } = string.Empty;
        public string AudioBitrate { get; set; } = string.Empty;
        public string AudioFormat { get; set; } = string.Empty;
        public string Comment { get; set; } = string.Empty;
        public string ReleaseDateText { get; set; } = string.Empty;
        public DateTimeOffset ReleaseDate { get; set; } = new DateTimeOffset(1900, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

    public sealed class VideoMetadataService : IVideoMetadataService
    {
        private const string ArchivioTagOwner = "com.archivio";
        private const string ReleaseDateTagName = "ReleaseDate";
        private static readonly System.Threading.SemaphoreSlim WindowsPropertyReadSemaphore = new(1, 1);

        public async Task<VideoMetadataLoadResult> LoadMetadataAsync(StorageFile file)
        {
            return await Task.Run(() =>
            {
                var snapshot = new VideoMetadataSnapshot();
                var windowsPropertyErrors = new List<string>();

                try
                {
                    using var tagFile = TagLib.File.Create(file.Path);
                    if (tagFile.Tag != null)
                    {
                        snapshot.Title = tagFile.Tag.Title ?? string.Empty;
                        snapshot.Participants = string.Join("; ", tagFile.Tag.Performers ?? Array.Empty<string>());
                        snapshot.Comment = tagFile.Tag.Comment ?? string.Empty;
                        snapshot.Category = string.Join("; ", tagFile.Tag.Genres ?? Array.Empty<string>());
                        snapshot.CatalogNumber = tagFile.Tag.Grouping ?? string.Empty;
                        snapshot.Publisher = tagFile.Tag.Publisher ?? string.Empty;
                        snapshot.ContentDistributor = tagFile.Tag.Copyright ?? string.Empty;
                        snapshot.Rating = GetAppleTag(tagFile, create: false)?.GetDashBox(ArchivioTagOwner, "Rating") ?? string.Empty;
                        snapshot.ReleaseDate = ReadReleaseDate(tagFile);
                        snapshot.ReleaseDateText = snapshot.ReleaseDate.Year > 1900 ? snapshot.ReleaseDate.ToString("yyyy-MM-dd") : string.Empty;

                        PopulateMediaProperties(snapshot, tagFile);
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Error("ファイル内タグの読み込みに失敗しました", ex, file.Path);
                }

                try
                {
                    // Windows の Property Handler は動画形式ごとに実装が異なり、並列アクセスで不安定になるものがある。
                    // アプリ内では一度に一件だけ取得する。
                    WindowsPropertyReadSemaphore.Wait();
                    using var shellFile = ShellFile.FromFilePath(file.Path);
                    var properties = shellFile.Properties;

                    var frameRate = GetFirstShellPropertyText(properties, new[] { "System.Video.FrameRate", "System.Media.FrameRate" }, windowsPropertyErrors);
                    if (!string.IsNullOrWhiteSpace(frameRate))
                    {
                        snapshot.FrameRate = NormalizeFrameRate(frameRate);
                    }

                    var videoBitrate = GetFirstShellPropertyText(properties, new[] { "System.Video.EncodingBitrate", "System.Video.BitRate", "System.Video.Bitrate" }, windowsPropertyErrors);
                    if (!string.IsNullOrWhiteSpace(videoBitrate))
                    {
                        snapshot.VideoBitrate = NormalizeBitrate(videoBitrate);
                    }

                    var videoCompression = GetFirstShellPropertyText(properties, new[] { "System.Video.Compression", "System.Video.CompressionType", "System.Video.Compressor" }, windowsPropertyErrors);
                    if (!string.IsNullOrWhiteSpace(videoCompression))
                    {
                        snapshot.VideoCompression = videoCompression;
                    }

                    var audioSampleRate = GetFirstShellPropertyText(properties, new[] { "System.Audio.SampleRate", "System.Audio.SamplingRate" }, windowsPropertyErrors);
                    if (!string.IsNullOrWhiteSpace(audioSampleRate))
                    {
                        snapshot.AudioSampleRate = audioSampleRate;
                    }

                    var audioBitrate = GetFirstShellPropertyText(properties, new[] { "System.Audio.EncodingBitrate", "System.Audio.BitRate", "System.Audio.Bitrate" }, windowsPropertyErrors);
                    if (!string.IsNullOrWhiteSpace(audioBitrate))
                    {
                        snapshot.AudioBitrate = NormalizeBitrate(audioBitrate);
                    }

                    var audioFormat = GetFirstShellPropertyText(properties, new[] { "System.Audio.Format", "System.Audio.EncodingFormat" }, windowsPropertyErrors);
                    if (!string.IsNullOrWhiteSpace(audioFormat))
                    {
                        snapshot.AudioFormat = audioFormat;
                    }

                }
                catch (Exception ex)
                {
                    windowsPropertyErrors.Add(ex.Message);
                    AppLogger.Error("Windows プロパティの読み込みに失敗しました", ex, file.Path);
                }
                finally
                {
                    WindowsPropertyReadSemaphore.Release();
                }

                return new VideoMetadataLoadResult
                {
                    Metadata = snapshot,
                    WindowsPropertiesLoaded = windowsPropertyErrors.Count == 0,
                    WindowsPropertyErrors = windowsPropertyErrors
                };
            });
        }

        public async Task<VideoMetadataOperationResult> SaveMetadataAsync(StorageFile file, VideoMetadataSnapshot metadata, VideoMetadataSnapshot? originalMetadata, IEnumerable<string> changedProperties)
        {
            var changedPropertyList = changedProperties?.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>();
            if (changedPropertyList.Count == 0)
            {
                return new VideoMetadataOperationResult { Succeeded = true, Message = "変更なし" };
            }

            return await Task.Run(() =>
            {
                var fieldsToPersist = new List<string>();
                if (changedPropertyList.Contains("Title") && HasValueChanged(metadata.Title, originalMetadata?.Title))
                {
                    fieldsToPersist.Add("Title");
                }
                if (changedPropertyList.Contains("Participants") && HasValueChanged(metadata.Participants, originalMetadata?.Participants))
                {
                    fieldsToPersist.Add("Participants");
                }
                if (changedPropertyList.Contains("Comment") && HasValueChanged(metadata.Comment, originalMetadata?.Comment))
                {
                    fieldsToPersist.Add("Comment");
                }
                if (changedPropertyList.Contains("Category") && HasValueChanged(metadata.Category, originalMetadata?.Category))
                {
                    fieldsToPersist.Add("Category");
                }
                if (changedPropertyList.Contains("CatalogNumber") && HasValueChanged(metadata.CatalogNumber, originalMetadata?.CatalogNumber))
                {
                    fieldsToPersist.Add("CatalogNumber");
                }
                if (changedPropertyList.Contains("Rating") && HasValueChanged(metadata.Rating, originalMetadata?.Rating))
                {
                    fieldsToPersist.Add("Rating");
                }
                if (changedPropertyList.Contains("ContentDistributor") && HasValueChanged(metadata.ContentDistributor, originalMetadata?.ContentDistributor))
                {
                    fieldsToPersist.Add("ContentDistributor");
                }
                if (changedPropertyList.Contains("Publisher") && HasValueChanged(metadata.Publisher, originalMetadata?.Publisher))
                {
                    fieldsToPersist.Add("Publisher");
                }
                if (changedPropertyList.Contains("ReleaseDate") && HasReleaseDateChanged(metadata.ReleaseDate, originalMetadata?.ReleaseDate))
                {
                    fieldsToPersist.Add("ReleaseDate");
                }

                if (fieldsToPersist.Count == 0)
                {
                    return new VideoMetadataOperationResult { Succeeded = true, Message = "変更なし" };
                }

                var canonicalErrors = new List<string>();
                try
                {
                    using var tagFile = TagLib.File.Create(file.Path);
                    if (tagFile?.Tag != null)
                    {
                        bool isDirty = false;

                        if (fieldsToPersist.Contains("Title"))
                        {
                            tagFile.Tag.Title = metadata.Title;
                            isDirty = true;
                        }

                        if (fieldsToPersist.Contains("Participants"))
                        {
                            tagFile.Tag.Performers = string.IsNullOrWhiteSpace(metadata.Participants)
                                ? Array.Empty<string>()
                                : metadata.Participants.Split(new[] { ';', ',', '，', '；' }, StringSplitOptions.RemoveEmptyEntries)
                                                        .Select(p => p.Trim())
                                                        .Where(p => !string.IsNullOrEmpty(p))
                                                        .ToArray();
                            isDirty = true;
                        }

                        if (fieldsToPersist.Contains("Comment"))
                        {
                            tagFile.Tag.Comment = metadata.Comment;
                            isDirty = true;
                        }

                        if (fieldsToPersist.Contains("ReleaseDate"))
                        {
                            tagFile.Tag.Year = metadata.ReleaseDate.Year > 1900 ? (uint)metadata.ReleaseDate.Year : 0;
                            var appleTag = GetAppleTag(tagFile, create: true);
                            if (appleTag != null)
                            {
                                appleTag.SetDashBox(
                                    ArchivioTagOwner,
                                    ReleaseDateTagName,
                                    metadata.ReleaseDate.Year > 1900
                                        ? metadata.ReleaseDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                                        : string.Empty);
                            }
                            isDirty = true;
                        }

                        if (fieldsToPersist.Contains("Rating"))
                        {
                            var appleTag = GetAppleTag(tagFile, create: true);
                            if (appleTag != null)
                            {
                                appleTag.SetDashBox(ArchivioTagOwner, "Rating", metadata.Rating ?? string.Empty);
                            }
                            isDirty = true;
                        }

                        if (fieldsToPersist.Contains("Category"))
                        {
                            tagFile.Tag.Genres = string.IsNullOrWhiteSpace(metadata.Category)
                                ? Array.Empty<string>()
                                : metadata.Category.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                                                    .Select(g => g.Trim())
                                                    .Where(g => !string.IsNullOrEmpty(g))
                                                    .ToArray();
                            isDirty = true;
                        }

                        if (fieldsToPersist.Contains("CatalogNumber"))
                        {
                            tagFile.Tag.Grouping = metadata.CatalogNumber;
                            isDirty = true;
                        }

                        if (fieldsToPersist.Contains("Publisher"))
                        {
                            tagFile.Tag.Publisher = metadata.Publisher;
                            isDirty = true;
                        }

                        if (fieldsToPersist.Contains("ContentDistributor"))
                        {
                            tagFile.Tag.Copyright = metadata.ContentDistributor;
                            isDirty = true;
                        }

                        if (isDirty)
                        {
                            tagFile.Save();
                        }
                    }
                    else
                    {
                        canonicalErrors.Add("ファイル内タグが利用できないため保存できません。");
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Error("ファイル内タグの保存に失敗しました", ex, file.Path);
                    canonicalErrors.Add($"ファイル内タグの保存に失敗しました: {ex.Message}");
                }

                if (canonicalErrors.Count == 0)
                {
                    try
                    {
                        VerifyEmbeddedMetadata(file.Path, metadata, fieldsToPersist);
                    }
                    catch (Exception ex)
                    {
                        canonicalErrors.Add($"ファイル内タグの保存内容を確認できませんでした: {ex.Message}");
                    }
                }

                return new VideoMetadataOperationResult
                {
                    Succeeded = canonicalErrors.Count == 0,
                    Message = canonicalErrors.Count > 0 ? "ファイル内タグの保存に失敗しました" : "保存しました",
                    Details = canonicalErrors.Count == 0 ? null : string.Join(Environment.NewLine, canonicalErrors)
                };
            });
        }

        public async Task<IReadOnlyList<CoverArtImageData>> LoadCoverArtImagesAsync(StorageFile file)
        {
            var imagesData = new List<CoverArtImageData>();

            await Task.Run(() =>
            {
                try
                {
                    using var tagFile = TagLib.File.Create(file.Path);
                    foreach (var picture in tagFile.Tag.Pictures)
                    {
                        if (picture?.Data?.Data is not null)
                        {
                            imagesData.Add(new CoverArtImageData
                            {
                                Data = picture.Data.Data,
                                MimeType = picture.MimeType ?? "image/jpeg",
                                Description = picture.Description ?? string.Empty
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Error("カバー画像の読み込みに失敗しました", ex, file.Path);
                }
            });

            return imagesData;
        }

        public async Task<VideoMetadataOperationResult> SaveCoverArtAsync(StorageFile file, IEnumerable<CoverArtImageData> coverArtImages)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var tagFile = TagLib.File.Create(file.Path);
                    var pics = new List<IPicture>();
                    foreach (var coverArt in coverArtImages ?? Array.Empty<CoverArtImageData>())
                    {
                        var pic = new TagLib.Picture(new ByteVector(coverArt?.Data ?? Array.Empty<byte>()))
                        {
                            MimeType = coverArt?.MimeType ?? "image/jpeg",
                            Description = coverArt?.Description ?? string.Empty
                        };
                        pics.Add(pic);
                    }

                    tagFile.Tag.Pictures = pics.ToArray();
                    tagFile.Save();

                    return new VideoMetadataOperationResult { Succeeded = true, Message = "カバー画像を保存しました" };
                }
                catch (Exception ex)
                {
                    AppLogger.Error("カバー画像の保存に失敗しました", ex, file.Path);
                    return new VideoMetadataOperationResult
                    {
                        Succeeded = false,
                        Message = "カバー画像の保存に失敗しました",
                        Details = ex.Message
                    };
                }
            });
        }

        private static void PopulateMediaProperties(VideoMetadataSnapshot snapshot, TagLib.File tagFile)
        {
            if (tagFile?.Properties is null)
            {
                return;
            }

            var properties = tagFile.Properties;
            if (properties.Duration > TimeSpan.Zero)
            {
                snapshot.Duration = FormatDuration(properties.Duration);
            }

            snapshot.FrameWidth = GetNumericStringProperty(properties, "VideoWidth");
            snapshot.FrameHeight = GetNumericStringProperty(properties, "VideoHeight");
            snapshot.FrameRate = GetNumericStringProperty(properties, "VideoFrameRate", format: "0.##");
            snapshot.VideoBitrate = GetNumericStringProperty(properties, "VideoBitrate");
            snapshot.AudioSampleRate = GetNumericStringProperty(properties, "AudioSampleRate");
            snapshot.AudioBitrate = GetNumericStringProperty(properties, "AudioBitrate");
            snapshot.AudioFormat = GetStringProperty(properties, "AudioFormat");
        }

        private static string FormatDuration(TimeSpan duration)
        {
            return duration.TotalHours >= 1
                ? duration.ToString("h\\:mm\\:ss")
                : duration.ToString("m\\:ss");
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
                if (property?.GetValue(instance) is null)
                {
                    return string.Empty;
                }

                var value = property.GetValue(instance);
                return value switch
                {
                    int intValue when intValue > 0 => intValue.ToString(format),
                    uint uintValue when uintValue > 0 => uintValue.ToString(format),
                    long longValue when longValue > 0 => longValue.ToString(format),
                    ulong ulongValue when ulongValue > 0 => ulongValue.ToString(format),
                    double doubleValue when doubleValue > 0 => doubleValue.ToString(format),
                    float floatValue when floatValue > 0 => floatValue.ToString(format),
                    _ => string.Empty
                };
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool HasValueChanged(string? currentValue, string? originalValue)
        {
            return !string.Equals(currentValue ?? string.Empty, originalValue ?? string.Empty, StringComparison.Ordinal);
        }

        private static void VerifyEmbeddedMetadata(string path, VideoMetadataSnapshot expected, IEnumerable<string> fields)
        {
            using var savedFile = TagLib.File.Create(path);
            var tag = savedFile.Tag ?? throw new InvalidDataException("ファイル内タグを読み取れません。");
            var appleTag = GetAppleTag(savedFile, create: false);

            foreach (var field in fields)
            {
                var matched = field switch
                {
                    "Title" => string.Equals(tag.Title ?? string.Empty, expected.Title ?? string.Empty, StringComparison.Ordinal),
                    "Participants" => tag.Performers.SequenceEqual(SplitValues(expected.Participants), StringComparer.Ordinal),
                    "Comment" => string.Equals(tag.Comment ?? string.Empty, expected.Comment ?? string.Empty, StringComparison.Ordinal),
                    "Category" => tag.Genres.SequenceEqual(SplitValues(expected.Category), StringComparer.Ordinal),
                    "CatalogNumber" => string.Equals(tag.Grouping ?? string.Empty, expected.CatalogNumber ?? string.Empty, StringComparison.Ordinal),
                    "Publisher" => string.Equals(tag.Publisher ?? string.Empty, expected.Publisher ?? string.Empty, StringComparison.Ordinal),
                    "ContentDistributor" => string.Equals(tag.Copyright ?? string.Empty, expected.ContentDistributor ?? string.Empty, StringComparison.Ordinal),
                    "Rating" => string.Equals(appleTag?.GetDashBox(ArchivioTagOwner, "Rating") ?? string.Empty, expected.Rating ?? string.Empty, StringComparison.Ordinal),
                    "ReleaseDate" => tag.Year == (expected.ReleaseDate.Year > 1900 ? (uint)expected.ReleaseDate.Year : 0)
                        && string.Equals(appleTag?.GetDashBox(ArchivioTagOwner, ReleaseDateTagName) ?? string.Empty,
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
            return file.GetTag(TagLib.TagTypes.Apple, create) as TagLib.Mpeg4.AppleTag;
        }

        private static DateTimeOffset ReadReleaseDate(TagLib.File file)
        {
            var rawReleaseDate = GetAppleTag(file, create: false)?.GetDashBox(ArchivioTagOwner, ReleaseDateTagName);
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

        private static bool HasReleaseDateChanged(DateTimeOffset currentValue, DateTimeOffset? originalValue)
        {
            if (originalValue is null)
            {
                return currentValue.Year > 1900;
            }

            return currentValue.Date != originalValue.Value.Date;
        }

        private static string GetShellPropertyText(ShellProperties properties, string propertyName, ICollection<string>? errors = null)
        {
            try
            {
                var property = properties.GetProperty(propertyName);
                if (property is null || property.ValueAsObject is null)
                {
                    return string.Empty;
                }

                var displayText = property.FormatForDisplay((PropertyDescriptionFormatOptions)0);
                if (string.IsNullOrWhiteSpace(displayText))
                {
                    // Property Handler によっては表示形式を返さず、値だけを返すことがある。
                    displayText = property.ValueAsObject switch
                    {
                        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                        _ => property.ValueAsObject?.ToString() ?? string.Empty
                    };
                }

                var trimmed = displayText.Trim();

                if (trimmed.EndsWith("を追加") || trimmed.EndsWith("の追加") || trimmed.Contains("追加してください") ||
                    trimmed == "評価" || trimmed == "コメント" || trimmed == "カテゴリ" || trimmed == "タグ" ||
                    trimmed == "タイトルを追加" || trimmed == "出演者を追加" || trimmed == "コメントを追加" ||
                    trimmed == "カテゴリを追加" || trimmed == "品番を追加" || trimmed == "発行元を追加" ||
                    trimmed == "レーベルを追加" || trimmed == "テキストを追加" || trimmed == "星を追加")
                {
                    return string.Empty;
                }

                return trimmed;
            }
            catch (Exception ex)
            {
                errors?.Add($"{propertyName}: {ex.Message}");
                return string.Empty;
            }
        }

        private static string GetFirstShellPropertyText(ShellProperties properties, IEnumerable<string> propertyNames, ICollection<string>? errors = null)
        {
            foreach (var propertyName in propertyNames)
            {
                var value = GetShellPropertyText(properties, propertyName, errors);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }

        private static string NormalizeFrameRate(string value)
        {
            var trimmed = value.Trim();
            if (double.TryParse(trimmed.Replace("fps", string.Empty, StringComparison.OrdinalIgnoreCase), out var fps))
            {
                return fps.ToString("0.##");
            }

            return trimmed;
        }

        private static string NormalizeBitrate(string value)
        {
            var trimmed = value.Trim();
            if (int.TryParse(trimmed.Replace("kbps", string.Empty, StringComparison.OrdinalIgnoreCase), out var kbps))
            {
                return $"{kbps} kbps";
            }

            if (double.TryParse(trimmed.Replace("kbps", string.Empty, StringComparison.OrdinalIgnoreCase), out var bitrate))
            {
                return $"{bitrate:0.##} kbps";
            }

            return trimmed;
        }

        public static string DecodeVideoSubtypeGuid(string guidStr)
        {
            if (string.IsNullOrWhiteSpace(guidStr)) return string.Empty;

            var trimmed = guidStr.Trim().ToLowerInvariant();
            if (trimmed.Length >= 36 && trimmed.Contains("-0000-0010-8000-00aa00389b71"))
            {
                var cleanGuid = trimmed.Replace("{", "").Replace("}", "");
                var hexPart = cleanGuid.Split('-')[0];
                if (hexPart.Length == 8)
                {
                    try
                    {
                        byte[] bytes = new byte[4];
                        for (int i = 0; i < 4; i++)
                        {
                            bytes[i] = Convert.ToByte(hexPart.Substring(i * 2, 2), 16);
                        }

                        char[] chars = new char[4];
                        chars[0] = (char)bytes[3];
                        chars[1] = (char)bytes[2];
                        chars[2] = (char)bytes[1];
                        chars[3] = (char)bytes[0];

                        var fourcc = new string(chars).Trim();
                        if (fourcc.All(c => char.IsLetterOrDigit(c) || char.IsPunctuation(c)))
                        {
                            return fourcc;
                        }
                    }
                    catch
                    {
                    }
                }
            }

            return guidStr;
        }

        public static string MapVideoCompression(string rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue)) return string.Empty;

            var decoded = DecodeVideoSubtypeGuid(rawValue);
            var trimmed = decoded.Trim().ToLowerInvariant();
            return trimmed switch
            {
                "avc1" or "h264" or "h.264" => "H.264 (AVC)",
                "hevc" or "hvc1" or "h265" or "h.265" => "H.265 (HEVC)",
                "mp4v" or "mpeg4" or "mpeg-4" => "MPEG-4 Video",
                "av01" or "av1" => "AV1",
                "vp9" or "vp09" => "VP9",
                "vp8" or "vp08" => "VP8",
                _ => decoded
            };
        }

        public static string MapAudioFormat(string rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue)) return string.Empty;

            var trimmed = rawValue.Trim().ToLowerInvariant();

            if (trimmed.Contains("1610") || trimmed.Contains("mp4a") || trimmed.Contains("aac"))
                return "AAC";
            if (trimmed.Contains("0055") || trimmed.Contains("mp3"))
                return "MP3";
            if (trimmed.Contains("0001") || trimmed.Contains("pcm") || trimmed.Contains("wav"))
                return "PCM (WAV)";
            if (trimmed.Contains("2000") || trimmed.Contains("ac3") || trimmed.Contains("ac-3"))
                return "AC-3 (Dolby Digital)";
            if (trimmed.Contains("opus"))
                return "Opus";
            if (trimmed.Contains("flac"))
                return "FLAC";

            return rawValue;
        }
    }
}
