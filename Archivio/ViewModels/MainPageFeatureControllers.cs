using Archivio.Models;
using System.Text.Json;
using Windows.Storage;

namespace Archivio.ViewModels;

/// <summary>動画一覧に関する画面コマンドのController。</summary>
public sealed class VideoListController
{
    private readonly MainPageViewModel _viewModel;

    public VideoListController(MainPageViewModel viewModel) => _viewModel = viewModel;

    public Task ChooseFolderAsync() => _viewModel.ChooseFolderAsync();
    public Task RefreshAsync() => _viewModel.RefreshFilesAsync();
    public Task ExportCsvAsync() => _viewModel.ExportVideosToCsvAsync();
    public void CancelRefresh() => _viewModel.CancelRefresh();
}

/// <summary>詳細ペインのメタデータ・カバー画像コマンドのController。</summary>
public sealed class VideoDetailsController
{
    private readonly VideoDetailsViewModel _viewModel;

    public VideoDetailsController(VideoDetailsViewModel viewModel) => _viewModel = viewModel;

    public Task SaveMetadataAsync() => _viewModel.SaveMetadataAsync();
    public Task SaveCoverArtAsync() => _viewModel.SaveCoverArtAsync();
    public Task ExportCoverArtAsync() => _viewModel.ExportCoverArtAsync();
    public Task AddCoverArtAsync() => _viewModel.AddCoverArtImageAsync();
    public Task AddGeneratedCoverArtAsync(byte[] data, string mimeType, string description)
        => _viewModel.AddGeneratedCoverArtImageAsync(data, mimeType, description);
    public Task ReplaceCoverArtAsync() => _viewModel.ReplaceCoverArtImageAsync();
    public void DeleteCoverArt() => _viewModel.DeleteCoverArtImage();
    public Task ImportCoverArtAsync(StorageFile file, bool replaceSelected)
        => _viewModel.ImportCoverArtImageAsync(file, replaceSelected);
}

/// <summary>動画ファイル操作の画面境界。実際のI/Oは既存サービスへ委譲する。</summary>
public sealed class VideoFileOperationsController
{
    private readonly MainPageViewModel _viewModel;

    public VideoFileOperationsController(MainPageViewModel viewModel) => _viewModel = viewModel;

    public Task RenameAsync(VideoFileItem item, string newName)
        => _viewModel.RenameVideoAsync(item, newName);

    public Task DeleteAsync(VideoFileItem item)
        => _viewModel.DeleteVideoAsync(item);

    public Task ReMuxAsync(VideoFileItem item)
        => _viewModel.ReMuxVideoAsync(item);

    public Task ClearCacheAsync() => _viewModel.ClearCurrentFolderCacheAsync();
}

/// <summary>FFmpeg・技術解析とStorageFile取得を画面から隠すController。</summary>
public sealed class VideoMediaController
{
    private readonly VideoMediaProcessingService _processingService;

    public VideoMediaController(VideoMediaProcessingService? processingService = null)
        => _processingService = processingService ?? new VideoMediaProcessingService();

    public Task<string?> FindFfmpegAsync(string? configuredPath, CancellationToken cancellationToken = default)
        => _processingService.FindFfmpegAsync(configuredPath, cancellationToken);

    public async Task<byte[]> CaptureFrameAsync(
        VideoFileItem item,
        string ffmpegPath,
        TimeSpan position,
        CancellationToken cancellationToken = default)
    {
        var file = await VideoFileStorageService.GetFileAsync(item);
        return await _processingService.CaptureFrameAsync(ffmpegPath, file.Path, position, cancellationToken);
    }

    public async Task<byte[]> CreateThumbnailSheetAsync(
        VideoFileItem item,
        string ffmpegPath,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        var file = await VideoFileStorageService.GetFileAsync(item);
        return await _processingService.CreateThumbnailSheetAsync(ffmpegPath, file.Path, duration, cancellationToken);
    }

    public Task<MediaProbeResult> ProbeAsync(StorageFile file, CancellationToken cancellationToken = default)
        => _processingService.ProbeAsync(file, cancellationToken);
}

public sealed record LanguageOption(string Code, string DisplayName);

/// <summary>ローカルリソースとREADMEのファイルアクセスを画面から分離する。</summary>
public sealed class MainPageResourceController
{
    public IReadOnlyList<LanguageOption> GetLanguages()
    {
        var localeDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Locale");
        if (!Directory.Exists(localeDirectory)) return Array.Empty<LanguageOption>();

        return Directory.GetFiles(localeDirectory, "*.json")
            .Select(path => CreateLanguageOption(path))
            .ToArray();
    }

    public Task<StorageFile> GetReadmeAsync()
        => StorageFile.GetFileFromPathAsync(Path.Combine(AppContext.BaseDirectory, "README.md")).AsTask();

    private static LanguageOption CreateLanguageOption(string path)
    {
        var code = Path.GetFileNameWithoutExtension(path);
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var name = document.RootElement.TryGetProperty("LanguageName", out var property)
                ? property.GetString()
                : null;
            return new LanguageOption(code, string.IsNullOrWhiteSpace(name) ? code : name);
        }
        catch
        {
            return new LanguageOption(code, code);
        }
    }
}

/// <summary>一覧・詳細・ファイル操作ControllerのComposition Root。</summary>
public sealed class MainPageFeatureControllers
{
    public MainPageFeatureControllers(MainPageViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        List = new VideoListController(viewModel);
        Details = new VideoDetailsController(viewModel.Details);
        FileOperations = new VideoFileOperationsController(viewModel);
        Media = new VideoMediaController();
        Resources = new MainPageResourceController();
    }

    public VideoListController List { get; }
    public VideoDetailsController Details { get; }
    public VideoFileOperationsController FileOperations { get; }
    public VideoMediaController Media { get; }
    public MainPageResourceController Resources { get; }
}
