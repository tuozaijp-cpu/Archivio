using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.WindowsAPICodePack.Shell;
using Microsoft.WindowsAPICodePack.Shell.PropertySystem;
using Windows.Storage;

namespace Archivio.ViewModels
{
    internal sealed class TechnicalMetadataLoadResult
    {
        public bool WindowsPropertiesLoaded { get; init; }
        public bool TechnicalPropertiesLoaded { get; init; }
        public IReadOnlyList<string> WindowsPropertyErrors { get; init; } = Array.Empty<string>();
    }

    internal interface IVideoTechnicalMetadataService
    {
        Task<TechnicalMetadataLoadResult> LoadAsync(
            StorageFile file,
            VideoMetadataSnapshot snapshot,
            TagLibTechnicalProperties tagLibProperties,
            CancellationToken cancellationToken);
    }

    internal sealed class VideoTechnicalMetadataService : IVideoTechnicalMetadataService
    {
        private static readonly SemaphoreSlim WindowsPropertyReadSemaphore = new(4, 4);
        private readonly IMediaProbeService _mediaProbeService;

        public VideoTechnicalMetadataService(IMediaProbeService mediaProbeService)
        {
            _mediaProbeService = mediaProbeService;
        }

        public async Task<TechnicalMetadataLoadResult> LoadAsync(
            StorageFile file,
            VideoMetadataSnapshot snapshot,
            TagLibTechnicalProperties tagLibProperties,
            CancellationToken cancellationToken)
        {
            ApplyTagLibProperties(snapshot, tagLibProperties);
            var probeResult = await _mediaProbeService.ProbeAsync(file, cancellationToken);
            return await Task.Run(() =>
            {
                var errors = new List<string>();
                ApplyProbeResult(snapshot, probeResult);

                var shellLockTaken = false;
                try
                {
                    WindowsPropertyReadSemaphore.Wait(cancellationToken);
                    shellLockTaken = true;
                    using var shellFile = ShellFile.FromFilePath(file.Path);
                    var properties = shellFile.Properties;

                    var frameRate = GetFirstShellPropertyText(properties,
                        new[] { "System.Video.FrameRate", "System.Media.FrameRate" }, errors);
                    if (!string.IsNullOrWhiteSpace(frameRate))
                    {
                        var normalized = NormalizeFrameRate(frameRate);
                        snapshot.FrameRate = ValueOrExisting(snapshot.FrameRate, normalized);
                        snapshot.Technical.FrameRate ??= ParseDouble(normalized);
                    }

                    var videoBitrate = GetFirstShellPropertyText(properties,
                        new[] { "System.Video.EncodingBitrate", "System.Video.BitRate", "System.Video.Bitrate" }, errors);
                    if (!string.IsNullOrWhiteSpace(videoBitrate))
                    {
                        var normalized = NormalizeBitrate(videoBitrate);
                        snapshot.VideoBitrate = ValueOrExisting(snapshot.VideoBitrate, normalized);
                        snapshot.Technical.VideoBitrate ??= ParseBitrate(normalized);
                    }

                    var videoCompression = GetFirstShellPropertyText(properties,
                        new[] { "System.Video.Compression", "System.Video.CompressionType", "System.Video.Compressor" }, errors);
                    if (!string.IsNullOrWhiteSpace(videoCompression))
                    {
                        snapshot.VideoCompression = ValueOrExisting(snapshot.VideoCompression, videoCompression);
                        snapshot.Technical.VideoCodec = string.IsNullOrWhiteSpace(snapshot.Technical.VideoCodec)
                            ? videoCompression
                            : snapshot.Technical.VideoCodec;
                    }

                    var audioSampleRate = GetFirstShellPropertyText(properties,
                        new[] { "System.Audio.SampleRate", "System.Audio.SamplingRate" }, errors);
                    if (!string.IsNullOrWhiteSpace(audioSampleRate))
                    {
                        snapshot.AudioSampleRate = ValueOrExisting(snapshot.AudioSampleRate, audioSampleRate);
                        snapshot.Technical.AudioSampleRate ??= ParseInt(audioSampleRate);
                    }

                    var audioBitrate = GetFirstShellPropertyText(properties,
                        new[] { "System.Audio.EncodingBitrate", "System.Audio.BitRate", "System.Audio.Bitrate" }, errors);
                    if (!string.IsNullOrWhiteSpace(audioBitrate))
                    {
                        var normalized = NormalizeBitrate(audioBitrate);
                        snapshot.AudioBitrate = ValueOrExisting(snapshot.AudioBitrate, normalized);
                        snapshot.Technical.AudioBitrate ??= ParseBitrate(normalized);
                    }

                    var audioFormat = GetFirstShellPropertyText(properties,
                        new[] { "System.Audio.Format", "System.Audio.EncodingFormat" }, errors);
                    if (!string.IsNullOrWhiteSpace(audioFormat))
                    {
                        snapshot.AudioFormat = ValueOrExisting(snapshot.AudioFormat, audioFormat);
                        snapshot.Technical.AudioCodec = string.IsNullOrWhiteSpace(snapshot.Technical.AudioCodec)
                            ? audioFormat
                            : snapshot.Technical.AudioCodec;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    errors.Add(ex.Message);
                    AppLogger.Error("Windows プロパティの読み込みに失敗しました", ex, file.Path);
                }
                finally
                {
                    if (shellLockTaken)
                    {
                        WindowsPropertyReadSemaphore.Release();
                    }
                }

                return new TechnicalMetadataLoadResult
                {
                    WindowsPropertiesLoaded = errors.Count == 0,
                    TechnicalPropertiesLoaded = probeResult.HasValues || errors.Count == 0,
                    WindowsPropertyErrors = errors
                };
            }, cancellationToken);
        }

        public static string DecodeVideoSubtypeGuid(string guidStr)
        {
            if (string.IsNullOrWhiteSpace(guidStr))
            {
                return string.Empty;
            }

            var trimmed = guidStr.Trim().ToLowerInvariant();
            if (trimmed.Length >= 36 && trimmed.Contains("-0000-0010-8000-00aa00389b71"))
            {
                var cleanGuid = trimmed.Replace("{", "").Replace("}", "");
                var hexPart = cleanGuid.Split('-')[0];
                if (hexPart.Length == 8)
                {
                    try
                    {
                        var bytes = new byte[4];
                        for (var index = 0; index < bytes.Length; index++)
                        {
                            bytes[index] = Convert.ToByte(hexPart.Substring(index * 2, 2), 16);
                        }

                        var chars = new[]
                        {
                            (char)bytes[3], (char)bytes[2], (char)bytes[1], (char)bytes[0]
                        };
                        var fourcc = new string(chars).Trim();
                        if (fourcc.All(character => char.IsLetterOrDigit(character) || char.IsPunctuation(character)))
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
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return string.Empty;
            }

            var decoded = DecodeVideoSubtypeGuid(rawValue);
            return decoded.Trim().ToLowerInvariant() switch
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
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return string.Empty;
            }

            var trimmed = rawValue.Trim().ToLowerInvariant();
            if (trimmed.Contains("1610") || trimmed.Contains("mp4a") || trimmed.Contains("aac"))
            {
                return "AAC";
            }

            if (trimmed.Contains("0055") || trimmed.Contains("mp3"))
            {
                return "MP3";
            }

            if (trimmed.Contains("0001") || trimmed.Contains("pcm") || trimmed.Contains("wav"))
            {
                return "PCM (WAV)";
            }

            if (trimmed.Contains("2000") || trimmed.Contains("ac3") || trimmed.Contains("ac-3"))
            {
                return "AC-3 (Dolby Digital)";
            }

            if (trimmed.Contains("opus"))
            {
                return "Opus";
            }

            if (trimmed.Contains("flac"))
            {
                return "FLAC";
            }

            return rawValue;
        }

        private static void ApplyProbeResult(VideoMetadataSnapshot snapshot, MediaProbeResult result)
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

        private static void ApplyTagLibProperties(VideoMetadataSnapshot snapshot, TagLibTechnicalProperties properties)
        {
            if (properties.Duration is { } duration)
            {
                snapshot.Duration = FormatDuration(duration);
                snapshot.Technical.Duration = duration;
            }

            snapshot.FrameWidth = properties.FrameWidth;
            snapshot.FrameHeight = properties.FrameHeight;
            snapshot.FrameRate = properties.FrameRate;
            snapshot.VideoBitrate = properties.VideoBitrate;
            snapshot.AudioSampleRate = properties.AudioSampleRate;
            snapshot.AudioBitrate = properties.AudioBitrate;
            snapshot.AudioFormat = properties.AudioFormat;
            snapshot.Technical.FrameWidth = ParseInt(snapshot.FrameWidth);
            snapshot.Technical.FrameHeight = ParseInt(snapshot.FrameHeight);
            snapshot.Technical.FrameRate = ParseDouble(snapshot.FrameRate);
            snapshot.Technical.VideoBitrate = ParseBitrate(snapshot.VideoBitrate);
            snapshot.Technical.AudioSampleRate = ParseInt(snapshot.AudioSampleRate);
            snapshot.Technical.AudioBitrate = ParseBitrate(snapshot.AudioBitrate);
            snapshot.Technical.AudioCodec = snapshot.AudioFormat;
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

        private static string GetShellPropertyText(ShellProperties properties, string propertyName, ICollection<string> errors)
        {
            try
            {
                var property = properties.GetProperty(propertyName);
                if (property?.ValueAsObject is null)
                {
                    return string.Empty;
                }

                var displayText = property.FormatForDisplay((PropertyDescriptionFormatOptions)0);
                if (string.IsNullOrWhiteSpace(displayText))
                {
                    displayText = property.ValueAsObject switch
                    {
                        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                        _ => property.ValueAsObject.ToString() ?? string.Empty
                    };
                }

                var trimmed = displayText.Trim();
                if (trimmed.EndsWith("を追加") || trimmed.EndsWith("の追加") || trimmed.Contains("追加してください")
                    || trimmed is "評価" or "コメント" or "カテゴリ" or "タグ" or "タイトルを追加" or "出演者を追加"
                    or "コメントを追加" or "カテゴリを追加" or "品番を追加" or "発行元を追加" or "レーベルを追加"
                    or "テキストを追加" or "星を追加")
                {
                    return string.Empty;
                }

                return trimmed;
            }
            catch (Exception ex)
            {
                errors.Add($"{propertyName}: {ex.Message}");
                return string.Empty;
            }
        }

        private static string GetFirstShellPropertyText(
            ShellProperties properties,
            IEnumerable<string> propertyNames,
            ICollection<string> errors)
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
            return double.TryParse(
                trimmed.Replace("fps", string.Empty, StringComparison.OrdinalIgnoreCase),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var frameRate)
                ? frameRate.ToString("0.##", CultureInfo.InvariantCulture)
                : trimmed;
        }

        private static string NormalizeBitrate(string value)
        {
            var trimmed = value.Trim();
            if (int.TryParse(trimmed.Replace("kbps", string.Empty, StringComparison.OrdinalIgnoreCase),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out var kbps))
            {
                return $"{kbps} kbps";
            }

            return double.TryParse(trimmed.Replace("kbps", string.Empty, StringComparison.OrdinalIgnoreCase),
                NumberStyles.Float, CultureInfo.InvariantCulture, out var bitrate)
                ? $"{bitrate.ToString("0.##", CultureInfo.InvariantCulture)} kbps"
                : trimmed;
        }

        private static int? ParseInt(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var digits = new string(value.Where(char.IsDigit).ToArray());
            return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) && result > 0
                ? result
                : null;
        }

        private static double? ParseDouble(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var normalized = value.Replace("fps", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
            return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) && result > 0
                ? result
                : null;
        }

        private static long? ParseBitrate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var hasKbps = value.Contains("kbps", StringComparison.OrdinalIgnoreCase);
            var normalized = value.Replace("kbps", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
            if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) || result <= 0)
            {
                return null;
            }

            return hasKbps ? (long)(result * 1000) : (long)result;
        }
    }
}