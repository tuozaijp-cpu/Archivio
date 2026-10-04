using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Archivio.Models;
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
        /// <summary>保存対象項目ごとのフォーマット能力。画面表示や警告に利用できる。</summary>
        public IReadOnlyList<MetadataFieldCapabilityResult> FieldCapabilities { get; init; } = Array.Empty<MetadataFieldCapabilityResult>();
        public IReadOnlyList<string> ArchivioMetadataErrors { get; init; } = Array.Empty<string>();
        /// <summary>保存後にファイルから再読み込みした実データ。</summary>
        public VideoMetadataSnapshot? ReloadedMetadata { get; init; }
        /// <summary>カバー画像保存後にファイルから再読み込みした実データ。</summary>
        public IReadOnlyList<CoverArtImageData> ReloadedCoverArtImages { get; init; } = Array.Empty<CoverArtImageData>();
    }

    public sealed class MetadataFieldCapabilityResult
    {
        public string FieldId { get; init; } = string.Empty;
        public MetadataFieldCapability Capability { get; init; }
        public bool CanRead { get; init; }
        public bool CanWrite { get; init; }
        public string? FailureReason { get; init; }
    }

    public sealed class VideoMetadataLoadResult
    {
        public VideoMetadataSnapshot Metadata { get; init; } = new();
        public VideoTechnicalInfo TechnicalInfo { get; init; } = new();
        public string? ArchivioMetadataError { get; init; }
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
        /// <summary>複数ストリームを含む自動取得専用の正規化技術情報。</summary>
        public VideoTechnicalInfo TechnicalInfo { get; set; } = new();
        public MetadataDocument StructuredMetadata { get; set; } = new();
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
        private readonly IVideoTechnicalMetadataService _technicalMetadataService;
        private readonly IEmbeddedVideoMetadataStore _embeddedMetadataStore;

        public VideoMetadataService(IMediaProbeService? mediaProbeService = null)
            : this(
                new VideoTechnicalMetadataService(mediaProbeService ?? new MediaProbeService()),
                new EmbeddedVideoMetadataStore())
        {
        }

        internal VideoMetadataService(
            IVideoTechnicalMetadataService technicalMetadataService,
            IEmbeddedVideoMetadataStore embeddedMetadataStore)
        {
            _technicalMetadataService = technicalMetadataService;
            _embeddedMetadataStore = embeddedMetadataStore;
        }

        public async Task<VideoMetadataLoadResult> LoadMetadataAsync(StorageFile file, CancellationToken cancellationToken = default)
        {
            var embeddedResult = await _embeddedMetadataStore.LoadAsync(file);
            var technicalResult = await _technicalMetadataService.LoadAsync(
                file,
                embeddedResult.Metadata,
                embeddedResult.TechnicalProperties,
                cancellationToken);
            return new VideoMetadataLoadResult
            {
                Metadata = embeddedResult.Metadata,
                TechnicalInfo = technicalResult.TechnicalInfo,
                ArchivioMetadataError = embeddedResult.ArchivioMetadataError,
                WindowsPropertiesLoaded = technicalResult.WindowsPropertiesLoaded,
                TechnicalPropertiesLoaded = technicalResult.TechnicalPropertiesLoaded,
                WindowsPropertyErrors = technicalResult.WindowsPropertyErrors
            };
        }

        public async Task<VideoMetadataOperationResult> SaveMetadataAsync(StorageFile file, VideoMetadataSnapshot metadata, VideoMetadataSnapshot? originalMetadata, IEnumerable<string> changedProperties)
        {
            return await _embeddedMetadataStore.SaveAsync(file, metadata, originalMetadata, changedProperties);
        }

        public async Task<IReadOnlyList<CoverArtImageData>> LoadCoverArtImagesAsync(StorageFile file)
        {
            return await _embeddedMetadataStore.LoadCoverArtImagesAsync(file);
        }

        /// <summary>
        /// 一覧表示用に、動画内の最初のカバーアートだけを読み込みます。
        /// 詳細表示用の全カバーアート読み込みとは独立した処理です。
        /// </summary>
        public async Task<CoverArtImageData?> LoadThumbnailImageAsync(StorageFile file, CancellationToken cancellationToken = default)
        {
            return await _embeddedMetadataStore.LoadThumbnailImageAsync(file, cancellationToken);
        }

        public async Task<VideoMetadataOperationResult> SaveCoverArtAsync(StorageFile file, IEnumerable<CoverArtImageData> coverArtImages)
        {
            return await _embeddedMetadataStore.SaveCoverArtAsync(file, coverArtImages);
        }

        public static string DecodeVideoSubtypeGuid(string guidStr)
        {
            return VideoTechnicalMetadataService.DecodeVideoSubtypeGuid(guidStr);
        }

        public static string MapVideoCompression(string rawValue)
        {
            return VideoTechnicalMetadataService.MapVideoCompression(rawValue);
        }

        public static string MapAudioFormat(string rawValue)
        {
            return VideoTechnicalMetadataService.MapAudioFormat(rawValue);
        }
    }
}
