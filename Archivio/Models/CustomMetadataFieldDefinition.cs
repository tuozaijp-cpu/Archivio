namespace Archivio.Models
{
    /// <summary>ユーザー定義項目の設定。動画ごとの値は動画ファイル内に保存する。</summary>
    public sealed class CustomMetadataFieldDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string ValueType { get; set; } = "Text";
        public string Category { get; set; } = "Work";
        public bool IsInListByDefault { get; set; }
        public bool IsInDetailsByDefault { get; set; } = true;
        public bool IsEditable { get; set; } = true;
    }
}
