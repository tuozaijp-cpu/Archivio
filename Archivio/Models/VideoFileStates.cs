using System.Collections.Generic;
using Archivio.ViewModels;

namespace Archivio.Models;

/// <summary>ファイルシステムに依存しない動画ファイルの識別・サイズ状態。</summary>
public sealed class VideoFileInfoState
{
    public string FileName { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public ulong FileSizeBytes { get; set; }
    public string FileSizeText { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
}

/// <summary>保存対象外の解析済み技術情報。FFprobe/TagLibの結果だけを保持する。</summary>
public sealed class VideoTechnicalState
{
    public VideoTechnicalInfo Info { get; set; } = new();
}

/// <summary>画面表示形式に依存しない埋め込み画像の状態。</summary>
public sealed class VideoCoverArtState
{
    public List<CoverArtImageData> Images { get; set; } = new();
    public bool HasCoverArt { get; set; }
    public bool IsLoaded { get; set; }
    public bool IsDirty { get; set; }
    public int SelectedIndex { get; set; }
    public int OriginalImageCount { get; set; }
    public int OriginalImageHash { get; set; }
}

/// <summary>安定したカスタム項目IDと保存状態を管理する状態。</summary>
public sealed class VideoChangeTrackingState
{
    public HashSet<string> DirtyCustomFieldIds { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> OriginalValues { get; } = new(StringComparer.OrdinalIgnoreCase);
    public MetadataDocument OriginalStructuredMetadata { get; set; } = new();
    public string OriginalStructuredMetadataJson { get; set; } = string.Empty;
    public int ActiveFileSaveOperationCount { get; set; }
}

/// <summary>
/// 画面のBitmapImageやStorageFileを含まない動画の編集状態。
/// VideoFileItemは既存バインディング向けの互換ファサードとしてこの状態を公開する。
/// </summary>
public sealed class VideoMetadataState
{
    public MetadataDocument StructuredMetadata { get; set; } = new();
    public VideoTechnicalInfo TechnicalInfo { get; set; } = new();
    public string Title { get; set; } = string.Empty;
    public string Participants { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public string CatalogNumber { get; set; } = string.Empty;
    public string Rating { get; set; } = string.Empty;
    public string ContentDistributor { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public string Duration { get; set; } = string.Empty;
    public TimeSpan? DurationValue { get; set; }
    public string FrameWidth { get; set; } = string.Empty;
    public string FrameHeight { get; set; } = string.Empty;
    public string FrameRate { get; set; } = string.Empty;
    public string VideoBitrate { get; set; } = string.Empty;
    public string VideoCompression { get; set; } = string.Empty;
    public string AudioSampleRate { get; set; } = string.Empty;
    public string AudioBitrate { get; set; } = string.Empty;
    public string AudioFormat { get; set; } = string.Empty;
    public string ReleaseDateText { get; set; } = string.Empty;
    public DateTimeOffset ReleaseDate { get; set; } = new(1900, 1, 1, 0, 0, 0, TimeSpan.Zero);
}
