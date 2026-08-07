using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Storage;

namespace Archivio.ViewModels
{
    /// <summary>動画ストリームの技術情報を、利用可能な解析器から取得する。</summary>
    public interface IMediaProbeService
    {
        Task<MediaProbeResult> ProbeAsync(StorageFile file);
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

        public bool HasValues => Duration is not null || FrameWidth is not null || FrameHeight is not null
            || FrameRate is not null || VideoBitrate is not null || !string.IsNullOrWhiteSpace(VideoCodec)
            || AudioSampleRate is not null || AudioBitrate is not null || !string.IsNullOrWhiteSpace(AudioCodec);
    }

    public sealed class MediaProbeService : IMediaProbeService
    {
        // 0: 未確認、1: 利用可能、-1: 起動不可。FFprobe 非導入環境での失敗プロセス生成を防ぐ。
        private static int _ffprobeAvailability;

        public async Task<MediaProbeResult> ProbeAsync(StorageFile file)
        {
            var ffprobeResult = await TryProbeWithFfprobeAsync(file.Path);
            if (ffprobeResult.HasValues)
            {
                return ffprobeResult;
            }

            return await ProbeWithWindowsAsync(file);
        }

        private static async Task<MediaProbeResult> TryProbeWithFfprobeAsync(string path)
        {
            if (System.Threading.Volatile.Read(ref _ffprobeAvailability) < 0)
            {
                return new MediaProbeResult();
            }

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
                startInfo.ArgumentList.Add("-show_entries");
                startInfo.ArgumentList.Add("format=duration,bit_rate:stream=codec_type,codec_name,codec_long_name,width,height,avg_frame_rate,r_frame_rate,bit_rate,sample_rate");
                startInfo.ArgumentList.Add("-of");
                startInfo.ArgumentList.Add("json");
                startInfo.ArgumentList.Add(path);

                using var process = Process.Start(startInfo);
                if (process is null)
                {
                    return new MediaProbeResult();
                }

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
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
                AudioCodec = GetString(audio, "codec_long_name") ?? GetString(audio, "codec_name") ?? string.Empty
            };
        }

        private static async Task<MediaProbeResult> ProbeWithWindowsAsync(StorageFile file)
        {
            try
            {
                var properties = await file.Properties.GetVideoPropertiesAsync();
                return new MediaProbeResult
                {
                    Duration = properties.Duration > TimeSpan.Zero ? properties.Duration : null,
                    FrameWidth = properties.Width > 0 ? (int)properties.Width : null,
                    FrameHeight = properties.Height > 0 ? (int)properties.Height : null,
                    VideoBitrate = properties.Bitrate > 0 ? properties.Bitrate : null
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
    }
}
