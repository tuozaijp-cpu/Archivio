using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
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
        private bool _isThumbnailView;
        private int _thumbnailTileSizeIndex = 1;
        private readonly VideoDetailsViewModel _details;

        private readonly VideoListFilterManager _filterManager = new();
        private readonly List<VideoFileItem> _allVideosList = new();
        private readonly ConcurrentDictionary<string, VideoMetadataSnapshot> _metadataCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, ulong> _fileSizeCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, DateTimeOffset> _lastWriteTimeCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim _thumbnailReadSemaphore = new(4, 4);
        private readonly ConcurrentDictionary<string, ThumbnailCacheEntry> _thumbnailCache = new(StringComparer.OrdinalIgnoreCase);

        private sealed class ThumbnailCacheEntry
        {
            public ulong FileSize { get; init; }
            public DateTimeOffset LastWriteTime { get; init; }
            public byte[] ImageData { get; init; } = Array.Empty<byte>();
            public bool LoadFailed { get; init; }
        }

        public MainPageViewModel() : this(new VideoMetadataService(), new VideoFileService())
        {
        }

        internal MainPageViewModel(IVideoMetadataService metadataService, IVideoFileService fileService)
        {
            _metadataService = metadataService;
            _fileService = fileService;
            var settings = SettingsManager.LoadSettings();
            _includeSubfolders = settings.IncludeSubfolders;
            _isThumbnailView = settings.IsThumbnailView;
            _thumbnailTileSizeIndex = Math.Clamp(settings.ThumbnailTileSizeIndex, 0, 3);

            _details = new VideoDetailsViewModel(
                _metadataService,
                _metadataReadSemaphore,
                _metadataSaveSemaphore,
                busy => IsBusy = busy,
                (msg, ex, path) => SetStatusError(msg, ex, path),
                msg => SetStatusSuccess(msg),
                () => FolderPath,
                (path, size, mtime, meta) =>
                {
                    _fileSizeCache[path] = size;
                    _lastWriteTimeCache[path] = mtime;
                    _metadataCache[path] = meta;
                },
                OnCoverArtSaved
            );

            // 起動時に非同期でキャッシュのガベージコレクションを実行
            _ = Task.Run(async () =>
            {
                try
                {
                    await MetadataCacheManager.GarbageCollectCacheAsync();
                }
                catch (Exception ex)
                {
                    AppLogger.Error("起動時のキャッシュGCに失敗しました", ex);
                }
            });
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
                    // フォルダが変わったときだけキャッシュをクリア
                    MediaProbeService.ClearCache();
                    _fileService.ClearEnumerationCache();
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

            // 再読み込み時は外部での変更（ファイルの追加・削除・移動など）を反映するため、ファイルスキャンキャッシュをクリア
            _fileService.ClearEnumerationCache();

            _refreshCts?.Cancel();
            _refreshCts?.Dispose();
            _refreshCts = new CancellationTokenSource();
            var cancellationToken = _refreshCts.Token;
            IsBusy = true;
            HasOperationError = false;
            StatusMessage = LanguageManager.GetString("Msg_SearchingVideos");
            var refreshTimer = Stopwatch.StartNew();
            try
            {
                _metadataCache.Clear();
                _fileSizeCache.Clear();
                _lastWriteTimeCache.Clear();

                // 永続化キャッシュの読み込み
                var cache = await MetadataCacheManager.LoadCacheAsync(FolderPath);
                AppLogger.Info($"動画一覧: キャッシュ読み込み完了 ({refreshTimer.ElapsedMilliseconds} ms)", FolderPath);

                var folder = await StorageFolder.GetFolderFromPathAsync(FolderPath);
                var filePaths = await _fileService.EnumerateVideoFilePathsAsync(folder, IncludeSubfolders, cancellationToken);
                PruneThumbnailCache(filePaths);
                AppLogger.Info($"動画一覧: ファイル列挙完了 ({filePaths.Count} 件, {refreshTimer.ElapsedMilliseconds} ms)", FolderPath);

                var processedItems = new VideoFileItem[filePaths.Count];
                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount * 2),
                    CancellationToken = cancellationToken
                };

                await Parallel.ForAsync(0, filePaths.Count, parallelOptions, async (i, ct) =>
                {
                    var filePath = filePaths[i];
                    try
                    {
                        var item = new VideoFileItem(filePath);

                        // キャッシュヒットの判定
                        if (cache != null && cache.Entries.TryGetValue(filePath, out var cachedEntry))
                        {
                            try
                            {
                                var fileInfo = new System.IO.FileInfo(filePath);
                                if (fileInfo.Exists)
                                {
                                    var currentSize = (ulong)fileInfo.Length;
                                    var currentModified = fileInfo.LastWriteTimeUtc;

                                    // サイズと最終更新日の両方が一致しているか（1秒未満の誤差を許容）
                                    if (cachedEntry.FileSize == currentSize && Math.Abs((cachedEntry.LastWriteTime - currentModified).TotalSeconds) < 1.0)
                                    {
                                        _fileSizeCache[filePath] = cachedEntry.FileSize;
                                        _metadataCache[filePath] = cachedEntry.Metadata;
                                        _lastWriteTimeCache[filePath] = cachedEntry.LastWriteTime;

                                        // キャッシュされたメタデータも、ファイルから再取得した場合と
                                        // 同じ表示用の適用処理を通す。ここで直接設定すると、例えば
                                        // AAC(Advanced Audio Codec) が AAC のように正規化されず、
                                        // ファイル選択時に一覧の表示が変わってしまう。
                                        var metadata = cachedEntry.Metadata;
                                        ApplyMetadataToItem(item, metadata);
                                        item.IsLoaded = true;
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                            AppLogger.Error("キャッシュ整合性検証中にエラーが発生しました", ex, filePath);
                            }
                        }

                        processedItems[i] = item;
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Error("動画の基本情報の読み込みに失敗しました", ex, filePath);
                    }
                });

                var items = new ObservableCollection<VideoFileItem>();
                foreach (var item in processedItems)
                {
                    if (item != null)
                    {
                        items.Add(item);
                    }
                }

                Videos = items;
                _allVideosList.Clear();
                _allVideosList.AddRange(items);
                _filterManager.ClearAllFilters();
                OnPropertyChanged(nameof(FilterText));
                OnPropertyChanged(nameof(CanExportCsv));
                SelectedVideo = null;

                StatusMessage = LanguageManager.GetString("Msg_LoadedVideos", items.Count);
                AppLogger.Info($"動画一覧: 一覧表示準備完了 ({items.Count} 件, {refreshTimer.ElapsedMilliseconds} ms)", FolderPath);
                _ = LoadThumbnailsAsync(items, cancellationToken);
                _ = LoadRemainingDataAsync(items, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                StatusMessage = LanguageManager.GetString("Msg_LoadingCancelled");
            }
            catch (Exception ex)
            {
                AppLogger.Error("動画一覧の読み込みに失敗しました", ex, FolderPath);
                Videos = new ObservableCollection<VideoFileItem>();
                _allVideosList.Clear();
                _filterManager.ClearAllFilters();
                SelectedVideo = null;
                OnPropertyChanged(nameof(CanExportCsv));
                SetStatusError(LanguageManager.GetString("Msg_LoadFailed"), ex);
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

        public async Task ClearCurrentFolderCacheAsync()
        {
            if (string.IsNullOrWhiteSpace(FolderPath))
            {
                return;
            }

            // キャッシュ削除前に、実行中のバックグラウンド処理（上書き保存を伴う）を確実にキャンセル
            CancelRefresh();

            IsBusy = true;
            try
            {
                await MetadataCacheManager.ClearCacheForFolderAsync(FolderPath);
                _metadataCache.Clear();
                _fileSizeCache.Clear();
                _lastWriteTimeCache.Clear();
                _thumbnailCache.Clear();
                MediaProbeService.ClearCache();
                SetStatusSuccess("キャッシュをクリアしました");
                await RefreshFilesAsync();
            }
            catch (Exception ex)
            {
                SetStatusError("キャッシュのクリアに失敗しました", ex, FolderPath);
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task RenameVideoAsync(VideoFileItem item, string newName)
        {
            var file = await item.GetFileAsync();
            if (string.IsNullOrWhiteSpace(newName) || newName == file.Name)
            {
                return;
            }

            var oldExt = System.IO.Path.GetExtension(item.FullPath);
            var newExt = System.IO.Path.GetExtension(newName);
            if (string.IsNullOrWhiteSpace(newExt) || !string.Equals(oldExt, newExt, StringComparison.OrdinalIgnoreCase))
            {
                newName = System.IO.Path.GetFileNameWithoutExtension(newName) + oldExt;
            }

            var oldPath = item.FullPath;

            item.IsSaving = true;
            HasOperationError = false;
            try
            {
                await file.RenameAsync(newName, NameCollisionOption.FailIfExists);
                item.SyncFromStorageFile();

                // キャッシュの更新
                try
                {
                    if (!string.IsNullOrWhiteSpace(FolderPath))
                    {
                        var newPath = file.Path;
                        var fileInfo = new System.IO.FileInfo(newPath);
                        var fileSize = (ulong)fileInfo.Length;
                        var lastWriteTime = fileInfo.LastWriteTimeUtc;
                        var currentMetadata = item.CreateCurrentMetadataSnapshot();

                        // メモリキャッシュのキーも更新
                    if (_metadataCache.TryRemove(oldPath, out var cachedMeta))
                        {
                            _metadataCache[newPath] = cachedMeta;
                        }

                        _thumbnailCache.TryRemove(oldPath, out _);
                        _thumbnailCache.TryRemove(newPath, out _);
                        if (_fileSizeCache.TryRemove(oldPath, out _))
                        {
                            _fileSizeCache[newPath] = fileSize;
                        }

                        await MetadataCacheManager.RenameEntryAsync(FolderPath, oldPath, newPath, lastWriteTime, fileSize, currentMetadata);
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Error("ファイル名変更後のキャッシュ更新に失敗しました", ex, item.FullPath);
                }

                SetStatusSuccess("ファイル名を変更しました");
            }
            catch (Exception ex)
            {
                item.SyncFromStorageFile();
                SetStatusError($"ファイル名の変更に失敗しました: {ex.Message}", ex, item.FullPath);
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

            var file = await item.GetFileAsync();
            var pathToRemove = item.FullPath;

            item.IsSaving = true;
            HasOperationError = false;
            try
            {
                await file.DeleteAsync();
                item.IsDeleted = true;
                if (SelectedVideo == item)
                {
                    SelectedVideo = null;
                }
                _allVideosList.Remove(item);
                Videos.Remove(item);

                // キャッシュの削除
                try
                {
                    _metadataCache.TryRemove(pathToRemove, out _);
                    _fileSizeCache.TryRemove(pathToRemove, out _);
                    _thumbnailCache.TryRemove(pathToRemove, out _);

                    if (!string.IsNullOrWhiteSpace(FolderPath))
                    {
                        await MetadataCacheManager.RemoveEntryAsync(FolderPath, pathToRemove);
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Error("ファイル削除後のキャッシュ更新に失敗しました", ex, pathToRemove);
                }

                SetStatusSuccess("ファイルを削除しました");
            }
            catch (Exception ex)
            {
                SetStatusError($"ファイルの削除に失敗しました: {ex.Message}", ex, item.FullPath);
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
            StatusMessage = LanguageManager.GetString("Msg_ReMuxing");

            try
            {
                var inputPath = item.FullPath;
                var dir = System.IO.Path.GetDirectoryName(inputPath);
                if (string.IsNullOrWhiteSpace(dir))
                {
                    throw new Exception(LanguageManager.GetString("Msg_GetFolderPathFailed"));
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
                        throw new Exception(LanguageManager.GetString("Msg_FFmpegProcessError"));
                    }

                    try
                    {
                        var errorText = await process.StandardError.ReadToEndAsync();
                        await process.WaitForExitAsync();

                        if (process.ExitCode != 0)
                        {
                            throw new Exception(LanguageManager.GetString("Msg_FFmpegExitError", process.ExitCode, errorText));
                        }
                    }
                    catch
                    {
                        try
                        {
                            if (!process.HasExited)
                            {
                                process.Kill(true); // Kill entire process tree of ffmpeg to avoid orphaned processes!
                            }
                        }
                        catch { }
                        throw;
                    }
                });

                // 新しく作成されたファイルを直接ロードして挿入する
                var newFile = await StorageFile.GetFileFromPathAsync(outPath);
                var newItem = new VideoFileItem(newFile);

                // 基本情報のロード
                await LoadBasicInfoAsync(newItem);

                // UIスレッド上で既存のコレクションへ直接挿入（フォルダ再検索を完全にスキップ）
                await DispatcherHelper.RunOnUIThreadAsync(() =>
                {
                    var indexInAll = _allVideosList.IndexOf(item);
                    if (indexInAll >= 0)
                    {
                        _allVideosList.Insert(indexInAll + 1, newItem);
                    }
                    else
                    {
                        _allVideosList.Add(newItem);
                    }

                    var indexInVideos = Videos.IndexOf(item);
                    if (indexInVideos >= 0)
                    {
                        Videos.Insert(indexInVideos + 1, newItem);
                    }
                    else
                    {
                        Videos.Add(newItem);
                    }

                    // 新しく作成された動画を選択状態にする
                    SelectedVideo = newItem;
                });

                // バックグラウンドでメタデータ読み込みとキャッシュ保存を実行
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var loaded = await LoadWindowsPropertiesAsync(newItem);
                        if (loaded)
                        {
                            await DispatcherHelper.RunOnUIThreadAsync(() =>
                            {
                                newItem.IsLoaded = true;
                            });
                            await SaveCurrentFolderCacheAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Error("再MUXファイルのバックグラウンドメタデータ読み込みに失敗しました", ex, outPath);
                    }
                });

                SetStatusSuccess(LanguageManager.GetString("Msg_ReMuxSuccess"));
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 2)
            {
                SetStatusError(LanguageManager.GetString("Msg_FFmpegMissing"), ex, item.FullPath);
            }
            catch (Exception ex)
            {
                SetStatusError(LanguageManager.GetString("Msg_ReMuxFailed", ex.Message), ex, item.FullPath);
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
                MaxDegreeOfParallelism = Math.Min(8, Math.Max(1, items.Count)),
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
                        var loaded = await LoadWindowsPropertiesAsync(item, cancellationToken: ct);

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
                        AppLogger.Error("バックグラウンドでのメタデータ読み込みに失敗しました", ex, item.FullPath);
                    }
                });

                if (Videos == items && !cancellationToken.IsCancellationRequested)
                {
                    await SaveCurrentFolderCacheAsync();
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        public bool IsThumbnailView
        {
            get => _isThumbnailView;
            set
            {
                if (SetProperty(ref _isThumbnailView, value))
                {
                    OnPropertyChanged(nameof(IsDetailView));
                    OnPropertyChanged(nameof(DetailViewVisibility));
                    OnPropertyChanged(nameof(ThumbnailViewVisibility));
                }
            }
        }

        public bool IsDetailView => !IsThumbnailView;

        public int ThumbnailTileSizeIndex
        {
            get => _thumbnailTileSizeIndex;
            set
            {
                var normalizedValue = Math.Clamp(value, 0, 3);
                if (SetProperty(ref _thumbnailTileSizeIndex, normalizedValue))
                {
                    OnPropertyChanged(nameof(ThumbnailTileWidth));
                    OnPropertyChanged(nameof(ThumbnailImageHeight));
                    OnPropertyChanged(nameof(ThumbnailImageRowHeight));
                }
            }
        }

        public double ThumbnailTileWidth => ThumbnailTileSizeIndex switch
        {
            0 => 140,
            2 => 240,
            3 => 300,
            _ => 180
        };

        public double ThumbnailImageHeight => ThumbnailTileSizeIndex switch
        {
            0 => 120,
            2 => 210,
            3 => 260,
            _ => 160
        };

        public GridLength ThumbnailImageRowHeight => new(ThumbnailImageHeight);

        public Visibility DetailViewVisibility => IsDetailView ? Visibility.Visible : Visibility.Collapsed;

        public Visibility ThumbnailViewVisibility => IsThumbnailView ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// 一覧表示後に基本情報とメタデータを補完する。
        /// 外付けドライブでも、詳細情報の取得を一覧表示から切り離す。
        /// </summary>
        private async Task LoadRemainingDataAsync(ObservableCollection<VideoFileItem> items, CancellationToken cancellationToken)
        {
            try
            {
                await LoadBasicInfoInBackgroundAsync(items, cancellationToken);
                await LoadAllPropertiesSequentiallyAsync(items, cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async Task LoadThumbnailsAsync(ObservableCollection<VideoFileItem> items, CancellationToken cancellationToken)
        {
            try
            {
                var tasks = items.Select(item => LoadThumbnailAsync(item, items, cancellationToken));
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void PruneThumbnailCache(IReadOnlyCollection<string> currentPaths)
        {
            var currentPathSet = new HashSet<string>(currentPaths, StringComparer.OrdinalIgnoreCase);
            foreach (var cachedPath in _thumbnailCache.Keys)
            {
                if (!currentPathSet.Contains(cachedPath))
                {
                    _thumbnailCache.TryRemove(cachedPath, out _);
                }
            }
        }

        private void OnCoverArtSaved(string path)
        {
            _thumbnailCache.TryRemove(path, out _);

            var item = Videos.FirstOrDefault(video =>
                string.Equals(video.FullPath, path, StringComparison.OrdinalIgnoreCase));
            if (item is not null)
            {
                _ = LoadThumbnailAsync(item, Videos, CancellationToken.None);
            }
        }

        private static (bool Exists, ulong FileSize, DateTimeOffset LastWriteTime) GetThumbnailFileState(string path)
        {
            var fileInfo = new FileInfo(path);
            if (!fileInfo.Exists)
            {
                return (false, 0, default);
            }

            return ((true, (ulong)fileInfo.Length, new DateTimeOffset(fileInfo.LastWriteTimeUtc)));
        }

        private static bool IsCurrentThumbnailCacheEntry(
            ThumbnailCacheEntry entry,
            (bool Exists, ulong FileSize, DateTimeOffset LastWriteTime) fileState)
        {
            return fileState.Exists
                && entry.FileSize == fileState.FileSize
                && entry.LastWriteTime == fileState.LastWriteTime;
        }

        private async Task ApplyThumbnailCacheEntryAsync(VideoFileItem item, ThumbnailCacheEntry entry)
        {
            if (entry.LoadFailed)
            {
                await DispatcherHelper.RunOnUIThreadAsync(() =>
                {
                    item.ThumbnailImage = null;
                    item.ThumbnailLoadFailed = true;
                    item.IsThumbnailLoading = false;
                });
                return;
            }

            if (entry.ImageData.Length == 0)
            {
                await DispatcherHelper.RunOnUIThreadAsync(() =>
                {
                    item.ThumbnailImage = null;
                    item.ThumbnailLoadFailed = false;
                    item.IsThumbnailLoading = false;
                });
                return;
            }

            await SetThumbnailImageAsync(item, entry.ImageData);
        }

        private async Task SetThumbnailImageAsync(VideoFileItem item, byte[] imageData)
        {
            await DispatcherHelper.RunOnUIThreadAsync(async () =>
            {
                using var stream = new InMemoryRandomAccessStream();
                using var writer = new DataWriter(stream.GetOutputStreamAt(0));
                writer.WriteBytes(imageData);
                await writer.StoreAsync();
                await writer.FlushAsync();
                stream.Seek(0);

                var bitmap = new BitmapImage
                {
                    DecodePixelWidth = 360,
                    DecodePixelHeight = 320
                };
                await bitmap.SetSourceAsync(stream);

                item.ThumbnailImage = bitmap;
                item.ThumbnailLoadFailed = false;
                item.IsThumbnailLoading = false;
            });
        }

        private async Task LoadThumbnailAsync(
            VideoFileItem item,
            ObservableCollection<VideoFileItem> items,
            CancellationToken cancellationToken)
        {
            await _thumbnailReadSemaphore.WaitAsync(cancellationToken);
            try
            {
                await DispatcherHelper.RunOnUIThreadAsync(() =>
                {
                    item.ThumbnailImage = null;
                    item.ThumbnailLoadFailed = false;
                    item.IsThumbnailLoading = true;
                });

                var fileState = await Task.Run(() => GetThumbnailFileState(item.FullPath), cancellationToken);
                if (!fileState.Exists)
                {
                    if (Videos == items)
                    {
                        await DispatcherHelper.RunOnUIThreadAsync(() =>
                        {
                            item.ThumbnailImage = null;
                            item.ThumbnailLoadFailed = false;
                            item.IsThumbnailLoading = false;
                        });
                    }

                    return;
                }

                if (_thumbnailCache.TryGetValue(item.FullPath, out var cachedEntry)
                    && IsCurrentThumbnailCacheEntry(cachedEntry, fileState))
                {
                    if (Videos == items)
                    {
                        await ApplyThumbnailCacheEntryAsync(item, cachedEntry);
                    }

                    return;
                }

                var file = await item.GetFileAsync();
                var coverArt = await _metadataService.LoadThumbnailImageAsync(file, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                if (Videos != items)
                {
                    return;
                }

                if (coverArt is null)
                {
                    var emptyEntry = new ThumbnailCacheEntry
                    {
                        FileSize = fileState.FileSize,
                        LastWriteTime = fileState.LastWriteTime
                    };
                    _thumbnailCache[item.FullPath] = emptyEntry;

                    await DispatcherHelper.RunOnUIThreadAsync(() =>
                    {
                        item.ThumbnailImage = null;
                        item.ThumbnailLoadFailed = false;
                        item.IsThumbnailLoading = false;
                    });
                    return;
                }

                var imageData = coverArt.Data;
                var loadedEntry = new ThumbnailCacheEntry
                {
                    FileSize = fileState.FileSize,
                    LastWriteTime = fileState.LastWriteTime,
                    ImageData = imageData
                };
                _thumbnailCache[item.FullPath] = loadedEntry;

                await ApplyThumbnailCacheEntryAsync(item, loadedEntry);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                AppLogger.Error("一覧用サムネイルの読み込みに失敗しました", ex, item.FullPath);
                if (Videos == items)
                {
                    var fileState = await Task.Run(() => GetThumbnailFileState(item.FullPath));
                    if (fileState.Exists)
                    {
                        _thumbnailCache[item.FullPath] = new ThumbnailCacheEntry
                        {
                            FileSize = fileState.FileSize,
                            LastWriteTime = fileState.LastWriteTime,
                            LoadFailed = true
                        };
                    }

                    await DispatcherHelper.RunOnUIThreadAsync(() =>
                    {
                        item.ThumbnailImage = null;
                        item.ThumbnailLoadFailed = true;
                        item.IsThumbnailLoading = false;
                    });
                }
            }
            finally
            {
                _thumbnailReadSemaphore.Release();
            }
        }

        private async Task LoadBasicInfoInBackgroundAsync(ObservableCollection<VideoFileItem> items, CancellationToken cancellationToken)
        {
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Min(8, Math.Max(1, items.Count)),
                CancellationToken = cancellationToken
            };

            await Parallel.ForEachAsync(items, options, async (item, ct) =>
            {
                if (Videos != items || ct.IsCancellationRequested)
                {
                    return;
                }

                await LoadBasicInfoAsync(item);
            });
        }

        private async Task SaveCurrentFolderCacheAsync()
        {
            if (string.IsNullOrWhiteSpace(FolderPath))
            {
                return;
            }

            try
            {
                var cacheData = new FolderCacheData
                {
                    FolderPath = FolderPath,
                    LastUpdated = DateTime.UtcNow
                };

                foreach (var pair in _metadataCache)
                {
                    var filePath = pair.Key;
                    var metadata = pair.Value;
                    if (_fileSizeCache.TryGetValue(filePath, out var fileSize))
                    {
                        if (_lastWriteTimeCache.TryGetValue(filePath, out var lastWriteTime))
                        {
                            cacheData.Entries[filePath] = new CacheEntry
                            {
                                LastWriteTime = lastWriteTime,
                                FileSize = fileSize,
                                Metadata = metadata
                            };
                        }
                        else
                        {
                            try
                            {
                                var fileInfo = new System.IO.FileInfo(filePath);
                                if (fileInfo.Exists)
                                {
                                    cacheData.Entries[filePath] = new CacheEntry
                                    {
                                        LastWriteTime = fileInfo.LastWriteTimeUtc,
                                        FileSize = fileSize,
                                        Metadata = metadata
                                    };
                                }
                            }
                            catch
                            {
                                // ファイルが移動・削除・ロックなどで情報取得できない場合はキャッシュしない
                            }
                        }
                    }
                }

                await MetadataCacheManager.SaveCacheAsync(FolderPath, cacheData);
            }
            catch (Exception ex)
            {
                AppLogger.Error("現在のフォルダキャッシュの保存に失敗しました", ex, FolderPath);
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

            IsBusy = true;
            try
            {
                cts.Token.ThrowIfCancellationRequested();

                // キャッシュに頼らず直接ファイルから読み込み直すために force: true を指定
                await LoadWindowsPropertiesAsync(currentVideo, force: true);

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
                AppLogger.Error("選択中の動画の読み込みに失敗しました", ex, currentVideo.FullPath);
                if (SelectedVideo == currentVideo)
                {
                    SetStatusError("選択中の動画の読み込みに失敗しました。詳細はログを確認してください。", ex, currentVideo.FullPath);
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
                if (_fileSizeCache.TryGetValue(item.FullPath, out var cachedSize))
                {
                    await DispatcherHelper.RunOnUIThreadAsync(() =>
                    {
                        item.FileSizeBytes = cachedSize;
                        item.FileSizeText = DisplayFormatHelper.FormatFileSize(cachedSize);
                    });
                    return;
                }

                // サイズと更新日時を同じファイルシステム情報から取得し、
                // GetBasicPropertiesAsync と FileInfo の二重アクセスを避ける。
                var fileInfo = new System.IO.FileInfo(item.FullPath);
                if (!fileInfo.Exists)
                {
                    return;
                }

                var size = (ulong)fileInfo.Length;
                var lastWriteTime = fileInfo.LastWriteTimeUtc;
                _fileSizeCache[item.FullPath] = size;
                _lastWriteTimeCache[item.FullPath] = lastWriteTime;
                await DispatcherHelper.RunOnUIThreadAsync(() =>
                {
                    item.FileSizeBytes = size;
                    item.FileSizeText = DisplayFormatHelper.FormatFileSize(size);
                });
            }
            catch (Exception ex)
            {
                AppLogger.Error("基本情報の読み込みに失敗しました", ex, item.FullPath);
                item.FileSizeBytes = 0;
                item.FileSizeText = string.Empty;
            }
        }

        private async Task<bool> LoadWindowsPropertiesAsync(VideoFileItem item, bool force = false, CancellationToken cancellationToken = default)
        {
            await _metadataReadSemaphore.WaitAsync();
            try
            {
                if (!force && item.IsLoaded)
                {
                    return true;
                }

                if (!force && _metadataCache.TryGetValue(item.FullPath, out var cachedMetadata))
                {
                    await ApplyMetadataToItemAsync(item, cachedMetadata);
                    return true;
                }

                VideoMetadataLoadResult? loadResult = null;
                for (var attempt = 1; attempt <= 3; attempt++)
                {
                    var file = await item.GetFileAsync();
                    loadResult = await _metadataService.LoadMetadataAsync(file, cancellationToken);
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
                    _metadataCache[item.FullPath] = loadResult.Metadata;
                    return true;
                }

                AppLogger.Error(
                    "Windows プロパティを再試行後も読み込めませんでした",
                    new InvalidOperationException(string.Join(Environment.NewLine, loadResult.WindowsPropertyErrors)),
                    item.FullPath);
                return false;
            }
            catch (Exception ex)
            {
                AppLogger.Error("メタデータの適用に失敗しました", ex, item.FullPath);
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
                ApplyMetadataToItem(item, meta);
            });
        }

        /// <summary>
        /// メタデータを表示用に変換して VideoFileItem に適用する共通処理。
        /// 一覧の初回読み込み、キャッシュ復元、選択時の再読み込みで同じ結果にする。
        /// </summary>
        private static void ApplyMetadataToItem(VideoFileItem item, VideoMetadataSnapshot meta)
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

                var file = await item.GetFileAsync();
                return await _metadataService.SaveMetadataAsync(file, metadata, originalMetadata, changedProperties);
            }
            catch (Exception ex)
            {
                AppLogger.Error("メタデータの保存に失敗しました", ex, item.FullPath);
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

        private async Task LoadCoverArtAsync(VideoFileItem item, bool force = false)
        {
            await _metadataReadSemaphore.WaitAsync();
            try
            {
                if (!force && item.HasCoverArt)
                {
                    return;
                }

                var file = await item.GetFileAsync();
                var imagesData = (await _metadataService.LoadCoverArtImagesAsync(file)).ToList();

                if (imagesData.Count > 0)
                {
                    await DispatcherHelper.RunOnUIThreadAsync(async () =>
                    {
                        try
                        {
                            var list = new ObservableCollection<BitmapImage>();
                            foreach (var coverArt in imagesData)
                            {
                                using var stream = new InMemoryRandomAccessStream();
                                using var writer = new DataWriter(stream.GetOutputStreamAt(0));
                                writer.WriteBytes(coverArt.Data);
                                await writer.StoreAsync();
                                await writer.FlushAsync();
                                stream.Seek(0);

                                var bitmap = new BitmapImage();
                                await bitmap.SetSourceAsync(stream);
                                list.Add(bitmap);
                            }
                            item.CoverArtImages = imagesData;
                            item.CoverArts = list;
                            item.CoverArt = list[0];
                            item.SelectedCoverArtIndex = 0;
                            item.HasCoverArt = true;
                        }
                        catch
                        {
                            item.CoverArts = new ObservableCollection<BitmapImage>();
                            item.CoverArt = null;
                            item.HasCoverArt = false;
                        }
                    });
                }
                else
                {
                    await DispatcherHelper.RunOnUIThreadAsync(() =>
                    {
                        item.CoverArts = new ObservableCollection<BitmapImage>();
                        item.CoverArt = null;
                        item.HasCoverArt = false;
                    });
                }
            }
            catch
            {
                await DispatcherHelper.RunOnUIThreadAsync(() =>
                {
                    item.CoverArts = new ObservableCollection<BitmapImage>();
                    item.CoverArt = null;
                    item.HasCoverArt = false;
                });
            }
            finally
            {
                _metadataReadSemaphore.Release();
            }
        }

        private async Task<VideoMetadataOperationResult> SaveCoverArtAsync(VideoFileItem item)
        {
            await _metadataSaveSemaphore.WaitAsync();
            try
            {
                var file = await item.GetFileAsync();
                return await _metadataService.SaveCoverArtAsync(file, item.CoverArtImages);
            }
            catch (Exception ex)
            {
                return new VideoMetadataOperationResult
                {
                    Succeeded = false,
                    Message = "カバー画像の保存に失敗しました",
                    Details = ex.Message
                };
            }
            finally
            {
                _metadataSaveSemaphore.Release();
            }
        }

        public async Task AddCoverArtImageAsync()
        {
            if (SelectedVideo is null) return;

            var picker = new FileOpenPicker();
            picker.ViewMode = PickerViewMode.Thumbnail;
            picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".png");

            if (App.MainWindow is not null)
            {
                var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
                InitializeWithWindow.Initialize(picker, hwnd);
            }

            var file = await picker.PickSingleFileAsync();
            if (file is null) return;

            try
            {
                using var stream = await file.OpenReadAsync();
                var bytes = new byte[stream.Size];
                using var reader = new DataReader(stream);
                await reader.LoadAsync((uint)stream.Size);
                reader.ReadBytes(bytes);

                // UIスレッド上でBitmapImageの生成とコレクション追加
                var bitmap = new BitmapImage();
                stream.Seek(0);
                await bitmap.SetSourceAsync(stream);

                SelectedVideo.CoverArtImages.Add(new CoverArtImageData
                {
                    Data = bytes,
                    MimeType = file.ContentType ?? "image/jpeg",
                    Description = file.Name
                });
                SelectedVideo.CoverArts.Add(bitmap);
                SelectedVideo.HasCoverArt = true;
                SelectedVideo.SelectedCoverArtIndex = SelectedVideo.CoverArts.Count - 1;
                SelectedVideo.IsCoverArtDirty = true; // Mark as dirty explicitly!

                SelectedVideo.NotifyCoverArtPropertiesChanged();
                OnPropertyChanged(nameof(HasPendingChanges));
            }
            catch
            {
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
            savePicker.FileTypeChoices.Add(LanguageManager.GetString("FileChoice_Csv"), new List<string>() { ".csv" });
            savePicker.SuggestedFileName = LanguageManager.GetString("Csv_DefaultFileName");

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

                SetStatusSuccess(LanguageManager.GetString("Msg_CsvSaved", file.Name));
            }
            catch (Exception ex)
            {
                SetStatusError(LanguageManager.GetString("Msg_CsvSaveFailed", ex.Message), ex, file.Path);
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
