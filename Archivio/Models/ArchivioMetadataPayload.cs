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
        // v2: 旧フィールドを維持しつつ、MetadataDocument の全作品情報を保存する。
        // Artwork のバイナリ本体は既存の画像タグ領域に保存するためここには含めない。
        public WorkMetadata Work { get; set; } = new();
        public PeopleAndStaffMetadata PeopleAndStaff { get; set; } = new();
        public MetadataDates Dates { get; set; } = new();
        public IdentifierMetadata Identifiers { get; set; } = new();
        public AcquisitionMetadata Acquisition { get; set; } = new();
        public RightsMetadata Rights { get; set; } = new();
        public LocationMetadata Location { get; set; } = new();
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
