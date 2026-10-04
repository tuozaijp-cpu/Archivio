using System;
using System.Collections.Generic;
using System.Linq;

namespace Archivio.Models
{
    /// <summary>動画・音声・字幕ストリームを含む自動取得専用の技術情報。</summary>
    public sealed class VideoTechnicalInfo
    {
        public TimeSpan? Duration { get; set; }
        public long? TotalBitrate { get; set; }
        public List<VideoStreamInfo> VideoStreams { get; set; } = new();
        public List<AudioStreamInfo> AudioStreams { get; set; } = new();
        public List<SubtitleStreamInfo> SubtitleStreams { get; set; } = new();

        public VideoStreamInfo? PrimaryVideo => VideoStreams.FirstOrDefault();
        public AudioStreamInfo? PrimaryAudio => AudioStreams.FirstOrDefault();

        public bool HasValues => Duration is not null
            || TotalBitrate is not null
            || VideoStreams.Count > 0
            || AudioStreams.Count > 0
            || SubtitleStreams.Count > 0;
    }

    public sealed class VideoStreamInfo
    {
        public int? StreamIndex { get; set; }
        public string TrackId { get; set; } = string.Empty;
        public string TrackName { get; set; } = string.Empty;
        public string Language { get; set; } = string.Empty;
        public string Codec { get; set; } = string.Empty;
        public string Profile { get; set; } = string.Empty;
        public int? Level { get; set; }
        public string FourCc { get; set; } = string.Empty;
        public int? Width { get; set; }
        public int? Height { get; set; }
        public double? FrameRate { get; set; }
        public long? Bitrate { get; set; }
        public string PixelAspectRatio { get; set; } = string.Empty;
        public string DisplayAspectRatio { get; set; } = string.Empty;
        public int? Rotation { get; set; }
        public string Hdr { get; set; } = string.Empty;
        public string ColorSpace { get; set; } = string.Empty;
        public string ColorPrimaries { get; set; } = string.Empty;
        public string TransferCharacteristics { get; set; } = string.Empty;
        public string MatrixCoefficients { get; set; } = string.Empty;
        public string MasteringDisplay { get; set; } = string.Empty;
        public decimal? MaxCll { get; set; }
        public decimal? MaxFall { get; set; }
        public string Stereo3D { get; set; } = string.Empty;
        public bool Is360Video { get; set; }
        public string Encoder { get; set; } = string.Empty;
        public string EncodingSettings { get; set; } = string.Empty;
        public TimeSpan? Duration { get; set; }
    }

    public sealed class AudioStreamInfo
    {
        public int? StreamIndex { get; set; }
        public string TrackId { get; set; } = string.Empty;
        public string TrackName { get; set; } = string.Empty;
        public string Language { get; set; } = string.Empty;
        public string Codec { get; set; } = string.Empty;
        public string Profile { get; set; } = string.Empty;
        public long? Bitrate { get; set; }
        public int? SampleRate { get; set; }
        public int? Channels { get; set; }
        public string ChannelLayout { get; set; } = string.Empty;
        public string Encoder { get; set; } = string.Empty;
        public TimeSpan? Duration { get; set; }
    }

    public sealed class SubtitleStreamInfo
    {
        public int? StreamIndex { get; set; }
        public string TrackId { get; set; } = string.Empty;
        public string TrackName { get; set; } = string.Empty;
        public string Language { get; set; } = string.Empty;
        public string Codec { get; set; } = string.Empty;
        public bool IsForced { get; set; }
        public bool IsDefault { get; set; }
    }
}
