using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Archivio.Models;
using Windows.Storage;

namespace Archivio.ViewModels
{
    /// <summary>動画ストリームの技術情報を、利用可能な解析器から取得する。</summary>
    public interface IMediaProbeService
    {
        Task<MediaProbeResult> ProbeAsync(StorageFile file, CancellationToken cancellationToken = default);
    }

    public sealed class MediaProbeResult
    {
        public bool UsedFfprobe { get; init; }
        public TimeSpan? Duration { get; init; }
        public int? FrameWidth { get; init; }
        public int? FrameHeight { get; init; }
        public double? FrameRate { get; init; }
        public long? VideoBitrate { get; init; }
        public string VideoCodec { get; init; } = string.Empty;
        public int? AudioSampleRate { get; init; }
        public long? AudioBitrate { get; init; }
        public string AudioCodec { get; init; } = string.Empty;
        public VideoTechnicalInfo Technical { get; init; } = new();

        public bool HasValues => Duration is not null || FrameWidth is not null || FrameHeight is not null
            || FrameRate is not null || VideoBitrate is not null || !string.IsNullOrWhiteSpace(VideoCodec)
            || AudioSampleRate is not null || AudioBitrate is not null || !string.IsNullOrWhiteSpace(AudioCodec)
            || Technical.HasValues;
    }

    public sealed class MediaProbeService : IMediaProbeService
    {
        private readonly record struct FileVersion(long Length, DateTime LastWriteTimeUtc);
        private sealed record ProbeCacheEntry(FileVersion Version, MediaProbeResult Result);

        // 0: 未確認、1: 利用可能、-1: 起動不可。FFprobe 非導入環境での失敗プロセス生成を防ぐ。
        private static int _ffprobeAvailability;
        private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(30);
        // ファイルパスごとに、更新日時とサイズを照合して結果を再利用する。
        private static readonly ConcurrentDictionary<string, ProbeCacheEntry> ProbeResultCache =
            new(StringComparer.OrdinalIgnoreCase);

        public async Task<MediaProbeResult> ProbeAsync(StorageFile file, CancellationToken cancellationToken = default)
        {
            var fileVersion = GetFileVersion(file.Path);
            if (fileVersion is { } version
                && ProbeResultCache.TryGetValue(file.Path, out var cachedEntry)
                && cachedEntry.Version == version)
            {
                return cachedEntry.Result;
            }

            var ffprobeResult = await TryProbeWithFfprobeAsync(file.Path, cancellationToken);
            if (ffprobeResult.HasValues)
            {
                CacheResultIfUnchanged(file.Path, fileVersion, ffprobeResult);
                return ffprobeResult;
            }

            var windowsResult = await ProbeWithWindowsAsync(file, cancellationToken);
            CacheResultIfUnchanged(file.Path, fileVersion, windowsResult);
            return windowsResult;
        }

        private static FileVersion? GetFileVersion(string path)
        {
            try
            {
                var fileInfo = new FileInfo(path);
                fileInfo.Refresh();
                return fileInfo.Exists
                    ? new FileVersion(fileInfo.Length, fileInfo.LastWriteTimeUtc)
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private static void CacheResultIfUnchanged(string path, FileVersion? originalVersion, MediaProbeResult result)
        {
            if (originalVersion is not { } version || GetFileVersion(path) is not { } currentVersion || currentVersion != version)
            {
                return;
            }

            ProbeResultCache[path] = new ProbeCacheEntry(version, result);
        }

        private static async Task<MediaProbeResult> TryProbeWithFfprobeAsync(string path, CancellationToken cancellationToken)
        {
            if (System.Threading.Volatile.Read(ref _ffprobeAvailability) < 0)
            {
                return new MediaProbeResult();
            }

            Process? process = null;
            try
            {
                var executable = GetFfprobeExecutable();
                var startInfo = new ProcessStartInfo(executable)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                startInfo.ArgumentList.Add("-v");
                startInfo.ArgumentList.Add("error");
                startInfo.ArgumentList.Add("-show_format");
                startInfo.ArgumentList.Add("-show_streams");
                startInfo.ArgumentList.Add("-of");
                startInfo.ArgumentList.Add("json");
                startInfo.ArgumentList.Add(path);

                process = Process.Start(startInfo);
                if (process is null)
                {
                    return new MediaProbeResult();
                }

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(ProbeTimeout);
                await process.WaitForExitAsync(timeout.Token);
                var output = await outputTask;
                var error = await errorTask;
                if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
                {
                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        AppLogger.Error("FFprobe による技術情報の取得に失敗しました", new InvalidOperationException(error), path);
                    }
                    return new MediaProbeResult();
                }

                System.Threading.Interlocked.Exchange(ref _ffprobeAvailability, 1);
                return ParseFfprobeJson(output);
            }
            catch (OperationCanceledException)
            {
                if (process is { HasExited: false })
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                }

                throw;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // FFprobe は任意機能。PATH 上にない環境は Windows API へフォールバックする。
                System.Threading.Interlocked.Exchange(ref _ffprobeAvailability, -1);
                return new MediaProbeResult();
            }
            catch (Exception ex)
            {
                AppLogger.Error("FFprobe の実行に失敗しました", ex, path);
                return new MediaProbeResult();
            }
            finally
            {
                process?.Dispose();
            }
        }

        private static string GetFfprobeExecutable()
        {
            var environmentPath = Environment.GetEnvironmentVariable("FFPROBE_PATH");
            if (!string.IsNullOrWhiteSpace(environmentPath))
            {
                return environmentPath;
            }

            var configuredPath = SettingsManager.LoadSettings().FfprobePath;
            return string.IsNullOrWhiteSpace(configuredPath) ? "ffprobe" : configuredPath;
        }

        private static MediaProbeResult ParseFfprobeJson(string json)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var format = root.TryGetProperty("format", out var formatElement) ? formatElement : default;
            var streams = root.TryGetProperty("streams", out var streamsElement) && streamsElement.ValueKind == JsonValueKind.Array
                ? streamsElement.EnumerateArray().ToArray()
                : Array.Empty<JsonElement>();
            var video = streams.FirstOrDefault(stream => GetString(stream, "codec_type") == "video");
            var audio = streams.FirstOrDefault(stream => GetString(stream, "codec_type") == "audio");
            var technical = ParseTechnicalInfo(format, streams);

            return new MediaProbeResult
            {
                UsedFfprobe = true,
                Duration = TryGetDuration(format, "duration"),
                FrameWidth = TryGetInt(video, "width"),
                FrameHeight = TryGetInt(video, "height"),
                FrameRate = TryGetFrameRate(video),
                VideoBitrate = TryGetLong(video, "bit_rate") ?? TryGetLong(format, "bit_rate"),
                VideoCodec = GetString(video, "codec_long_name") ?? GetString(video, "codec_name") ?? string.Empty,
                AudioSampleRate = TryGetInt(audio, "sample_rate"),
                AudioBitrate = TryGetLong(audio, "bit_rate"),
                AudioCodec = GetString(audio, "codec_long_name") ?? GetString(audio, "codec_name") ?? string.Empty,
                Technical = technical
            };
        }

        private static VideoTechnicalInfo ParseTechnicalInfo(JsonElement format, JsonElement[] streams)
        {
            var technical = new VideoTechnicalInfo
            {
                Duration = TryGetDuration(format, "duration"),
                TotalBitrate = TryGetLong(format, "bit_rate")
            };

            foreach (var stream in streams)
            {
                var type = GetString(stream, "codec_type");
                switch (type)
                {
                    case "video":
                        technical.VideoStreams.Add(ParseVideoStream(stream, technical.Duration));
                        break;
                    case "audio":
                        technical.AudioStreams.Add(ParseAudioStream(stream, technical.Duration));
                        break;
                    case "subtitle":
                        technical.SubtitleStreams.Add(ParseSubtitleStream(stream));
                        break;
                }
            }

            return technical;
        }

        private static VideoStreamInfo ParseVideoStream(JsonElement stream, TimeSpan? formatDuration)
        {
            var tags = GetObject(stream, "tags");
            var sideData = GetArray(stream, "side_data");
            var masteringDisplay = sideData.FirstOrDefault(item =>
                (GetString(item, "side_data_type") ?? string.Empty).Contains("mastering", StringComparison.OrdinalIgnoreCase));
            var contentLight = sideData.FirstOrDefault(item =>
                (GetString(item, "side_data_type") ?? string.Empty).Contains("content light", StringComparison.OrdinalIgnoreCase));
            var projection = GetString(tags, "projection") ?? string.Empty;
            return new VideoStreamInfo
            {
                StreamIndex = TryGetInt(stream, "index"),
                TrackId = GetString(stream, "id") ?? string.Empty,
                TrackName = GetString(tags, "title") ?? GetString(tags, "handler_name") ?? string.Empty,
                Language = GetString(tags, "language") ?? string.Empty,
                Codec = GetString(stream, "codec_long_name") ?? GetString(stream, "codec_name") ?? string.Empty,
                Profile = GetString(stream, "profile") ?? string.Empty,
                Level = TryGetInt(stream, "level"),
                FourCc = GetString(stream, "codec_tag_string") ?? GetString(stream, "codec_tag") ?? string.Empty,
                Width = TryGetInt(stream, "width"),
                Height = TryGetInt(stream, "height"),
                FrameRate = TryGetFrameRate(stream),
                Bitrate = TryGetLong(stream, "bit_rate"),
                PixelAspectRatio = GetString(stream, "sample_aspect_ratio") ?? string.Empty,
                DisplayAspectRatio = GetString(stream, "display_aspect_ratio") ?? string.Empty,
                Rotation = TryGetRotation(stream),
                Hdr = GetString(stream, "color_transfer") is { Length: > 0 } transfer
                    ? transfer.Contains("smpte2084", StringComparison.OrdinalIgnoreCase) || transfer.Contains("arib-std-b67", StringComparison.OrdinalIgnoreCase)
                        ? transfer
                        : string.Empty
                    : string.Empty,
                ColorSpace = GetString(stream, "color_space") ?? string.Empty,
                ColorPrimaries = GetString(stream, "color_primaries") ?? string.Empty,
                TransferCharacteristics = GetString(stream, "color_transfer") ?? string.Empty,
                MatrixCoefficients = GetString(stream, "color_matrix") ?? string.Empty,
                MasteringDisplay = masteringDisplay.ValueKind == JsonValueKind.Object
                    ? masteringDisplay.GetRawText()
                    : string.Empty,
                MaxCll = TryGetDecimal(contentLight, "max_content") ?? TryGetDecimal(contentLight, "max_content_light_level"),
                MaxFall = TryGetDecimal(contentLight, "max_average") ?? TryGetDecimal(contentLight, "max_fall"),
                Stereo3D = GetString(stream, "stereo_mode") ?? string.Empty,
                Is360Video = projection.Contains("equirectangular", StringComparison.OrdinalIgnoreCase)
                    || sideData.Any(item => (GetString(item, "side_data_type") ?? string.Empty)
                        .Contains("spherical", StringComparison.OrdinalIgnoreCase)),
                Encoder = GetString(tags, "encoder") ?? GetString(tags, "ENCODER") ?? string.Empty,
                EncodingSettings = GetString(stream, "bits_per_raw_sample") ?? string.Empty,
                Duration = TryGetDuration(stream, "duration") ?? formatDuration
            };
        }

        private static AudioStreamInfo ParseAudioStream(JsonElement stream, TimeSpan? formatDuration)
        {
            var tags = GetObject(stream, "tags");
            return new AudioStreamInfo
            {
                StreamIndex = TryGetInt(stream, "index"),
                TrackId = GetString(stream, "id") ?? string.Empty,
                TrackName = GetString(tags, "title") ?? GetString(tags, "handler_name") ?? string.Empty,
                Language = GetString(tags, "language") ?? string.Empty,
                Codec = GetString(stream, "codec_long_name") ?? GetString(stream, "codec_name") ?? string.Empty,
                Profile = GetString(stream, "profile") ?? string.Empty,
                Bitrate = TryGetLong(stream, "bit_rate"),
                SampleRate = TryGetInt(stream, "sample_rate"),
                Channels = TryGetInt(stream, "channels"),
                ChannelLayout = GetString(stream, "channel_layout") ?? string.Empty,
                Encoder = GetString(tags, "encoder") ?? GetString(tags, "ENCODER") ?? string.Empty,
                Duration = TryGetDuration(stream, "duration") ?? formatDuration
            };
        }

        private static SubtitleStreamInfo ParseSubtitleStream(JsonElement stream)
        {
            var tags = GetObject(stream, "tags");
            return new SubtitleStreamInfo
            {
                StreamIndex = TryGetInt(stream, "index"),
                TrackId = GetString(stream, "id") ?? string.Empty,
                TrackName = GetString(tags, "title") ?? GetString(tags, "handler_name") ?? string.Empty,
                Language = GetString(tags, "language") ?? string.Empty,
                Codec = GetString(stream, "codec_long_name") ?? GetString(stream, "codec_name") ?? string.Empty,
                IsForced = string.Equals(GetString(tags, "forced"), "1", StringComparison.OrdinalIgnoreCase),
                IsDefault = string.Equals(GetString(tags, "default"), "1", StringComparison.OrdinalIgnoreCase)
            };
        }

        private static async Task<MediaProbeResult> ProbeWithWindowsAsync(StorageFile file, CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var properties = await file.Properties.GetVideoPropertiesAsync();
                return new MediaProbeResult
                {
                    Duration = properties.Duration > TimeSpan.Zero ? properties.Duration : null,
                    FrameWidth = properties.Width > 0 ? (int)properties.Width : null,
                    FrameHeight = properties.Height > 0 ? (int)properties.Height : null,
                    VideoBitrate = properties.Bitrate > 0 ? properties.Bitrate : null,
                    Technical = new VideoTechnicalInfo
                    {
                        Duration = properties.Duration > TimeSpan.Zero ? properties.Duration : null,
                        TotalBitrate = properties.Bitrate > 0 ? properties.Bitrate : null,
                        VideoStreams = new List<VideoStreamInfo>
                        {
                            new()
                            {
                                Width = properties.Width > 0 ? (int)properties.Width : null,
                                Height = properties.Height > 0 ? (int)properties.Height : null,
                                Bitrate = properties.Bitrate > 0 ? properties.Bitrate : null,
                                Duration = properties.Duration > TimeSpan.Zero ? properties.Duration : null
                            }
                        }
                    }
                };
            }
            catch (Exception ex)
            {
                AppLogger.Error("Windows API による技術情報の取得に失敗しました", ex, file.Path);
                return new MediaProbeResult();
            }
        }

        private static string? GetString(JsonElement element, string propertyName)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
            {
                return null;
            }

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null
            };
        }

        private static JsonElement GetObject(JsonElement element, string propertyName)
        {
            return element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(propertyName, out var value)
                && value.ValueKind == JsonValueKind.Object
                ? value
                : default;
        }

        private static JsonElement[] GetArray(JsonElement element, string propertyName)
        {
            return element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(propertyName, out var value)
                && value.ValueKind == JsonValueKind.Array
                ? value.EnumerateArray().ToArray()
                : Array.Empty<JsonElement>();
        }

        private static int? TryGetRotation(JsonElement stream)
        {
            var tags = GetObject(stream, "tags");
            return TryGetInt(tags, "rotate") ?? TryGetInt(stream, "rotation");
        }

        private static decimal? TryGetDecimal(JsonElement element, string propertyName)
        {
            var value = GetString(element, propertyName);
            return decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                ? number
                : null;
        }

        private static int? TryGetInt(JsonElement element, string propertyName)
        {
            var value = GetString(element, propertyName);
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number > 0 ? number : null;
        }

        private static long? TryGetLong(JsonElement element, string propertyName)
        {
            var value = GetString(element, propertyName);
            return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number > 0 ? number : null;
        }

        private static TimeSpan? TryGetDuration(JsonElement element, string propertyName)
        {
            var value = GetString(element, propertyName);
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0
                ? TimeSpan.FromSeconds(seconds)
                : null;
        }

        private static double? TryGetFrameRate(JsonElement stream)
        {
            var rational = GetString(stream, "avg_frame_rate");
            if (string.IsNullOrWhiteSpace(rational) || rational == "0/0")
            {
                rational = GetString(stream, "r_frame_rate");
            }

            var parts = rational?.Split('/');
            if (parts?.Length == 2
                && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator)
                && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator)
                && denominator > 0)
            {
                return numerator / denominator;
            }

            return null;
        }

        /// <summary>FFprobe 結果のメモリキャッシュをクリアする。フォルダ切り替え時に呼ぶ。</summary>
        public static void ClearCache()
        {
            ProbeResultCache.Clear();
        }
    }
}
