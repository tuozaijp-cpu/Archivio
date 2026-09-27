using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.WindowsAPICodePack.Shell;
using Microsoft.WindowsAPICodePack.Shell.PropertySystem;
using TagLib;
using Windows.Storage;

namespace Archivio.ViewModels
{
    public interface IVideoMetadataService
    {
        Task<VideoMetadataLoadResult> LoadMetadataAsync(StorageFile file, CancellationToken cancellationToken = default);
        Task<VideoMetadataOperationResult> SaveMetadataAsync(StorageFile file, VideoMetadataSnapshot metadata, VideoMetadataSnapshot? originalMetadata, IEnumerable<string> changedProperties);
        Task<IReadOnlyList<CoverArtImageData>> LoadCoverArtImagesAsync(StorageFile file);
        Task<CoverArtImageData?> LoadThumbnailImageAsync(StorageFile file, CancellationToken cancellationToken = default);
        Task<VideoMetadataOperationResult> SaveCoverArtAsync(StorageFile file, IEnumerable<CoverArtImageData> coverArtImages);
    }

    public sealed class VideoMetadataOperationResult
    {
        public bool Succeeded { get; init; }
        /// <summary>一部項目だけ保存できなかった場合に true。</summary>
        public bool HasWarnings { get; init; }
        public string Message { get; init; } = string.Empty;
        public string? Details { get; init; }
        public IReadOnlyList<string> SavedProperties { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> FailedProperties { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> UnsupportedProperties { get; init; } = Array.Empty<string>();
    }

    public sealed class VideoMetadataLoadResult
    {
        public VideoMetadataSnapshot Metadata { get; init; } = new();
        public bool WindowsPropertiesLoaded { get; init; }
        /// <summary>FFprobe、Windows API、Shell のいずれかから技術情報を取得できたか。</summary>
        public bool TechnicalPropertiesLoaded { get; init; }
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
        public VideoTechnicalMetadata Technical { get; set; } = new();
    }

    /// <summary>技術情報の正規化済み内部表現。表示用文字列とは分離する。</summary>
    public sealed class VideoTechnicalMetadata
    {
        public TimeSpan? Duration { get; set; }
        public int? FrameWidth { get; set; }
        public int? FrameHeight { get; set; }
        public double? FrameRate { get; set; }
        public long? VideoBitrate { get; set; }
        public string VideoCodec { get; set; } = string.Empty;
        public int? AudioSampleRate { get; set; }
        public long? AudioBitrate { get; set; }
        public string AudioCodec { get; set; } = string.Empty;
    }

    public sealed class VideoMetadataService : IVideoMetadataService
    {
        private const string ArchivioTagOwner = "com.archivio";
        private const string ReleaseDateTagName = "ReleaseDate";
        private const string RatingTagName = "Rating";
        private static readonly System.Threading.SemaphoreSlim WindowsPropertyReadSemaphore = new(4, 4);
        private readonly IMediaProbeService _mediaProbeService;

        public VideoMetadataService(IMediaProbeService? mediaProbeService = null)
        {
            _mediaProbeService = mediaProbeService ?? new MediaProbeService();
        }

        public async Task<VideoMetadataLoadResult> LoadMetadataAsync(StorageFile file, CancellationToken cancellationToken = default)
        {
            // FFprobe が利用できれば最優先する。存在しない・失敗した環境では Windows API の結果になる。
            var probeResult = await _mediaProbeService.ProbeAsync(file, cancellationToken);
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
                        snapshot.ContentDistributor = tagFile.Tag.Copyright ?? string.Empty;

                        var extension = Path.GetExtension(file.Path);
                        if (extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase)
                            || extension.Equals(".webm", StringComparison.OrdinalIgnoreCase))
                        {
                            var mkvTag = tagFile.GetTag(TagTypes.Matroska, create: false) as TagLib.Matroska.Tag;
                            snapshot.Publisher = GetMatroskaCustomText(mkvTag, "PUBLISHER");
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
                        snapshot.ReleaseDateText = snapshot.ReleaseDate.Year > 1900 ? snapshot.ReleaseDate.ToString("yyyy-MM-dd") : string.Empty;

                        PopulateMediaProperties(snapshot, tagFile);
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Error("ファイル内タグの読み込みに失敗しました", ex, file.Path);
                }

                ApplyMediaProbeResult(snapshot, probeResult);

                var shellLockTaken = false;
                try
                {
                    // Windows の Property Handler は動画形式ごとに実装が異なり、並列アクセスで不安定になるものがある。
                    // アプリ内では一度に一件だけ取得する。
                    WindowsPropertyReadSemaphore.Wait();
                    shellLockTaken = true;
                    using var shellFile = ShellFile.FromFilePath(file.Path);
                    var properties = shellFile.Properties;

                    var frameRate = GetFirstShellPropertyText(properties, new[] { "System.Video.FrameRate", "System.Media.FrameRate" }, windowsPropertyErrors);
                    if (!string.IsNullOrWhiteSpace(frameRate))
                    {
                        var normalized = NormalizeFrameRate(frameRate);
                        snapshot.FrameRate = ValueOrExisting(snapshot.FrameRate, normalized);
                        snapshot.Technical.FrameRate ??= ParseDouble(normalized);
                    }

                    var videoBitrate = GetFirstShellPropertyText(properties, new[] { "System.Video.EncodingBitrate", "System.Video.BitRate", "System.Video.Bitrate" }, windowsPropertyErrors);
                    if (!string.IsNullOrWhiteSpace(videoBitrate))
                    {
                        var normalized = NormalizeBitrate(videoBitrate);
                        snapshot.VideoBitrate = ValueOrExisting(snapshot.VideoBitrate, normalized);
                        snapshot.Technical.VideoBitrate ??= ParseBitrate(normalized);
                    }

                    var videoCompression = GetFirstShellPropertyText(properties, new[] { "System.Video.Compression", "System.Video.CompressionType", "System.Video.Compressor" }, windowsPropertyErrors);
                    if (!string.IsNullOrWhiteSpace(videoCompression))
                    {
                        snapshot.VideoCompression = ValueOrExisting(snapshot.VideoCompression, videoCompression);
                        snapshot.Technical.VideoCodec = string.IsNullOrWhiteSpace(snapshot.Technical.VideoCodec)
                            ? videoCompression
                            : snapshot.Technical.VideoCodec;
                    }

                    var audioSampleRate = GetFirstShellPropertyText(properties, new[] { "System.Audio.SampleRate", "System.Audio.SamplingRate" }, windowsPropertyErrors);
                    if (!string.IsNullOrWhiteSpace(audioSampleRate))
                    {
                        snapshot.AudioSampleRate = ValueOrExisting(snapshot.AudioSampleRate, audioSampleRate);
                        snapshot.Technical.AudioSampleRate ??= ParseInt(audioSampleRate);
                    }

                    var audioBitrate = GetFirstShellPropertyText(properties, new[] { "System.Audio.EncodingBitrate", "System.Audio.BitRate", "System.Audio.Bitrate" }, windowsPropertyErrors);
                    if (!string.IsNullOrWhiteSpace(audioBitrate))
                    {
                        var normalized = NormalizeBitrate(audioBitrate);
                        snapshot.AudioBitrate = ValueOrExisting(snapshot.AudioBitrate, normalized);
                        snapshot.Technical.AudioBitrate ??= ParseBitrate(normalized);
                    }

                    var audioFormat = GetFirstShellPropertyText(properties, new[] { "System.Audio.Format", "System.Audio.EncodingFormat" }, windowsPropertyErrors);
                    if (!string.IsNullOrWhiteSpace(audioFormat))
                    {
                        snapshot.AudioFormat = ValueOrExisting(snapshot.AudioFormat, audioFormat);
                        snapshot.Technical.AudioCodec = string.IsNullOrWhiteSpace(snapshot.Technical.AudioCodec)
                            ? audioFormat
                            : snapshot.Technical.AudioCodec;
                    }

                }
                catch (Exception ex)
                {
                    windowsPropertyErrors.Add(ex.Message);
                    AppLogger.Error("Windows プロパティの読み込みに失敗しました", ex, file.Path);
                }
                finally
                {
                    if (shellLockTaken)
                    {
                        WindowsPropertyReadSemaphore.Release();
                    }
                }

                return new VideoMetadataLoadResult
                {
                    Metadata = snapshot,
                    WindowsPropertiesLoaded = windowsPropertyErrors.Count == 0,
                    TechnicalPropertiesLoaded = probeResult.HasValues || windowsPropertyErrors.Count == 0,
                    WindowsPropertyErrors = windowsPropertyErrors
                };
            }, cancellationToken);
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

                var warningErrors = new List<string>();
                var fatalErrors = new List<string>();
                var unsupportedCustomFields = fieldsToPersist
                    .Where(field => field is "Rating" or "ReleaseDate")
                    .Where(field => !SupportsCustomArchivioTags(file.Path))
                    .ToList();
                if (unsupportedCustomFields.Count > 0)
                {
                    warningErrors.Add($"{Path.GetExtension(file.Path)} は Archivio 固有項目（{string.Join("、", unsupportedCustomFields)}）の埋め込み保存に対応していません。");
                    fieldsToPersist = fieldsToPersist.Except(unsupportedCustomFields, StringComparer.OrdinalIgnoreCase).ToList();
                }

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
                            SetCustomText(tagFile, file.Path, ReleaseDateTagName,
                                metadata.ReleaseDate.Year > 1900
                                    ? metadata.ReleaseDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                                    : string.Empty);
                            isDirty = true;
                        }

                        if (fieldsToPersist.Contains("Rating"))
                        {
                            SetCustomText(tagFile, file.Path, RatingTagName, metadata.Rating ?? string.Empty);
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
                            var extension = Path.GetExtension(file.Path);
                            if (extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase)
                                || extension.Equals(".webm", StringComparison.OrdinalIgnoreCase))
                            {
                                var mkvTag = tagFile.GetTag(TagTypes.Matroska, create: true) as TagLib.Matroska.Tag
                                    ?? throw new NotSupportedException("Matroska タグを作成できません。");
                                SetMatroskaCustomText(mkvTag, "PUBLISHER", metadata.Publisher ?? string.Empty);
                            }
                            
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
                        fatalErrors.Add("ファイル内タグが利用できないため保存できません。");
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Error("ファイル内タグの保存に失敗しました", ex, file.Path);
                    fatalErrors.Add($"ファイル内タグの保存に失敗しました: {ex.Message}");
                }

                // 非対応の独自項目があっても、対応する標準項目の保存結果は検証する。
                if (fieldsToPersist.Count > 0 && fatalErrors.Count == 0)
                {
                    try
                    {
                        VerifyEmbeddedMetadata(file.Path, metadata, fieldsToPersist);
                    }
                    catch (Exception ex)
                    {
                        fatalErrors.Add($"ファイル内タグの保存内容を確認できませんでした: {ex.Message}");
                    }
                }

                var succeeded = fatalErrors.Count == 0;
                var errors = warningErrors.Concat(fatalErrors).ToList();

                return new VideoMetadataOperationResult
                {
                    Succeeded = succeeded,
                    HasWarnings = succeeded && warningErrors.Count > 0,
                    Message = succeeded ? (warningErrors.Count > 0 ? "一部の項目を保存しました" : "保存しました") : "ファイル内タグの保存に失敗しました",
                    Details = errors.Count == 0 ? null : string.Join(Environment.NewLine, errors),
                    SavedProperties = succeeded ? fieldsToPersist : Array.Empty<string>(),
                    FailedProperties = succeeded ? Array.Empty<string>() : fieldsToPersist,
                    UnsupportedProperties = unsupportedCustomFields
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
                                Description = picture.Description ?? string.Empty,
                                Type = picture.Type
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

        /// <summary>
        /// 一覧表示用に、動画内の最初のカバーアートだけを読み込みます。
        /// 詳細表示用の全カバーアート読み込みとは独立した処理です。
        /// </summary>
        public async Task<CoverArtImageData?> LoadThumbnailImageAsync(StorageFile file, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
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

                    return new CoverArtImageData
                    {
                        Data = picture.Data.Data,
                        MimeType = picture.MimeType ?? "image/jpeg",
                        Description = picture.Description ?? string.Empty,
                        Type = picture.Type
                    };
                }

                return null;
            }, cancellationToken);
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
                            Description = coverArt?.Description ?? string.Empty,
                            Type = coverArt?.Type ?? PictureType.FrontCover
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
                snapshot.Technical.Duration = properties.Duration;
            }

            snapshot.FrameWidth = GetNumericStringProperty(properties, "VideoWidth");
            snapshot.FrameHeight = GetNumericStringProperty(properties, "VideoHeight");
            snapshot.FrameRate = GetNumericStringProperty(properties, "VideoFrameRate", format: "0.##");
            snapshot.VideoBitrate = GetNumericStringProperty(properties, "VideoBitrate");
            snapshot.AudioSampleRate = GetNumericStringProperty(properties, "AudioSampleRate");
            snapshot.AudioBitrate = GetNumericStringProperty(properties, "AudioBitrate");
            snapshot.AudioFormat = GetStringProperty(properties, "AudioFormat");
            snapshot.Technical.FrameWidth = ParseInt(snapshot.FrameWidth);
            snapshot.Technical.FrameHeight = ParseInt(snapshot.FrameHeight);
            snapshot.Technical.FrameRate = ParseDouble(snapshot.FrameRate);
            snapshot.Technical.VideoBitrate = ParseBitrate(snapshot.VideoBitrate);
            snapshot.Technical.AudioSampleRate = ParseInt(snapshot.AudioSampleRate);
            snapshot.Technical.AudioBitrate = ParseBitrate(snapshot.AudioBitrate);
            snapshot.Technical.AudioCodec = snapshot.AudioFormat;
        }

        private static void ApplyMediaProbeResult(VideoMetadataSnapshot snapshot, MediaProbeResult result)
        {
            if (!result.HasValues)
            {
                return;
            }

            if (result.Duration is { } duration)
            {
                snapshot.Duration = ValueOrExisting(snapshot.Duration, FormatDuration(duration));
                snapshot.Technical.Duration ??= duration;
            }

            if (result.FrameWidth is { } width)
            {
                snapshot.FrameWidth = ValueOrExisting(snapshot.FrameWidth, width.ToString(CultureInfo.InvariantCulture));
                snapshot.Technical.FrameWidth ??= width;
            }

            if (result.FrameHeight is { } height)
            {
                snapshot.FrameHeight = ValueOrExisting(snapshot.FrameHeight, height.ToString(CultureInfo.InvariantCulture));
                snapshot.Technical.FrameHeight ??= height;
            }

            if (result.FrameRate is { } frameRate)
            {
                snapshot.FrameRate = ValueOrExisting(snapshot.FrameRate, frameRate.ToString("0.###", CultureInfo.InvariantCulture));
                snapshot.Technical.FrameRate ??= frameRate;
            }

            if (result.VideoBitrate is { } videoBitrate)
            {
                snapshot.VideoBitrate = ValueOrExisting(snapshot.VideoBitrate, FormatBitrate(videoBitrate));
                snapshot.Technical.VideoBitrate ??= videoBitrate;
            }

            snapshot.VideoCompression = ValueOrExisting(snapshot.VideoCompression, result.VideoCodec);
            if (string.IsNullOrWhiteSpace(snapshot.Technical.VideoCodec))
            {
                snapshot.Technical.VideoCodec = result.VideoCodec;
            }

            if (result.AudioSampleRate is { } sampleRate)
            {
                snapshot.AudioSampleRate = ValueOrExisting(snapshot.AudioSampleRate, $"{sampleRate} Hz");
                snapshot.Technical.AudioSampleRate ??= sampleRate;
            }

            if (result.AudioBitrate is { } audioBitrate)
            {
                snapshot.AudioBitrate = ValueOrExisting(snapshot.AudioBitrate, FormatBitrate(audioBitrate));
                snapshot.Technical.AudioBitrate ??= audioBitrate;
            }

            snapshot.AudioFormat = ValueOrExisting(snapshot.AudioFormat, result.AudioCodec);
            if (string.IsNullOrWhiteSpace(snapshot.Technical.AudioCodec))
            {
                snapshot.Technical.AudioCodec = result.AudioCodec;
            }
        }

        private static string ValueOrExisting(string destination, string? value)
        {
            return string.IsNullOrWhiteSpace(destination) && !string.IsNullOrWhiteSpace(value) ? value : destination;
        }

        private static string FormatBitrate(long bitsPerSecond)
        {
            return bitsPerSecond >= 1000
                ? $"{bitsPerSecond / 1000d:0.##} kbps"
                : $"{bitsPerSecond} bps";
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
                        (Path.GetExtension(path).Equals(".mkv", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".webm", StringComparison.OrdinalIgnoreCase))
                            ? GetMatroskaCustomText(savedFile.GetTag(TagTypes.Matroska, create: false) as TagLib.Matroska.Tag, "PUBLISHER")
                            : (tag.Publisher ?? string.Empty),
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
            return file.GetTag(TagLib.TagTypes.Apple, create) as TagLib.Mpeg4.AppleTag;
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
            if (extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".m4v", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".mov", StringComparison.OrdinalIgnoreCase))
            {
                return GetAppleTag(file, create: false)?.GetDashBox(ArchivioTagOwner, name) ?? string.Empty;
            }

            if (extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".webm", StringComparison.OrdinalIgnoreCase))
            {
                var tag = file.GetTag(TagTypes.Matroska, create: false) as TagLib.Matroska.Tag;
                return GetMatroskaCustomText(tag, $"ARCHIVIO_{name.ToUpperInvariant()}");
            }

            return string.Empty;
        }

        private static void SetCustomText(TagLib.File file, string path, string name, string value)
        {
            var extension = Path.GetExtension(path);
            if (extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".m4v", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".mov", StringComparison.OrdinalIgnoreCase))
            {
                var appleTag = GetAppleTag(file, create: true)
                    ?? throw new NotSupportedException("Apple タグを作成できません。");
                appleTag.SetDashBox(ArchivioTagOwner, name, value);
                return;
            }

            if (extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".webm", StringComparison.OrdinalIgnoreCase))
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
            if (tag is null)
            {
                return string.Empty;
            }

            return tag.Get(key, null, true)?.FirstOrDefault() ?? string.Empty;
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
            if (double.TryParse(trimmed.Replace("fps", string.Empty, StringComparison.OrdinalIgnoreCase), NumberStyles.Float, CultureInfo.InvariantCulture, out var fps))
            {
                return fps.ToString("0.##", CultureInfo.InvariantCulture);
            }

            return trimmed;
        }

        private static string NormalizeBitrate(string value)
        {
            var trimmed = value.Trim();
            if (int.TryParse(trimmed.Replace("kbps", string.Empty, StringComparison.OrdinalIgnoreCase), NumberStyles.Integer, CultureInfo.InvariantCulture, out var kbps))
            {
                return $"{kbps} kbps";
            }

            if (double.TryParse(trimmed.Replace("kbps", string.Empty, StringComparison.OrdinalIgnoreCase), NumberStyles.Float, CultureInfo.InvariantCulture, out var bitrate))
            {
                return $"{bitrate.ToString("0.##", CultureInfo.InvariantCulture)} kbps";
            }

            return trimmed;
        }

        private static int? ParseInt(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;

            var digits = new string(value.Where(char.IsDigit).ToArray());
            return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) && result > 0
                ? result
                : null;
        }

        private static double? ParseDouble(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;

            var normalized = value.Replace("fps", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
            return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) && result > 0
                ? result
                : null;
        }

        private static long? ParseBitrate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;

            var hasKbps = value.Contains("kbps", StringComparison.OrdinalIgnoreCase);
            var normalized = value.Replace("kbps", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
            if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) || result <= 0)
            {
                return null;
            }

            return hasKbps
                ? (long)(result * 1000)
                : (long)result;
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
