using System;
using System.Collections.Generic;

namespace Archivio.Models
{
    /// <summary>動画ファイル内へ埋め込むArchivio独自メタデータのペイロード。</summary>
    public sealed class ArchivioMetadataPayload
    {
        public string SchemaVersion { get; set; } = "1";
        public string OriginalTitle { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public List<string> AlternateTitles { get; set; } = new();
        public List<CastCredit> Cast { get; set; } = new();
        public SeriesMetadata Series { get; set; } = new();
        public string ArchivioId { get; set; } = string.Empty;
        public string SourceFileId { get; set; } = string.Empty;
        public EvaluationMetadata Evaluation { get; set; } = new();
        public Dictionary<string, MetadataValue> CustomFields { get; set; } = new(StringComparer.Ordinal);
    }

    public sealed class ArchivioMetadataReadResult
    {
        public bool IsPresent { get; init; }
        public ArchivioMetadataPayload? Payload { get; init; }
        public string? Error { get; init; }
    }
}
