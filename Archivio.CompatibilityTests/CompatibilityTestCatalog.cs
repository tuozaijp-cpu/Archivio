namespace Archivio.CompatibilityTests;

internal sealed record CompatibilityTestCase(
    string Category,
    string Name,
    Action Action,
    bool RequiresFfmpeg = false)
{
    public string DisplayName => $"{Category} / {Name}";
}

/// <summary>
/// テストの分類と実行順だけを管理するカタログ。
/// 本体の単体テストフレームワークは導入せず、既存の単一プロセス実行を維持する。
/// </summary>
internal static class CompatibilityTestCatalog
{
    internal static IReadOnlyList<CompatibilityTestCase> All { get; } = new List<CompatibilityTestCase>
    {
        new("Metadata unit", "filename template service", Program.TestMetadataFileNameTemplateService),
        new("Metadata unit", "custom editor value conversion", Program.TestCustomMetadataEditorValueConversion),
        new("Metadata unit", "field format capabilities", Program.TestMetadataFieldFormatCapabilities),
        new("Metadata unit", "file save operation state", Program.TestFileSaveOperationState),
        new("Metadata unit", "video state compatibility facade", Program.TestVideoFileStateBoundaries),
        new("Metadata unit", "selection generation guard", Program.TestSelectionGenerationGuard),
        new("Metadata unit", "operation coordinator lifecycle", Program.TestOperationCoordinatorLifecycle),

        new("MP4/MKV integration", "create isolated media fixtures", Program.CreateFixtures, true),
        new("MP4/MKV integration", "verification preserves technical information", Program.TestVerificationPreservesTechnicalInformation, true),
        new("MP4/MKV integration", "MP4 standard/custom metadata roundtrip", Program.TestMp4RoundTrip, true),
        new("MP4/MKV integration", "MKV standard/custom metadata roundtrip", Program.TestMkvRoundTrip, true),
        new("MP4/MKV integration", "direct and backup save modes", Program.TestMetadataSaveModes, true),
        new("MP4/MKV integration", "standard-only save preserves payload", Program.TestStandardOnlySaveKeepsPayload, true),
        new("MP4/MKV integration", "multiple cover images roundtrip", Program.TestMultipleImages, true),
        new("MP4/MKV integration", "cover art image export preserves bytes", Program.TestCoverArtExport),
        new("MP4/MKV integration", "corrupt custom payload keeps standard tags", Program.TestCorruptCustomPayload, true),
        new("MP4/MKV integration", "technical streams", Program.TestTechnicalStreams, true),

        new("Read-only formats", "other format refuses writes", Program.TestOtherFormatReadOnly, true),

        new("FFmpeg integration", "command construction and discovery", Program.TestFfmpegMediaService, true),
        new("FFmpeg integration", "process runner lifecycle", Program.TestProcessRunner),
        new("FFmpeg integration", "frame capture and thumbnail sheet", Program.TestFfmpegMediaProcessing, true),

        new("Cache/settings migration", "thumbnail cache capacity and LRU", Program.TestThumbnailCacheCapacityAndLru),
        new("Cache/settings migration", "thumbnail cache mutations", Program.TestThumbnailCacheMutation),
        new("Cache/settings migration", "metadata loading retries and cancellation", Program.TestMetadataLoadCoordinator),
        new("Cache/settings migration", "legacy settings and snapshot migration", Program.TestLegacyMigration),
        new("Cache/settings migration", "display visibility and order settings", Program.TestDisplaySettings),
        new("Cache/settings migration", "unknown field IDs", Program.TestUnknownFieldIds),

        new("File commit/external changes", "file operation naming rules", Program.TestVideoFileOperationNamingRules),
        new("File commit/external changes", "metadata file commit modes", Program.TestMetadataFileCommitModes, true),
        new("File commit/external changes", "external modifications detected", Program.TestExternalModificationDetection, true),
        new("File commit/external changes", "failed save preserves original bytes", Program.TestFailedSavePreservesOriginal, true),
        new("File commit/external changes", "CSV row formatting", Program.TestVideoCsvRowFormatting)
    };
}
