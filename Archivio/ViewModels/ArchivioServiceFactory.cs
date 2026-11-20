using Archivio.Models;

namespace Archivio.ViewModels;

/// <summary>
/// Archivioの最小Composition Root。DIコンテナーは使用せず、アプリケーション境界で
/// 共有サービスを一度だけ生成する。テストでは内部コンストラクターにFakeを渡せる。
/// </summary>
public sealed class ArchivioServiceFactory
{
    private static readonly Lazy<ArchivioServiceFactory> DefaultFactory =
        new(() => new ArchivioServiceFactory());

    internal ArchivioServiceFactory(
        IVideoMetadataService? metadataService = null,
        IVideoFileService? fileService = null,
        IVideoFileOperationsService? fileOperationsService = null,
        IVideoCsvExportService? csvExportService = null,
        ISettingsService? settingsService = null,
        VideoMediaProcessingService? mediaProcessingService = null,
        CoverArtExportService? coverArtExportService = null)
    {
        MetadataService = metadataService ?? new VideoMetadataService();
        FileService = fileService ?? new VideoFileService();
        FileOperationsService = fileOperationsService ?? new VideoFileOperationsService();
        CsvExportService = csvExportService ?? new VideoCsvExportService();
        SettingsService = settingsService ?? new MainPageSettingsService();
        MediaProcessingService = mediaProcessingService ?? new VideoMediaProcessingService();
        CoverArtExportService = coverArtExportService ?? new CoverArtExportService();
    }

    public static ArchivioServiceFactory Default => DefaultFactory.Value;

    internal IVideoMetadataService MetadataService { get; }
    internal IVideoFileService FileService { get; }
    internal IVideoFileOperationsService FileOperationsService { get; }
    internal IVideoCsvExportService CsvExportService { get; }
    internal ISettingsService SettingsService { get; }
    internal VideoMediaProcessingService MediaProcessingService { get; }
    internal CoverArtExportService CoverArtExportService { get; }

    public MainPageViewModel CreateMainPageViewModel()
        => new(this);

    public MainPageFeatureControllers CreateFeatureControllers(MainPageViewModel viewModel)
        => new(viewModel, new VideoMediaController(MediaProcessingService));
}
