using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Archivio.Helpers;
using Archivio.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace Archivio.ViewModels
{
    /// <summary>
    /// 動画ファイル一覧と詳細情報の状態を管理する ViewModel。
    /// </summary>
    public sealed class MainPageViewModel : ViewModelBase
    {
        private ObservableCollection<VideoFileItem> _videos = new();
        private VideoFileItem? _selectedVideo;
        private string _folderPath = string.Empty;
        private bool _isBusy;
        private CancellationTokenSource? _loadingCts;
        private CancellationTokenSource? _refreshCts;
        private readonly SemaphoreSlim _metadataReadSemaphore = new(8, 8);
        private readonly SemaphoreSlim _metadataSaveSemaphore = new(1, 1);
        private readonly IVideoMetadataService _metadataService;
        private readonly IVideoFileService _fileService;
        private string _statusMessage = string.Empty;
        private bool _hasOperationError;
        private bool _includeSubfolders = true;
        private readonly VideoDetailsViewModel _details;

        private readonly VideoListFilterManager _filterManager = new();
        private readonly List<VideoFileItem> _allVideosList = new();
        private readonly ConcurrentDictionary<string, VideoMetadataSnapshot> _metadataCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, ulong> _fileSizeCache = new(StringComparer.OrdinalIgnoreCase);

        public MainPageViewModel() : this(new VideoMetadataService(), new VideoFileService())
        {
        }

        internal MainPageViewModel(IVideoMetadataService metadataService, IVideoFileService fileService)
        {
            _metadataService = metadataService;
            _fileService = fileService;
            _includeSubfolders = SettingsManager.LoadSettings().IncludeSubfolders;

            _details = new VideoDetailsViewModel(
                _metadataService,
                _metadataReadSemaphore,
                _metadataSaveSemaphore,
                busy => IsBusy = busy,
                (msg, ex, path) => SetStatusError(msg, ex, path),
                msg => SetStatusSuccess(msg)
            );
        }

        public VideoDetailsViewModel Details => _details;

        public ObservableCollection<VideoFileItem> Videos
        {
            get => _videos;
            set
            {
                if (SetProperty(ref _videos, value))
                {
                    OnPropertyChanged(nameof(CanExportCsv));
                }
            }
        }

        public VideoFileItem? SelectedVideo
        {
            get => _selectedVideo;
            set
            {
                if (_selectedVideo is not null)
                {
                    _selectedVideo.PropertyChanged -= SelectedVideo_PropertyChanged;
                }

                if (SetProperty(ref _selectedVideo, value))
                {
                    if (_selectedVideo is not null)
                    {
                        _selectedVideo.PropertyChanged += SelectedVideo_PropertyChanged;
                    }

                    StatusMessage = string.Empty;
                    HasOperationError = false;

                    _details.SelectedVideo = value;

                    OnPropertyChanged(nameof(HasPendingChanges));
                    OnPropertyChanged(nameof(IsVideoSelected));
                    OnPropertyChanged(nameof(DetailedPanelVisibility));
                    _ = LoadSelectedVideoAsync();
                }
            }
        }

        public string FolderPath
        {
            get => _folderPath;
            set
            {
                if (SetProperty(ref _folderPath, value))
                {
                    UpdateWindowTitle();
                }
            }
        }

        private void UpdateWindowTitle()
        {
            if (App.MainWindow is not null)
            {
                App.MainWindow.DispatcherQueue.TryEnqueue(() =>
                {
                    App.MainWindow.Title = string.IsNullOrWhiteSpace(FolderPath)
                        ? "Archivio"
                        : $"Archivio - {FolderPath}";
                });
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        public bool IncludeSubfolders
        {
            get => _includeSubfolders;
            set => SetProperty(ref _includeSubfolders, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set
            {
                if (SetProperty(ref _statusMessage, value))
                {
                    OnPropertyChanged(nameof(StatusMessageVisibility));
                }
            }
        }

        public bool HasOperationError
        {
            get => _hasOperationError;
            private set
            {
                if (SetProperty(ref _hasOperationError, value))
                {
                    OnPropertyChanged(nameof(StatusMessageVisibility));
                }
            }
        }

        public Visibility StatusMessageVisibility => string.IsNullOrWhiteSpace(StatusMessage) ? Visibility.Collapsed : Visibility.Visible;

        public bool HasPendingChanges => SelectedVideo?.HasPendingChanges ?? false;

        public bool IsVideoSelected => SelectedVideo is not null && !SelectedVideo.IsDeleted;

        public Visibility DetailedPanelVisibility => IsVideoSelected ? Visibility.Visible : Visibility.Collapsed;

        public string FilterText
        {
            get => _filterManager.FilterText;
            set
            {
                if (_filterManager.FilterText != value)
                {
                    _filterManager.FilterText = value;
                    OnPropertyChanged();
                    ApplyFilterAndSort();
                }
            }
        }

        public List<string> GetUniqueValuesForProperty(string propertyName)
        {
            return _filterManager.GetUniqueValuesForProperty(_allVideosList, propertyName);
        }

        public HashSet<string>? GetColumnFilterValues(string propertyName)
        {
            return _filterManager.GetColumnFilterValues(propertyName);
        }

        public void SetColumnFilterValues(string propertyName, List<string> allowedValues)
        {
            _filterManager.SetColumnFilterValues(propertyName, allowedValues);
            ApplyFilterAndSort();
        }

        public void ClearColumnFilter(string propertyName)
        {
            _filterManager.ClearColumnFilter(propertyName);
            ApplyFilterAndSort();
        }

        public void Sort(string column, bool ascending)
        {
            _filterManager.Sort(column, ascending);
            ApplyFilterAndSort();
        }

        private void ApplyFilterAndSort()
        {
            var resultList = _filterManager.Apply(_allVideosList).ToList();
            var previouslySelected = SelectedVideo;

            Videos = new ObservableCollection<VideoFileItem>(resultList);

            if (previouslySelected != null && resultList.Contains(previouslySelected))
            {
                SelectedVideo = previouslySelected;
            }
            else
            {
                SelectedVideo = null;
            }
        }

        public bool CanExportCsv => Videos is not null && Videos.Count > 0;

        public async Task ChooseFolderAsync()
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");

            if (App.MainWindow is not null)
            {
                var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
                InitializeWithWindow.Initialize(picker, hwnd);
            }

            var folder = await picker.PickSingleFolderAsync();
            if (folder is null)
            {
                return;
            }

            FolderPath = folder.Path;
            Videos = new ObservableCollection<VideoFileItem>();
            SelectedVideo = null;
            await RefreshFilesAsync();
        }

        public async Task RefreshFilesAsync()
        {
            if (string.IsNullOrWhiteSpace(FolderPath))
            {
                return;
            }

            _refreshCts?.Cancel();
            _refreshCts?.Dispose();
            _refreshCts = new CancellationTokenSource();
            var cancellationToken = _refreshCts.Token;
            IsBusy = true;
            HasOperationError = false;
            StatusMessage = "動画ファイルを検索しています…";
            try
            {
                _metadataCache.Clear();
                _fileSizeCache.Clear();
                var folder = await StorageFolder.GetFolderFromPathAsync(FolderPath);
                var files = await _fileService.EnumerateVideoFilesAsync(folder, IncludeSubfolders, cancellationToken);

                var items = new ObservableCollection<VideoFileItem>();
                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var item = new VideoFileItem(file);
                        await LoadBasicInfoAsync(item);
                        items.Add(item);
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Error("動画の基本情報の読み込みに失敗しました", ex, file.Path);
                    }
                }

                Videos = items;
                _allVideosList.Clear();
                _allVideosList.AddRange(items);
                _filterManager.ClearAllFilters();
                OnPropertyChanged(nameof(FilterText));
                OnPropertyChanged(nameof(CanExportCsv));
                SelectedVideo = null;

                StatusMessage = $"{items.Count} 件の動画を読み込みました";
                _ = LoadAllPropertiesSequentiallyAsync(items, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "読み込みを中止しました";
            }
            catch (Exception ex)
            {
                AppLogger.Error("動画一覧の読み込みに失敗しました", ex, FolderPath);
                Videos = new ObservableCollection<VideoFileItem>();
                _allVideosList.Clear();
                _filterManager.ClearAllFilters();
                SelectedVideo = null;
                OnPropertyChanged(nameof(CanExportCsv));
                SetStatusError("動画一覧の読み込みに失敗しました。詳細はログを確認してください。", ex);
            }
            finally
            {
                IsBusy = false;
                if (_refreshCts?.Token == cancellationToken)
                {
                    _refreshCts.Dispose();
                    _refreshCts = null;
                }
            }
        }

        public void CancelRefresh()
        {
            _refreshCts?.Cancel();
        }

        public async Task RenameVideoAsync(VideoFileItem item, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName) || newName == item.File.Name)
            {
                return;
            }

            var oldExt = System.IO.Path.GetExtension(item.File.Path);
            var newExt = System.IO.Path.GetExtension(newName);
            if (string.IsNullOrWhiteSpace(newExt) || !string.Equals(oldExt, newExt, StringComparison.OrdinalIgnoreCase))
            {
                newName = System.IO.Path.GetFileNameWithoutExtension(newName) + oldExt;
            }

            item.IsSaving = true;
            HasOperationError = false;
            try
            {
                await item.File.RenameAsync(newName, NameCollisionOption.FailIfExists);
                item.SyncFromStorageFile();
                SetStatusSuccess("ファイル名を変更しました");
            }
            catch (Exception ex)
            {
                item.SyncFromStorageFile();
                SetStatusError($"ファイル名の変更に失敗しました: {ex.Message}", ex, item.File.Path);
            }
            finally
            {
                item.IsSaving = false;
            }
        }

        public async Task DeleteVideoAsync(VideoFileItem item)
        {
            if (item == null)
            {
                return;
            }

            item.IsSaving = true;
            HasOperationError = false;
            try
            {
                await item.File.DeleteAsync();
                item.IsDeleted = true;
                if (SelectedVideo == item)
                {
                    SelectedVideo = null;
                }
                _allVideosList.Remove(item);
                Videos.Remove(item);
                SetStatusSuccess("ファイルを削除しました");
            }
            catch (Exception ex)
            {
                SetStatusError($"ファイルの削除に失敗しました: {ex.Message}", ex, item.File.Path);
            }
            finally
            {
                item.IsSaving = false;
            }
        }

        public async Task ReMuxVideoAsync(VideoFileItem item)
        {
            if (item == null)
            {
                return;
            }

            IsBusy = true;
            HasOperationError = false;
            StatusMessage = "FFmpegで再MUX処理中…";

            try
            {
                var inputPath = item.File.Path;
                var dir = System.IO.Path.GetDirectoryName(inputPath);
                if (string.IsNullOrWhiteSpace(dir))
                {
                    throw new Exception("フォルダパスの取得に失敗しました。");
                }

                var baseName = System.IO.Path.GetFileNameWithoutExtension(inputPath);
                var ext = System.IO.Path.GetExtension(inputPath) ?? ".mp4";
                var outPath = System.IO.Path.Combine(dir, baseName + "_ReMUX" + ext);

                string movflags = "";
                if (ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
                    ext.Equals(".m4v", StringComparison.OrdinalIgnoreCase) ||
                    ext.Equals(".mov", StringComparison.OrdinalIgnoreCase) ||
                    ext.Equals(".3gp", StringComparison.OrdinalIgnoreCase))
                {
                    movflags = " -movflags +faststart";
                }

                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = $"-y -i \"{inputPath}\" -c copy{movflags} \"{outPath}\"",
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                await Task.Run(async () =>
                {
                    using var process = System.Diagnostics.Process.Start(startInfo);
                    if (process == null)
                    {
                        throw new Exception("FFmpegプロセスの起動に失敗しました。");
                    }

                    var errorText = await process.StandardError.ReadToEndAsync();
                    await process.WaitForExitAsync();

                    if (process.ExitCode != 0)
                    {
                        throw new Exception($"FFmpegがエラーコード {process.ExitCode} で終了しました。\n{errorText}");
                    }
                });

                SetStatusSuccess("再MUX処理が完了しました");
                await RefreshFilesAsync();
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 2)
            {
                SetStatusError("ffmpegがシステムにインストールされていないか、環境変数 PATH に登録されていません。", ex, item.File.Path);
            }
            catch (Exception ex)
            {
                SetStatusError($"再MUX処理に失敗しました: {ex.Message}", ex, item.File.Path);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task LoadAllPropertiesSequentiallyAsync(ObservableCollection<VideoFileItem> items, CancellationToken cancellationToken)
        {
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Min(4, Math.Max(1, items.Count)),
                CancellationToken = cancellationToken
            };

            try
            {
                await Parallel.ForEachAsync(items, options, async (item, ct) =>
                {
                    if (Videos != items || item.IsLoaded)
                    {
                        return;
                    }

                    try
                    {
                        var loaded = await LoadWindowsPropertiesAsync(item);

                        if (!loaded || Videos != items)
                        {
                            return;
                        }

                        await DispatcherHelper.RunOnUIThreadAsync(() =>
                        {
                            item.IsLoaded = true;
                        });
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Error("バックグラウンドでのメタデータ読み込みに失敗しました", ex, item.File.Path);
                    }
                });
            }
            catch (OperationCanceledException)
            {
            }
        }



        private async Task LoadSelectedVideoAsync()
        {
            _loadingCts?.Cancel();
            _loadingCts = new CancellationTokenSource();
            var cts = _loadingCts;

            if (SelectedVideo is null || SelectedVideo.IsDeleted)
            {
                return;
            }

            var currentVideo = SelectedVideo;

            if (currentVideo.IsLoaded)
            {
                OnPropertyChanged(nameof(HasPendingChanges));
                return;
            }

            IsBusy = true;
            try
            {
                cts.Token.ThrowIfCancellationRequested();

                await LoadWindowsPropertiesAsync(currentVideo);

                if (!cts.IsCancellationRequested && SelectedVideo == currentVideo)
                {
                    await DispatcherHelper.RunOnUIThreadAsync(() =>
                    {
                        currentVideo.IsLoaded = true;
                        OnPropertyChanged(nameof(HasPendingChanges));
                    });
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                AppLogger.Error("選択中の動画の読み込みに失敗しました", ex, currentVideo.File.Path);
                if (SelectedVideo == currentVideo)
                {
                    SetStatusError("選択中の動画の読み込みに失敗しました。詳細はログを確認してください。", ex, currentVideo.File.Path);
                }
            }
            finally
            {
                if (!cts.IsCancellationRequested)
                {
                    IsBusy = false;
                }
            }
        }

        private async Task LoadBasicInfoAsync(VideoFileItem item)
        {
            try
            {
                if (_fileSizeCache.TryGetValue(item.File.Path, out var cachedSize))
                {
                    item.FileSizeBytes = cachedSize;
                    item.FileSizeText = DisplayFormatHelper.FormatFileSize(cachedSize);
                    return;
                }

                var size = await _fileService.GetFileSizeAsync(item.File);
                _fileSizeCache[item.File.Path] = size;
                item.FileSizeBytes = size;
                item.FileSizeText = DisplayFormatHelper.FormatFileSize(size);
            }
            catch (Exception ex)
            {
                AppLogger.Error("基本情報の読み込みに失敗しました", ex, item.File.Path);
                item.FileSizeBytes = 0;
                item.FileSizeText = string.Empty;
            }
        }

        private async Task<bool> LoadWindowsPropertiesAsync(VideoFileItem item, bool force = false)
        {
            await _metadataReadSemaphore.WaitAsync();
            try
            {
                if (!force && item.IsLoaded)
                {
                    return true;
                }

                if (!force && _metadataCache.TryGetValue(item.File.Path, out var cachedMetadata))
                {
                    await ApplyMetadataToItemAsync(item, cachedMetadata);
                    return true;
                }

                VideoMetadataLoadResult? loadResult = null;
                for (var attempt = 1; attempt <= 3; attempt++)
                {
                    loadResult = await _metadataService.LoadMetadataAsync(item.File);
                    if (loadResult.TechnicalPropertiesLoaded || loadResult.WindowsPropertiesLoaded || attempt == 3)
                    {
                        break;
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(150 * attempt));
                }

                if (loadResult is null)
                {
                    return false;
                }

                await ApplyMetadataToItemAsync(item, loadResult.Metadata);

                if (loadResult.TechnicalPropertiesLoaded || loadResult.WindowsPropertiesLoaded)
                {
                    _metadataCache[item.File.Path] = loadResult.Metadata;
                    return true;
                }

                AppLogger.Error(
                    "Windows プロパティを再試行後も読み込めませんでした",
                    new InvalidOperationException(string.Join(Environment.NewLine, loadResult.WindowsPropertyErrors)),
                    item.File.Path);
                return false;
            }
            catch (Exception ex)
            {
                AppLogger.Error("メタデータの適用に失敗しました", ex, item.File.Path);
                return false;
            }
            finally
            {
                _metadataReadSemaphore.Release();
            }
        }

        private async Task ApplyMetadataToItemAsync(VideoFileItem item, VideoMetadataSnapshot meta)
        {
            await DispatcherHelper.RunOnUIThreadAsync(() =>
            {
                item.Title = meta.Title;
                item.Participants = meta.Participants;
                item.CatalogNumber = meta.CatalogNumber;
                item.Category = meta.Category;
                item.Rating = meta.Rating;
                item.ContentDistributor = meta.ContentDistributor;
                item.Publisher = meta.Publisher;
                item.Duration = meta.Duration;
                item.FrameWidth = meta.FrameWidth;
                item.FrameHeight = meta.FrameHeight;
                item.FrameRate = meta.FrameRate;
                item.VideoBitrate = meta.VideoBitrate;
                item.VideoCompression = VideoMetadataService.MapVideoCompression(meta.VideoCompression);
                item.AudioSampleRate = meta.AudioSampleRate;
                item.AudioBitrate = meta.AudioBitrate;
                item.AudioFormat = VideoMetadataService.MapAudioFormat(meta.AudioFormat);
                item.Comment = meta.Comment;
                item.ReleaseDate = meta.ReleaseDate;
                item.ReleaseDateText = DisplayFormatHelper.FormatReleaseDateDisplayText(meta.ReleaseDate);

                item.ResetChangeTracking();
            });
        }

        private async Task<VideoMetadataOperationResult> SaveWindowsPropertiesAsync(VideoFileItem item)
        {
            var changedProperties = new List<string>();
            if (item.HasChanges(nameof(VideoFileItem.Title))) changedProperties.Add("Title");
            if (item.HasChanges(nameof(VideoFileItem.Participants))) changedProperties.Add("Participants");
            if (item.HasChanges(nameof(VideoFileItem.Comment))) changedProperties.Add("Comment");
            if (item.HasChanges(nameof(VideoFileItem.Category))) changedProperties.Add("Category");
            if (item.HasChanges(nameof(VideoFileItem.CatalogNumber))) changedProperties.Add("CatalogNumber");
            if (item.HasChanges(nameof(VideoFileItem.Rating))) changedProperties.Add("Rating");
            if (item.HasChanges(nameof(VideoFileItem.ContentDistributor))) changedProperties.Add("ContentDistributor");
            if (item.HasChanges(nameof(VideoFileItem.Publisher))) changedProperties.Add("Publisher");
            if (item.HasChanges(nameof(VideoFileItem.ReleaseDate))) changedProperties.Add("ReleaseDate");

            if (changedProperties.Count == 0)
            {
                return new VideoMetadataOperationResult { Succeeded = true, Message = "変更なし" };
            }

            await _metadataSaveSemaphore.WaitAsync();
            try
            {
                var metadata = item.CreateCurrentMetadataSnapshot();
                var originalMetadata = item.CreateOriginalMetadataSnapshot();

                return await _metadataService.SaveMetadataAsync(item.File, metadata, originalMetadata, changedProperties);
            }
            catch (Exception ex)
            {
                AppLogger.Error("メタデータの保存に失敗しました", ex, item.File.Path);
                return new VideoMetadataOperationResult
                {
                    Succeeded = false,
                    Message = "保存に失敗しました",
                    Details = ex.Message
                };
            }
            finally
            {
                _metadataSaveSemaphore.Release();
            }
        }



        public async Task ExportVideosToCsvAsync()
        {
            if (Videos == null || Videos.Count == 0)
            {
                return;
            }

            var savePicker = new FileSavePicker();
            savePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            savePicker.FileTypeChoices.Add("CSVファイル", new List<string>() { ".csv" });
            savePicker.SuggestedFileName = "動画一覧.csv";

            if (App.MainWindow is not null)
            {
                var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
                InitializeWithWindow.Initialize(savePicker, hwnd);
            }

            var file = await savePicker.PickSaveFileAsync();
            if (file is null)
            {
                return;
            }

            IsBusy = true;
            HasOperationError = false;
            try
            {
                await Task.Run(async () =>
                {
                    var headers = new[]
                    {
                        "ファイル名", "サイズ", "タイトル", "出演者", "発売日", "品番",
                        "評価", "発行元", "レーベル", "カテゴリ", "コメント", "再生時間",
                        "フレーム幅", "フレーム高さ", "フレームレート", "映像ビットレート",
                        "圧縮方式", "サンプルレート", "音声ビットレート", "音声形式"
                    };

                    var lines = new List<string> { string.Join(",", headers.Select(DisplayFormatHelper.EscapeCsvField)) };

                    foreach (var item in Videos)
                    {
                        var fields = new[]
                        {
                            item.FileName,
                            item.FileSizeText,
                            item.Title,
                            item.Participants,
                            item.ReleaseDateText,
                            item.CatalogNumber,
                            item.Rating,
                            item.Publisher,
                            item.ContentDistributor,
                            item.Category,
                            item.Comment,
                            item.Duration,
                            item.FrameWidth,
                            item.FrameHeight,
                            item.FrameRate,
                            item.VideoBitrate,
                            item.VideoCompression,
                            item.AudioSampleRate,
                            item.AudioBitrate,
                            item.AudioFormat
                        };
                        lines.Add(string.Join(",", fields.Select(DisplayFormatHelper.EscapeCsvField)));
                    }

                    using var stream = await file.OpenStreamForWriteAsync();
                    stream.SetLength(0);
                    using var writer = new System.IO.StreamWriter(stream, new System.Text.UTF8Encoding(true));

                    foreach (var line in lines)
                    {
                        await writer.WriteLineAsync(line);
                    }
                });

                SetStatusSuccess($"CSV を保存しました: {file.Name}");
            }
            catch (Exception ex)
            {
                SetStatusError($"CSV の保存に失敗しました: {ex.Message}", ex, file.Path);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void SelectedVideo_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(VideoFileItem.HasPendingChanges))
            {
                OnPropertyChanged(nameof(HasPendingChanges));
            }
        }

        private void SetStatusError(string message, Exception? ex = null, string? targetPath = null)
        {
            if (ex is not null)
            {
                AppLogger.Error(message, ex, targetPath);
            }

            HasOperationError = true;
            StatusMessage = message;
        }

        private void SetStatusSuccess(string message)
        {
            HasOperationError = false;
            StatusMessage = message;
        }
    }
}
