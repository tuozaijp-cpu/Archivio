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
        new("Metadata unit", "filename template service", MetadataUnitTests.TestMetadataFileNameTemplateService),
        new("Metadata unit", "custom editor value conversion", MetadataUnitTests.TestCustomMetadataEditorValueConversion),
        new("Metadata unit", "field format capabilities", MetadataUnitTests.TestMetadataFieldFormatCapabilities),
        new("Metadata unit", "file save operation state", MetadataUnitTests.TestFileSaveOperationState),
        new("Metadata unit", "video state compatibility facade", MetadataUnitTests.TestVideoFileStateBoundaries),
        new("Metadata unit", "selection generation guard", MetadataUnitTests.TestSelectionGenerationGuard),
        new("Metadata unit", "operation coordinator lifecycle", MetadataUnitTests.TestOperationCoordinatorLifecycle),

        new("MP4/MKV integration", "create isolated media fixtures", Mp4MkvIntegrationTests.CreateFixtures, true),
        new("MP4/MKV integration", "verification preserves technical information", Mp4MkvIntegrationTests.TestVerificationPreservesTechnicalInformation, true),
        new("MP4/MKV integration", "MP4 standard/custom metadata roundtrip", Mp4MkvIntegrationTests.TestMp4RoundTrip, true),
        new("MP4/MKV integration", "MKV standard/custom metadata roundtrip", Mp4MkvIntegrationTests.TestMkvRoundTrip, true),
        new("MP4/MKV integration", "direct and backup save modes", Mp4MkvIntegrationTests.TestMetadataSaveModes, true),
        new("MP4/MKV integration", "standard-only save preserves payload", Mp4MkvIntegrationTests.TestStandardOnlySaveKeepsPayload, true),
        new("MP4/MKV integration", "multiple cover images roundtrip", Mp4MkvIntegrationTests.TestMultipleImages, true),
        new("MP4/MKV integration", "cover art image export preserves bytes", Mp4MkvIntegrationTests.TestCoverArtExport),
        new("MP4/MKV integration", "corrupt custom payload keeps standard tags", Mp4MkvIntegrationTests.TestCorruptCustomPayload, true),
        new("MP4/MKV integration", "typed metadata save errors", Mp4MkvIntegrationTests.TestTypedMetadataSaveErrors, true),
        new("MP4/MKV integration", "technical streams", Mp4MkvIntegrationTests.TestTechnicalStreams, true),

        new("Read-only formats", "other format refuses writes", ReadOnlyFormatTests.TestOtherFormatReadOnly, true),

        new("FFmpeg integration", "command construction and discovery", FfmpegIntegrationTests.TestFfmpegMediaService, true),
        new("FFmpeg integration", "process runner lifecycle", TestProcessRunner.RunProcessRunnerLifecycle),
        new("FFmpeg integration", "frame capture and thumbnail sheet", FfmpegIntegrationTests.TestFfmpegMediaProcessing, true),

        new("Cache/settings migration", "thumbnail cache capacity and LRU", CacheMigrationTests.TestThumbnailCacheCapacityAndLru),
        new("Cache/settings migration", "thumbnail cache mutations", CacheMigrationTests.TestThumbnailCacheMutation),
        new("Cache/settings migration", "metadata loading retries and cancellation", CacheMigrationTests.TestMetadataLoadCoordinator),
        new("Cache/settings migration", "legacy settings and snapshot migration", CacheMigrationTests.TestLegacyMigration),
        new("Cache/settings migration", "display visibility and order settings", CacheMigrationTests.TestDisplaySettings),
        new("Cache/settings migration", "unknown field IDs", CacheMigrationTests.TestUnknownFieldIds),

        new("File commit/external changes", "file operation naming rules", FileCommitTests.TestVideoFileOperationNamingRules),
        new("File commit/external changes", "metadata file commit modes", FileCommitTests.TestMetadataFileCommitModes, true),
        new("File commit/external changes", "external modifications detected", FileCommitTests.TestExternalModificationDetection, true),
        new("File commit/external changes", "failed save preserves original bytes", FileCommitTests.TestFailedSavePreservesOriginal, true),
        new("File commit/external changes", "CSV row formatting", FileCommitTests.TestVideoCsvRowFormatting)
    };
}
