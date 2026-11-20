using Archivio.Models;
using Windows.Storage;

namespace Archivio.ViewModels;

/// <summary>FFmpegと技術解析を画面から隠すメディア処理境界。</summary>
public sealed class VideoMediaProcessingService
{
    private readonly FfmpegMediaService _ffmpeg = new();
    private readonly MediaProbeService _probe = new();

    public Task<string?> FindFfmpegAsync(string? configuredPath, CancellationToken cancellationToken = default)
        => _ffmpeg.FindFfmpegAsync(configuredPath, cancellationToken);

    public Task<byte[]> CaptureFrameAsync(string ffmpegPath, string path, TimeSpan position, CancellationToken cancellationToken)
        => _ffmpeg.CaptureFrameAsync(ffmpegPath, path, position, cancellationToken);

    public Task<byte[]> CreateThumbnailSheetAsync(string ffmpegPath, string path, TimeSpan duration, CancellationToken cancellationToken)
        => _ffmpeg.CreateThumbnailSheetAsync(ffmpegPath, path, duration, cancellationToken);

    public Task<MediaProbeResult> ProbeAsync(StorageFile file, CancellationToken cancellationToken = default)
        => _probe.ProbeAsync(file, cancellationToken);
}
