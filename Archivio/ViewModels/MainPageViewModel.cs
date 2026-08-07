using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.WindowsAPICodePack.Shell;
using Microsoft.WindowsAPICodePack.Shell.PropertySystem;
using TagLib;
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

        private string _filterText = string.Empty;
        private string _sortColumn = string.Empty;
        private bool _isSortAscending = true;
        private readonly List<VideoFileItem> _allVideosList = new();
        private readonly Dictionary<string, HashSet<string>> _columnCheckedValues = new(StringComparer.OrdinalIgnoreCase);
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
        }

        internal static string FormatReleaseDateDisplayText(DateTimeOffset releaseDate)
        {
            return releaseDate.Year > 1900 ? releaseDate.ToString("yyyy/MM/dd") : string.Empty;
        }

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

                    // 選択ファイル変更時にエラー表示をクリア
                    StatusMessage = string.Empty;
                    HasOperationError = false;

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
                    if (string.IsNullOrWhiteSpace(FolderPath))
                    {
                        App.MainWindow.Title = "Archivio";
                    }
                    else
                    {
                        App.MainWindow.Title = $"Archivio - {FolderPath}";
                    }
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

        public bool IsVideoSelected => SelectedVideo is not null;

        public Visibility DetailedPanelVisibility => IsVideoSelected ? Visibility.Visible : Visibility.Collapsed;

        public string FilterText
        {
            get => _filterText;
            set
            {
                if (SetProperty(ref _filterText, value))
                {
                    ApplyFilterAndSort();
                }
            }
        }

        public List<string> GetUniqueValuesForProperty(string propertyName)
        {
            return _allVideosList.Select(v => GetPropertyValueString(v, propertyName))
                                 .Where(s => !string.IsNullOrWhiteSpace(s))
                                 .Distinct(StringComparer.OrdinalIgnoreCase)
                                 .OrderBy(s => s)
                                 .ToList();
        }

        public HashSet<string>? GetColumnFilterValues(string propertyName)
        {
            if (_columnCheckedValues.TryGetValue(propertyName, out var set))
            {
                return set;
            }
            return null;
        }

        public void SetColumnFilterValues(string propertyName, List<string> allowedValues)
        {
            _columnCheckedValues[propertyName] = new HashSet<string>(allowedValues, StringComparer.OrdinalIgnoreCase);
            ApplyFilterAndSort();
        }

        public void ClearColumnFilter(string propertyName)
        {
            _columnCheckedValues.Remove(propertyName);
            ApplyFilterAndSort();
        }

        public void Sort(string column, bool ascending)
        {
            _sortColumn = column;
            _isSortAscending = ascending;
            ApplyFilterAndSort();
        }

        private void ApplyFilterAndSort()
        {
            // 1. Filter
            var filtered = _allVideosList.AsEnumerable();
            
            // Global Filter
            if (!string.IsNullOrWhiteSpace(FilterText))
            {
                var search = FilterText.Trim().ToLowerInvariant();
                filtered = filtered.Where(v =>
                    v.FileName.ToLowerInvariant().Contains(search) ||
                    v.Title.ToLowerInvariant().Contains(search) ||
                    v.Participants.ToLowerInvariant().Contains(search) ||
                    v.CatalogNumber.ToLowerInvariant().Contains(search) ||
                    v.Publisher.ToLowerInvariant().Contains(search) ||
                    v.ContentDistributor.ToLowerInvariant().Contains(search) ||
                    v.Category.ToLowerInvariant().Contains(search) ||
                    v.Comment.ToLowerInvariant().Contains(search)
                );
            }

            // Column-Specific Spreadsheet Filters
            foreach (var colFilter in _columnCheckedValues)
            {
                var propertyName = colFilter.Key;
                var allowedValues = colFilter.Value;
                if (allowedValues != null)
                {
                    filtered = filtered.Where(v =>
                    {
                        var propVal = GetPropertyValueString(v, propertyName);
                        return allowedValues.Contains(propVal);
                    });
                }
            }

            // 2. Sort
            if (!string.IsNullOrWhiteSpace(_sortColumn))
            {
                Func<VideoFileItem, object> keySelector = _sortColumn switch
                {
                    "StatusText" => v => v.StatusText,
                    "FileName" => v => v.FileName,
                    "FileSizeText" => v => v.File != null ? (object)v.FullPath : v.FileName,
                    "Title" => v => v.Title,
                    "Participants" => v => v.Participants,
                    "ReleaseDateText" => v => v.ReleaseDate,
                    "CatalogNumber" => v => v.CatalogNumber,
                    "Rating" => v => v.RatingStarsIndex,
                    "Publisher" => v => v.Publisher,
                    "ContentDistributor" => v => v.ContentDistributor,
                    "Category" => v => v.Category,
                    "Comment" => v => v.Comment,
                    "Duration" => v => v.Duration,
                    _ => v => v.FileName
                };

                filtered = _isSortAscending
                    ? filtered.OrderBy(keySelector)
                    : filtered.OrderByDescending(keySelector);
            }

            var resultList = filtered.ToList();

            // Save currently selected video to restore it if possible
            var previouslySelected = SelectedVideo;

            Videos = new ObservableCollection<VideoFileItem>(resultList);

            // Restore selection if it's still in the list
            if (previouslySelected != null && resultList.Contains(previouslySelected))
            {
                SelectedVideo = previouslySelected;
            }
            else
            {
                SelectedVideo = null;
            }
        }

        private static string GetPropertyValueString(VideoFileItem item, string propertyName)
        {
            return propertyName switch
            {
                "StatusText" => item.StatusText,
                "FileName" => item.FileName,
                "FileSizeText" => item.FileSizeText,
                "Title" => item.Title,
                "Participants" => item.Participants,
                "ReleaseDateText" => item.ReleaseDateText,
                "CatalogNumber" => item.CatalogNumber,
                "Rating" => item.RatingStarsText,
                "Publisher" => item.Publisher,
                "ContentDistributor" => item.ContentDistributor,
                "Category" => item.Category,
                "Comment" => item.Comment,
                "Duration" => item.Duration,
                "FrameWidth" => item.FrameWidth,
                "FrameHeight" => item.FrameHeight,
                "FrameRate" => item.FrameRate,
                "VideoBitrate" => item.VideoBitrate,
                "VideoCompression" => item.VideoCompression,
                "AudioSampleRate" => item.AudioSampleRate,
                "AudioBitrate" => item.AudioBitrate,
                "AudioFormat" => item.AudioFormat,
                _ => string.Empty
            };
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
                // 同一パスの動画が外部ツールで更新・差し替えられている可能性があるため、
                // 明示的な再読み込みでは前回の情報を再利用しない。
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
                _filterText = string.Empty; // Reset filter text directly
                _sortColumn = string.Empty; // Reset sort column
                _columnCheckedValues.Clear(); // Clear all column-specific filters!
                OnPropertyChanged(nameof(FilterText)); // Notify reset
                OnPropertyChanged(nameof(CanExportCsv));
                SelectedVideo = null; // 選択フォルダ変更時（一覧読込時）は、詳細表示を確実にクリアする

                // すべてのファイルのプロパティをバックグラウンドで順次ロード
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
                _columnCheckedValues.Clear();
                SelectedVideo = null;
                OnPropertyChanged(nameof(CanExportCsv));
                HasOperationError = true;
                StatusMessage = "動画一覧の読み込みに失敗しました。詳細はログを確認してください。";
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

                        await RunOnUIThreadAsync(() =>
                        {
                            item.IsLoaded = true;
                        });
                    }
                    catch
                    {
                    }
                });
            }
            catch (OperationCanceledException)
            {
            }
        }

        public async Task SaveSelectedVideoMetadataAsync()
        {
            if (SelectedVideo is null)
            {
                return;
            }

            var targetVideo = SelectedVideo;
            targetVideo.IsSaving = true;
            targetVideo.SaveFailed = false; // 保存開始時にエラー状態をリセット
            IsBusy = true;
            HasOperationError = false;
            StatusMessage = string.Empty;
            try
            {
                var result = await SaveWindowsPropertiesAsync(targetVideo);

                // 保存が部分成功・失敗だった場合でも、実ファイルの現在値を必ず読み直す。
                _metadataCache.TryRemove(targetVideo.File.Path, out _);
                await LoadWindowsPropertiesAsync(targetVideo, force: true);

                await RunOnUIThreadAsync(() =>
                {
                    targetVideo.SaveFailed = !result.Succeeded;
                    HasOperationError = !result.Succeeded;
                    StatusMessage = FormatOperationMessage(result);
                    OnPropertyChanged(nameof(HasPendingChanges));
                });
            }
            catch (Exception ex)
            {
                await RunOnUIThreadAsync(() =>
                {
                    targetVideo.SaveFailed = true;
                    HasOperationError = true;
                    StatusMessage = $"保存エラー: {ex.Message}";
                });
            }
            finally
            {
                targetVideo.IsSaving = false;
                IsBusy = false;
            }
        }

        public async Task SaveSelectedVideoCoverArtAsync()
        {
            if (SelectedVideo is null)
            {
                return;
            }

            var targetVideo = SelectedVideo;
            targetVideo.IsSaving = true;
            targetVideo.SaveFailed = false; // 保存開始時にエラー状態をリセット
            IsBusy = true;
            HasOperationError = false;
            StatusMessage = string.Empty;
            try
            {
                var result = await SaveCoverArtAsync(targetVideo);

                await LoadCoverArtAsync(targetVideo, force: true);

                await RunOnUIThreadAsync(() =>
                {
                    targetVideo.SaveFailed = !result.Succeeded;
                    HasOperationError = !result.Succeeded;
                    StatusMessage = FormatOperationMessage(result);
                    OnPropertyChanged(nameof(HasPendingChanges));
                });
            }
            catch (Exception ex)
            {
                await RunOnUIThreadAsync(() =>
                {
                    targetVideo.SaveFailed = true;
                    HasOperationError = true;
                    StatusMessage = $"画像保存エラー: {ex.Message}";
                });
            }
            finally
            {
                targetVideo.IsSaving = false;
                IsBusy = false;
            }
        }

        private async Task LoadSelectedVideoAsync()
        {
            _loadingCts?.Cancel();
            _loadingCts = new CancellationTokenSource();
            var cts = _loadingCts;

            if (SelectedVideo is null)
            {
                return;
            }

            var currentVideo = SelectedVideo;

            // すでにメタデータ読込済みでも、カバーアートが未読込なら再ロードする
            if (currentVideo.IsLoaded && (currentVideo.CoverArtImages.Count > 0 || currentVideo.HasCoverArt))
            {
                OnPropertyChanged(nameof(HasPendingChanges));
                return;
            }

            IsBusy = true;
            try
            {
                cts.Token.ThrowIfCancellationRequested();

                // バックグラウンドでメタデータとカバーアートを安全にロード
                await LoadWindowsPropertiesAsync(currentVideo);

                cts.Token.ThrowIfCancellationRequested();

                await LoadCoverArtAsync(currentVideo);

                if (!cts.IsCancellationRequested && SelectedVideo == currentVideo)
                {
                    await RunOnUIThreadAsync(() =>
                    {
                        currentVideo.IsLoaded = true;
                        OnPropertyChanged(nameof(HasPendingChanges));
                    });
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
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
                    item.FileSizeText = FormatFileSize(cachedSize);
                    return;
                }

                var size = await _fileService.GetFileSizeAsync(item.File);
                _fileSizeCache[item.File.Path] = size;
                item.FileSizeText = FormatFileSize(size);
            }
            catch (Exception ex)
            {
                AppLogger.Error("基本情報の読み込みに失敗しました", ex, item.File.Path);
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
                    if (loadResult.WindowsPropertiesLoaded || attempt == 3)
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

                if (loadResult.WindowsPropertiesLoaded)
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
            await RunOnUIThreadAsync(() =>
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
                item.ReleaseDateText = FormatReleaseDateDisplayText(meta.ReleaseDate);

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

                var imagesData = (await _metadataService.LoadCoverArtImagesAsync(item.File)).ToList();

                if (imagesData.Count > 0)
                {
                    await RunOnUIThreadAsync(async () =>
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
                    await RunOnUIThreadAsync(() =>
                    {
                        item.CoverArts = new ObservableCollection<BitmapImage>();
                        item.CoverArt = null;
                        item.HasCoverArt = false;
                    });
                }
            }
            catch
            {
                await RunOnUIThreadAsync(() =>
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
                return await _metadataService.SaveCoverArtAsync(item.File, item.CoverArtImages);
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

        public async Task ReplaceCoverArtImageAsync()
        {
            if (SelectedVideo is null) return;
            int index = SelectedVideo.SelectedCoverArtIndex;
            if (index < 0 || index >= SelectedVideo.CoverArts.Count) return;

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

                // UIスレッド上でBitmapImageの生成と置換
                var bitmap = new BitmapImage();
                stream.Seek(0);
                await bitmap.SetSourceAsync(stream);

                SelectedVideo.CoverArtImages[index] = new CoverArtImageData
                {
                    Data = bytes,
                    MimeType = file.ContentType ?? "image/jpeg",
                    Description = file.Name
                };
                SelectedVideo.CoverArts[index] = bitmap;
                SelectedVideo.IsCoverArtDirty = true; // Mark as dirty explicitly!

                SelectedVideo.NotifyCoverArtPropertiesChanged();
                OnPropertyChanged(nameof(HasPendingChanges));
            }
            catch
            {
            }
        }

        public void DeleteCoverArtImage()
        {
            if (SelectedVideo is null) return;
            int index = SelectedVideo.SelectedCoverArtIndex;
            if (index < 0 || index >= SelectedVideo.CoverArts.Count) return;

            SelectedVideo.CoverArtImages.RemoveAt(index);
            SelectedVideo.CoverArts.RemoveAt(index);
            SelectedVideo.IsCoverArtDirty = true; // Mark as dirty explicitly!

            if (SelectedVideo.CoverArts.Count == 0)
            {
                SelectedVideo.HasCoverArt = false;
                SelectedVideo.SelectedCoverArtIndex = -1;
            }
            else
            {
                SelectedVideo.SelectedCoverArtIndex = Math.Min(index, SelectedVideo.CoverArts.Count - 1);
            }

            SelectedVideo.NotifyCoverArtPropertiesChanged();
            OnPropertyChanged(nameof(HasPendingChanges));
        }

        public async Task ExportVideosToCsvAsync()
        {
            if (Videos == null || Videos.Count == 0) return;

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
            if (file is null) return;

            IsBusy = true;
            try
            {
                await Task.Run(async () =>
                {
                    // CSVのヘッダー行を定義
                    var headers = new[]
                    {
                        "ファイル名", "サイズ", "タイトル", "出演者", "発売日", "品番", 
                        "評価", "発行元", "レーベル", "カテゴリ", "コメント", "再生時間", 
                        "フレーム幅", "フレーム高さ", "フレームレート", "映像ビットレート", 
                        "圧縮方式", "サンプルレート", "音声ビットレート", "音声形式"
                    };

                    var lines = new List<string>();

                    // 

                    // ヘッダー行をカンマで結合してエスケープ処理
                    lines.Add(string.Join(",", headers.Select(EscapeCsvField)));

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
                        lines.Add(string.Join(",", fields.Select(EscapeCsvField)));
                    }

                    // ファイルへの書き込み (UTF-8 BOM付きでExcel文字化けを完全防止)
                    using var stream = await file.OpenStreamForWriteAsync();
                    stream.SetLength(0); // 既存内容をクリア
                    using var writer = new System.IO.StreamWriter(stream, new System.Text.UTF8Encoding(true));

                    foreach (var line in lines)
                    {
                        await writer.WriteLineAsync(line);
                    }
                });
            }
            catch
            {
            }
            finally
            {
                IsBusy = false;
            }
        }

        private static string FormatOperationMessage(VideoMetadataOperationResult result)
        {
            return string.IsNullOrWhiteSpace(result.Details)
                ? result.Message
                : $"{result.Message}{Environment.NewLine}{result.Details}";
        }

        private static string EscapeCsvField(string field)
        {
            if (field == null) return string.Empty;

            // カンマ、ダブルクォーテーション、改行が含まれる場合はダブルクォーテーションで囲む
            // ダブルクォーテーション自体は二重にする
            if (field.Contains(",") || field.Contains("\"") || field.Contains("\r") || field.Contains("\n"))
            {
                return $"\"{field.Replace("\"", "\"\"")}\"";
            }
            return field;
        }

        private void SelectedVideo_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(VideoFileItem.HasPendingChanges))
            {
                OnPropertyChanged(nameof(HasPendingChanges));
            }
        }

        private static string DecodeVideoSubtypeGuid(string guidStr)
        {
            if (string.IsNullOrWhiteSpace(guidStr)) return string.Empty;

            var trimmed = guidStr.Trim().ToLowerInvariant();

            // GUIDのフォーマット {XXXXXXXX-0000-0010-8000-00aa00389b71} に一致するかチェック
            if (trimmed.Length >= 36 && trimmed.Contains("-0000-0010-8000-00aa00389b71"))
            {
                // 先頭の8桁を取り出す
                var cleanGuid = trimmed.Replace("{", "").Replace("}", "");
                var hexPart = cleanGuid.Split('-')[0];
                if (hexPart.Length == 8)
                {
                    try
                    {
                        // リトルエンディアンのDWORD値としてデコード
                        byte[] bytes = new byte[4];
                        for (int i = 0; i < 4; i++)
                        {
                            bytes[i] = Convert.ToByte(hexPart.Substring(i * 2, 2), 16);
                        }

                        char[] chars = new char[4];
                        chars[0] = (char)bytes[3];
                        chars[1] = (char)bytes[2];
                        chars[2] = (char)bytes[1];
                        chars[3] = (char)bytes[0];

                        var fourcc = new string(chars).Trim();
                        // 印刷可能文字のみで構成されているかチェック
                        if (fourcc.All(c => char.IsLetterOrDigit(c) || char.IsPunctuation(c)))
                        {
                            return fourcc;
                        }
                    }
                    catch
                    {
                    }
                }
            }

            return guidStr;
        }

        private static string MapVideoCompression(string rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue)) return string.Empty;

            var decoded = DecodeVideoSubtypeGuid(rawValue);
            var trimmed = decoded.Trim().ToLowerInvariant();
            return trimmed switch
            {
                "avc1" or "h264" or "h.264" => "H.264 (AVC)",
                "hevc" or "hvc1" or "h265" or "h.265" => "H.265 (HEVC)",
                "mp4v" or "mpeg4" or "mpeg-4" => "MPEG-4 Video",
                "av01" or "av1" => "AV1",
                "vp9" or "vp09" => "VP9",
                "vp8" or "vp08" => "VP8",
                _ => decoded
            };
        }

        private static string MapAudioFormat(string rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue)) return string.Empty;

            var trimmed = rawValue.Trim().ToLowerInvariant();

            if (trimmed.Contains("1610") || trimmed.Contains("mp4a") || trimmed.Contains("aac"))
                return "AAC";
            if (trimmed.Contains("0055") || trimmed.Contains("mp3"))
                return "MP3";
            if (trimmed.Contains("0001") || trimmed.Contains("pcm") || trimmed.Contains("wav"))
                return "PCM (WAV)";
            if (trimmed.Contains("2000") || trimmed.Contains("ac3") || trimmed.Contains("ac-3"))
                return "AC-3 (Dolby Digital)";
            if (trimmed.Contains("opus"))
                return "Opus";
            if (trimmed.Contains("flac"))
                return "FLAC";

            return rawValue;
        }

        private static string FormatDuration(long ticks)
        {
            if (ticks <= 0)
            {
                return string.Empty;
            }

            var timeSpan = TimeSpan.FromTicks(ticks);
            return timeSpan.TotalHours >= 1
                ? timeSpan.ToString("h\\:mm\\:ss")
                : timeSpan.ToString("m\\:ss");
        }

        private static string FormatDuration(ulong ticks)
        {
            return FormatDuration((long)ticks);
        }

        private static string FormatDuration(int ticks)
        {
            return FormatDuration((long)ticks);
        }

        private static string FormatDuration(TimeSpan duration)
        {
            return duration.TotalHours >= 1
                ? duration.ToString("h\\:mm\\:ss")
                : duration.ToString("m\\:ss");
        }

        private static string FormatFileSize(ulong bytes)
        {
            const double kb = 1024d;
            const double mb = kb * 1024d;
            const double gb = mb * 1024d;

            if (bytes >= gb)
            {
                return $"{bytes / gb:0.0} GB";
            }
            if (bytes >= mb)
            {
                return $"{bytes / mb:0.0} MB";
            }
            if (bytes >= kb)
            {
                return $"{bytes / kb:0.0} KB";
            }

            return $"{bytes} B";
        }

        private static async Task RunOnUIThreadAsync(Action action)
        {
            if (App.MainWindow is null)
            {
                action();
                return;
            }

            var dispatcher = App.MainWindow.DispatcherQueue;
            if (dispatcher.HasThreadAccess)
            {
                action();
                return;
            }

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool enqueued = dispatcher.TryEnqueue(() =>
            {
                try
                {
                    action();
                    tcs.SetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            if (!enqueued)
            {
                throw new InvalidOperationException("Failed to enqueue work on the UI thread.");
            }

            await tcs.Task;
        }

        private static async Task RunOnUIThreadAsync(Func<Task> action)
        {
            if (App.MainWindow is null)
            {
                await action();
                return;
            }

            var dispatcher = App.MainWindow.DispatcherQueue;
            if (dispatcher.HasThreadAccess)
            {
                await action();
                return;
            }

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool enqueued = dispatcher.TryEnqueue(async () =>
            {
                try
                {
                    await action();
                    tcs.SetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            if (!enqueued)
            {
                throw new InvalidOperationException("Failed to enqueue work on the UI thread.");
            }

            await tcs.Task;
        }
    }

    public sealed class VideoFileItem : ViewModelBase
    {
        private string _title = string.Empty;
        private string _fileSizeText = string.Empty;
        private string _participants = string.Empty;
        private string _director = string.Empty;
        private string _category = string.Empty;
        private string _comment = string.Empty;
        private string _catalogNumber = string.Empty;
        private string _rating = string.Empty;
        private string _contentDistributor = string.Empty;
        private string _publisher = string.Empty;
        private string _duration = string.Empty;
        private string _frameWidth = string.Empty;
        private string _frameHeight = string.Empty;
        private string _frameRate = string.Empty;
        private string _videoBitrate = string.Empty;
        private string _videoCompression = string.Empty;
        private string _audioSampleRate = string.Empty;
        private string _audioBitrate = string.Empty;
        private string _audioFormat = string.Empty;
        private string _releaseDateText = string.Empty;
        private DateTimeOffset _releaseDate = new DateTimeOffset(1900, 1, 1, 0, 0, 0, TimeSpan.Zero);
        private BitmapImage? _coverArt;
        private ObservableCollection<BitmapImage> _coverArts = new();
        private bool _hasCoverArt;
        private bool _isApplyingInitialValues;
        private bool _isLoaded;
        private bool _isSaving;
        private bool _saveFailed;
        private bool _isCoverArtDirty;
        private int _selectedCoverArtIndex = 0;
        private int _originalPictureBytesCount;
        private int _originalPictureBytesHash;
        private List<CoverArtImageData> _coverArtImages = new();
        private Dictionary<string, string> _originalValues = new(StringComparer.OrdinalIgnoreCase);
        private string _fileName = string.Empty;
        private string _fullPath = string.Empty;

        public VideoFileItem(StorageFile file)
        {
            File = file;
            _fileName = file.Name;
            _fullPath = file.Path;
        }

        public StorageFile File { get; }

        public string FileName
        {
            get => _fileName;
            set
            {
                if (SetProperty(ref _fileName, value))
                {
                    _ = RenameFileAsync(value);
                }
            }
        }

        public string FullPath
        {
            get => _fullPath;
            private set => SetProperty(ref _fullPath, value);
        }

        private async Task RenameFileAsync(string newName)
        {
            if (string.IsNullOrWhiteSpace(newName) || newName == File.Name)
            {
                return;
            }

            // 拡張子が変更・削除された場合は元の拡張子を維持する
            var oldExt = System.IO.Path.GetExtension(File.Path);
            var newExt = System.IO.Path.GetExtension(newName);
            if (string.IsNullOrWhiteSpace(newExt) || !string.Equals(oldExt, newExt, StringComparison.OrdinalIgnoreCase))
            {
                newName = System.IO.Path.GetFileNameWithoutExtension(newName) + oldExt;
            }

            IsSaving = true;
            try
            {
                await File.RenameAsync(newName, NameCollisionOption.FailIfExists);
                _fileName = File.Name;
                FullPath = File.Path;
                OnPropertyChanged(nameof(FileName));
            }
            catch
            {
                // 失敗した場合は元のファイル名に差し戻す
                _fileName = File.Name;
                OnPropertyChanged(nameof(FileName));
            }
            finally
            {
                IsSaving = false;
            }
        }

        public string FileSizeText
        {
            get => _fileSizeText;
            set => SetProperty(ref _fileSizeText, value);
        }

        public VideoMetadataSnapshot CreateCurrentMetadataSnapshot()
        {
            return new VideoMetadataSnapshot
            {
                Title = _title,
                Participants = _participants,
                CatalogNumber = _catalogNumber,
                Category = _category,
                Rating = _rating,
                ContentDistributor = _contentDistributor,
                Publisher = _publisher,
                Comment = _comment,
                ReleaseDateText = _releaseDateText,
                ReleaseDate = _releaseDate
            };
        }

        public VideoMetadataSnapshot CreateOriginalMetadataSnapshot()
        {
            return new VideoMetadataSnapshot
            {
                Title = _originalValues.TryGetValue(nameof(Title), out var title) ? title : _title,
                Participants = _originalValues.TryGetValue(nameof(Participants), out var participants) ? participants : _participants,
                CatalogNumber = _originalValues.TryGetValue(nameof(CatalogNumber), out var catalogNumber) ? catalogNumber : _catalogNumber,
                Category = _originalValues.TryGetValue(nameof(Category), out var category) ? category : _category,
                Rating = _originalValues.TryGetValue(nameof(Rating), out var rating) ? rating : _rating,
                ContentDistributor = _originalValues.TryGetValue(nameof(ContentDistributor), out var contentDistributor) ? contentDistributor : _contentDistributor,
                Publisher = _originalValues.TryGetValue(nameof(Publisher), out var publisher) ? publisher : _publisher,
                Comment = _originalValues.TryGetValue(nameof(Comment), out var comment) ? comment : _comment,
                ReleaseDateText = _originalValues.TryGetValue(nameof(ReleaseDate), out var releaseDateText) ? releaseDateText : _releaseDateText,
                ReleaseDate = DateTimeOffset.TryParse(_originalValues.TryGetValue(nameof(ReleaseDate), out var releaseDateValue) ? releaseDateValue : _releaseDateText, out var parsedDate)
                    ? parsedDate
                    : _releaseDate
            };
        }

        public bool IsLoaded
        {
            get => _isLoaded;
            set => SetProperty(ref _isLoaded, value);
        }

        public string Title
        {
            get => _title;
            set
            {
                if (SetProperty(ref _title, value))
                {
                    UpdateChangeState(nameof(Title), value);
                }
            }
        }

        public string Participants
        {
            get => _participants;
            set
            {
                if (SetProperty(ref _participants, value))
                {
                    UpdateChangeState(nameof(Participants), value);
                }
            }
        }

        public string Artist
        {
            get => Participants;
            set
            {
                if (Participants != value)
                {
                    Participants = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Director
        {
            get => _director;
            set => SetProperty(ref _director, value);
        }

        public string Category
        {
            get => _category;
            set
            {
                if (SetProperty(ref _category, value))
                {
                    UpdateChangeState(nameof(Category), value);
                }
            }
        }

        public string Genre
        {
            get => Category;
            set
            {
                if (Category != value)
                {
                    Category = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Comment
        {
            get => _comment;
            set
            {
                if (SetProperty(ref _comment, value))
                {
                    UpdateChangeState(nameof(Comment), value);
                }
            }
        }

        public string CatalogNumber
        {
            get => _catalogNumber;
            set
            {
                if (SetProperty(ref _catalogNumber, value))
                {
                    UpdateChangeState(nameof(CatalogNumber), value);
                }
            }
        }

        public string Rating
        {
            get => _rating;
            set
            {
                if (SetProperty(ref _rating, value))
                {
                    UpdateChangeState(nameof(Rating), value);
                    OnPropertyChanged(nameof(RatingStarsIndex));
                    OnPropertyChanged(nameof(RatingStarsText));
                }
            }
        }

        public int RatingStarsIndex
        {
            get
            {
                if (uint.TryParse(_rating, out var r))
                {
                    // 1星〜5星のダイレクト値（Windowsの表示フォーマット文字列に由来）
                    if (r >= 1 && r <= 5) return (int)r;

                    // Windowsシェルの生 uint しきい値
                    if (r >= 80) return 5;
                    if (r >= 60) return 4;
                    if (r >= 40) return 3;
                    if (r >= 15) return 2;
                    if (r >= 1) return 1;
                }
                else if (!string.IsNullOrWhiteSpace(_rating))
                {
                    var filledStarsCount = _rating.Count(c => c == '★');
                    if (filledStarsCount > 0) return filledStarsCount;

                    var digits = new string(_rating.Where(char.IsDigit).ToArray());
                    if (uint.TryParse(digits, out var d))
                    {
                        if (d >= 1 && d <= 5) return (int)d;

                        if (d >= 80) return 5;
                        if (d >= 60) return 4;
                        if (d >= 40) return 3;
                        if (d >= 15) return 2;
                        if (d >= 1) return 1;
                    }
                }
                return 0;
            }
            set
            {
                uint ratingNum = value switch
                {
                    1 => 1,
                    2 => 25,
                    3 => 50,
                    4 => 75,
                    5 => 99,
                    _ => 0
                };
                Rating = ratingNum.ToString();
                OnPropertyChanged(nameof(RatingStarsIndex));
                OnPropertyChanged(nameof(RatingStarsText));
            }
        }

        public string RatingStarsText
        {
            get
            {
                int index = RatingStarsIndex;
                return index switch
                {
                    1 => "★☆☆☆☆",
                    2 => "★★☆☆☆",
                    3 => "★★★☆☆",
                    4 => "★★★★☆",
                    5 => "★★★★★",
                    _ => "☆☆☆☆☆"
                };
            }
        }

        public string ContentDistributor
        {
            get => _contentDistributor;
            set
            {
                if (SetProperty(ref _contentDistributor, value))
                {
                    UpdateChangeState(nameof(ContentDistributor), value);
                }
            }
        }

        public string Publisher
        {
            get => _publisher;
            set
            {
                if (SetProperty(ref _publisher, value))
                {
                    UpdateChangeState(nameof(Publisher), value);
                }
            }
        }

        public string Duration
        {
            get => _duration;
            set => SetProperty(ref _duration, value);
        }

        public string FrameWidth
        {
            get => _frameWidth;
            set => SetProperty(ref _frameWidth, value);
        }

        public string FrameHeight
        {
            get => _frameHeight;
            set => SetProperty(ref _frameHeight, value);
        }

        public string FrameRate
        {
            get => _frameRate;
            set => SetProperty(ref _frameRate, value);
        }

        public string VideoBitrate
        {
            get => _videoBitrate;
            set => SetProperty(ref _videoBitrate, value);
        }

        public string VideoCompression
        {
            get => _videoCompression;
            set => SetProperty(ref _videoCompression, value);
        }

        public string AudioSampleRate
        {
            get => _audioSampleRate;
            set => SetProperty(ref _audioSampleRate, value);
        }

        public string AudioBitrate
        {
            get => _audioBitrate;
            set => SetProperty(ref _audioBitrate, value);
        }

        public string AudioFormat
        {
            get => _audioFormat;
            set => SetProperty(ref _audioFormat, value);
        }

        public string ReleaseDateText
        {
            get => _releaseDateText;
            set => SetProperty(ref _releaseDateText, value);
        }

        public DateTimeOffset ReleaseDate
        {
            get => _releaseDate;
            set
            {
                var normalizedDate = value.Date;
                if (SetProperty(ref _releaseDate, normalizedDate))
                {
                    _releaseDateText = MainPageViewModel.FormatReleaseDateDisplayText(normalizedDate);
                    OnPropertyChanged(nameof(ReleaseDateText));
                    UpdateChangeState(nameof(ReleaseDate), normalizedDate.ToString("yyyy-MM-dd"));
                }
            }
        }

        public BitmapImage? CoverArt
        {
            get => _coverArt;
            set => SetProperty(ref _coverArt, value);
        }

        public ObservableCollection<BitmapImage> CoverArts
        {
            get => _coverArts;
            set => SetProperty(ref _coverArts, value);
        }

        public bool HasCoverArt
        {
            get => _hasCoverArt;
            set
            {
                if (SetProperty(ref _hasCoverArt, value))
                {
                    NotifyCoverArtPropertiesChanged();
                }
            }
        }

        public int SelectedCoverArtIndex
        {
            get => _selectedCoverArtIndex;
            set
            {
                if (SetProperty(ref _selectedCoverArtIndex, value))
                {
                    OnPropertyChanged(nameof(IsImageControlsEnabled));
                }
            }
        }

        public List<CoverArtImageData> CoverArtImages
        {
            get => _coverArtImages;
            set => _coverArtImages = value;
        }

        public bool IsSaving
        {
            get => _isSaving;
            set
            {
                if (SetProperty(ref _isSaving, value))
                {
                    if (value)
                    {
                        SaveFailed = false; // 保存開始時に失敗状態をクリア
                    }
                    OnPropertyChanged(nameof(StatusText));
                }
            }
        }

        public bool SaveFailed
        {
            get => _saveFailed;
            set
            {
                if (SetProperty(ref _saveFailed, value))
                {
                    OnPropertyChanged(nameof(StatusText));
                }
            }
        }

        public bool IsCoverArtDirty
        {
            get => _isCoverArtDirty;
            set
            {
                if (SetProperty(ref _isCoverArtDirty, value))
                {
                    OnPropertyChanged(nameof(HasCoverArtChangesNotify));
                }
            }
        }

        public string StatusText
        {
            get
            {
                if (IsSaving) return "保存中...";
                if (SaveFailed) return "失敗";
                return string.Empty;
            }
        }

        public bool IsImageControlsEnabled => HasCoverArt && SelectedCoverArtIndex >= 0 && SelectedCoverArtIndex < CoverArts.Count;

        public Visibility PlaceholderVisibility => HasCoverArt ? Visibility.Collapsed : Visibility.Visible;
        public Visibility CoverArtVisibility => HasCoverArt ? Visibility.Visible : Visibility.Collapsed;

        public void NotifyCoverArtPropertiesChanged()
        {
            OnPropertyChanged(nameof(PlaceholderVisibility));
            OnPropertyChanged(nameof(CoverArtVisibility));
            OnPropertyChanged(nameof(IsImageControlsEnabled));
            OnPropertyChanged(nameof(CoverArts));
            OnPropertyChanged(nameof(HasCoverArt));
            OnPropertyChanged(nameof(HasCoverArtChangesNotify));

            // Gridに表示する代表画像を設定
            CoverArt = CoverArts.Count > 0 ? CoverArts[0] : null;
        }

        private static int CalculatePictureBytesHash(IReadOnlyList<CoverArtImageData> list)
        {
            if (list == null) return 0;
            int hash = 17;
            foreach (var coverArt in list)
            {
                hash = hash * 23 + (coverArt?.Data?.Length ?? 0);
                hash = hash * 23 + (coverArt?.MimeType?.GetHashCode(StringComparison.Ordinal) ?? 0);
                hash = hash * 23 + (coverArt?.Description?.GetHashCode(StringComparison.Ordinal) ?? 0);
            }
            return hash;
        }

        public bool HasCoverArtChanges()
        {
            return IsCoverArtDirty 
                || _coverArtImages.Count != _originalPictureBytesCount 
                || CalculatePictureBytesHash(_coverArtImages) != _originalPictureBytesHash;
        }

        public bool HasCoverArtChangesNotify => HasCoverArtChanges();
        public bool HasMetadataChangesNotify => HasMetadataChanges();

        public bool HasMetadataChanges()
        {
            return HasChanges(nameof(Title))
                || HasChanges(nameof(Participants))
                || HasChanges(nameof(Category))
                || HasChanges(nameof(Comment))
                || HasChanges(nameof(CatalogNumber))
                || HasChanges(nameof(Rating))
                || HasChanges(nameof(ContentDistributor))
                || HasChanges(nameof(Publisher))
                || HasChanges(nameof(ReleaseDate));
        }

        public bool HasPendingChanges => HasAnyPendingChanges();

        public void ResetMetadataChangeTracking()
        {
            _isApplyingInitialValues = true;
            try
            {
                _originalValues[nameof(Title)] = _title;
                _originalValues[nameof(Participants)] = _participants;
                _originalValues[nameof(Category)] = _category;
                _originalValues[nameof(Comment)] = _comment;
                _originalValues[nameof(CatalogNumber)] = _catalogNumber;
                _originalValues[nameof(Rating)] = _rating;
                _originalValues[nameof(ContentDistributor)] = _contentDistributor;
                _originalValues[nameof(Publisher)] = _publisher;
                _originalValues[nameof(ReleaseDate)] = _releaseDate.ToString("yyyy-MM-dd");

                HasPendingChangesNotify();
            }
            finally
            {
                _isApplyingInitialValues = false;
            }
        }

        public void ResetCoverArtChangeTracking()
        {
            _originalPictureBytesCount = _coverArtImages.Count;
            _originalPictureBytesHash = CalculatePictureBytesHash(_coverArtImages);
            IsCoverArtDirty = false;
            HasPendingChangesNotify();
        }

        public void ResetChangeTracking()
        {
            ResetMetadataChangeTracking();
            ResetCoverArtChangeTracking();
        }

        private void UpdateChangeState(string propertyName, string value)
        {
            if (_isApplyingInitialValues)
            {
                return;
            }

            SaveFailed = false; // プロパティを編集した時点で失敗ステータス「失敗」をクリア
            HasPendingChangesNotify();
        }

        private void HasPendingChangesNotify()
        {
            OnPropertyChanged(nameof(HasPendingChanges));
            OnPropertyChanged(nameof(HasMetadataChangesNotify));
            OnPropertyChanged(nameof(HasCoverArtChangesNotify));
        }

        public bool HasChanges(string propertyName)
        {
            if (propertyName == "CoverArts")
            {
                return HasCoverArtChanges();
            }

            var currentValue = propertyName switch
            {
                nameof(Title) => _title,
                nameof(Participants) => _participants,
                nameof(Category) => _category,
                nameof(Comment) => _comment,
                nameof(CatalogNumber) => _catalogNumber,
                nameof(Rating) => _rating,
                nameof(ContentDistributor) => _contentDistributor,
                nameof(Publisher) => _publisher,
                nameof(ReleaseDate) => _releaseDate.ToString("yyyy-MM-dd"),
                _ => string.Empty,
            };

            var originalValue = _originalValues.TryGetValue(propertyName, out var storedValue)
                ? storedValue
                : string.Empty;

            return !string.Equals(originalValue, currentValue, StringComparison.Ordinal);
        }

        private bool HasAnyPendingChanges()
        {
            return HasMetadataChanges() || HasCoverArtChanges();
        }
    }
}
