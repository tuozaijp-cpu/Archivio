using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    /// <summary>
    /// 動画詳細ペイン（メタデータ編集・保存、カバーアート操作）の状態と操作を管理する ViewModel。
    /// </summary>
    public sealed class VideoDetailsViewModel : ViewModelBase
    {
        private readonly IVideoMetadataService _metadataService;
        private readonly SemaphoreSlim _metadataReadSemaphore;
        private readonly SemaphoreSlim _metadataSaveSemaphore;
        private readonly Action<bool> _setBusy;
        private readonly Action<string, Exception?, string?> _setError;
        private readonly Action<string> _setSuccess;
        private readonly Func<string> _getRootFolderPath;
        private readonly Action<string, ulong, DateTimeOffset, VideoMetadataSnapshot>? _onMetadataUpdated;
        private readonly Action<string>? _onCoverArtSaved;

        private VideoFileItem? _selectedVideo;
        private CancellationTokenSource? _loadingCts;

        public VideoDetailsViewModel(
            IVideoMetadataService metadataService,
            SemaphoreSlim metadataReadSemaphore,
            SemaphoreSlim metadataSaveSemaphore,
            Action<bool> setBusy,
            Action<string, Exception?, string?> setError,
            Action<string> setSuccess,
            Func<string> getRootFolderPath,
            Action<string, ulong, DateTimeOffset, VideoMetadataSnapshot>? onMetadataUpdated = null,
            Action<string>? onCoverArtSaved = null)
        {
            _metadataService = metadataService ?? throw new ArgumentNullException(nameof(metadataService));
            _metadataReadSemaphore = metadataReadSemaphore ?? throw new ArgumentNullException(nameof(metadataReadSemaphore));
            _metadataSaveSemaphore = metadataSaveSemaphore ?? throw new ArgumentNullException(nameof(metadataSaveSemaphore));
            _setBusy = setBusy ?? throw new ArgumentNullException(nameof(setBusy));
            _setError = setError ?? throw new ArgumentNullException(nameof(setError));
            _setSuccess = setSuccess ?? throw new ArgumentNullException(nameof(setSuccess));
            _getRootFolderPath = getRootFolderPath ?? throw new ArgumentNullException(nameof(getRootFolderPath));
            _onMetadataUpdated = onMetadataUpdated;
            _onCoverArtSaved = onCoverArtSaved;
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

                    OnPropertyChanged(nameof(IsVideoSelected));
                    OnPropertyChanged(nameof(DetailedPanelVisibility));
                    OnPropertyChanged(nameof(HasPendingChanges));

                    _ = SetActiveItemAsync(value);
                }
            }
        }

        public bool IsVideoSelected => SelectedVideo is not null;

        public Visibility DetailedPanelVisibility => IsVideoSelected ? Visibility.Visible : Visibility.Collapsed;

        public bool HasPendingChanges => SelectedVideo?.HasPendingChanges ?? false;

        public async Task SetActiveItemAsync(VideoFileItem? item)
        {
            _loadingCts?.Cancel();
            _loadingCts = new CancellationTokenSource();
            var cts = _loadingCts;

            if (item is null)
            {
                return;
            }

            if (item.IsCoverArtLoaded && (item.CoverArtImages.Count > 0 || item.HasCoverArt))
            {
                OnPropertyChanged(nameof(HasPendingChanges));
                return;
            }

            _setBusy(true);
            try
            {
                cts.Token.ThrowIfCancellationRequested();

                // カバー画像のロード
                await LoadCoverArtAsync(item);

                if (!cts.IsCancellationRequested && SelectedVideo == item)
                {
                    await DispatcherHelper.RunOnUIThreadAsync(() =>
                    {
                        item.IsCoverArtLoaded = true;
                        OnPropertyChanged(nameof(HasPendingChanges));
                    });
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                AppLogger.Error("選択中の動画カバー画像の読み込みに失敗しました", ex, item.FullPath);
                if (SelectedVideo == item)
                {
                    _setError("選択中の動画カバー画像の読み込みに失敗しました。詳細はログを確認してください。", ex, item.FullPath);
                }
            }
            finally
            {
                if (!cts.IsCancellationRequested)
                {
                    _setBusy(false);
                }
            }
        }

        public async Task LoadCoverArtAsync(VideoFileItem item, bool force = false)
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
                            item.ResetCoverArtChangeTracking();
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Error("カバー画像の表示用変換に失敗しました", ex, item.FullPath);
                            ClearCoverArtState(item);
                            if (SelectedVideo == item)
                            {
                                _setError("カバー画像の表示に失敗しました。詳細はログを確認してください。", ex, item.FullPath);
                            }
                        }
                    });
                }
                else
                {
                    await DispatcherHelper.RunOnUIThreadAsync(() => ClearCoverArtState(item));
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("カバー画像の読み込みに失敗しました", ex, item.FullPath);
                await DispatcherHelper.RunOnUIThreadAsync(() =>
                {
                    ClearCoverArtState(item);
                    if (SelectedVideo == item)
                    {
                        _setError("カバー画像の読み込みに失敗しました。詳細はログを確認してください。", ex, item.FullPath);
                    }
                });
            }
            finally
            {
                _metadataReadSemaphore.Release();
            }
        }

        private static void ClearCoverArtState(VideoFileItem item)
        {
            item.CoverArts = new ObservableCollection<BitmapImage>();
            item.CoverArt = null;
            item.HasCoverArt = false;
            item.ResetCoverArtChangeTracking();
        }

        public async Task SaveMetadataAsync()
        {
            if (SelectedVideo is null)
            {
                return;
            }

            var targetVideo = SelectedVideo;
            targetVideo.IsSaving = true;
            targetVideo.SaveFailed = false;
            _setBusy(true);
            _setError(string.Empty, null, null); // Clear message
            try
            {
                // キャッシュに頼らず、再度ファイルから直接メタデータを読み込む
                var targetFile = await targetVideo.GetFileAsync();
                var loadResult = await _metadataService.LoadMetadataAsync(targetFile);
                if (loadResult != null && loadResult.Metadata != null)
                {
                    var freshMeta = loadResult.Metadata;

                    // 登録しようとしている情報（現在のUI値）と再度読み込んだ情報を比較する
                    bool hasActualChanges = targetVideo.HasMetadataChangesComparedTo(freshMeta);

                    if (!hasActualChanges)
                    {
                        await DispatcherHelper.RunOnUIThreadAsync(() =>
                        {
                            targetVideo.SaveFailed = false;
                            
                            // _originalValuesをファイルから読み込んだ最新値に更新することで変更トラッキングをリセットする
                            targetVideo.UpdateOriginalValuesFromSnapshot(freshMeta);
                            
                            _setSuccess("変更なし");
                            OnPropertyChanged(nameof(HasPendingChanges));
                        });
                        return;
                    }

                    // 変更がある場合、最新のファイル内メタデータを元の値として設定してから保存処理を実行する。
                    // これにより、保存処理が最新のディスク情報に基づいて差分を正確に書き込める。
                    await DispatcherHelper.RunOnUIThreadAsync(() =>
                    {
                        targetVideo.UpdateOriginalValuesFromSnapshot(freshMeta);
                    });
                }

                var result = await SaveWindowsPropertiesAsync(targetVideo);

                await DispatcherHelper.RunOnUIThreadAsync(() =>
                {
                    targetVideo.SaveFailed = !result.Succeeded;
                    if (result.Succeeded)
                    {
                        _setSuccess(DisplayFormatHelper.FormatOperationMessage(result));
                        targetVideo.ResetMetadataChangeTracking();
                    }
                    else
                    {
                        _setError(DisplayFormatHelper.FormatOperationMessage(result), null, null);
                    }

                    OnPropertyChanged(nameof(HasPendingChanges));
                });
            }
            catch (Exception ex)
            {
                AppLogger.Error("メタデータの保存に失敗しました", ex, targetVideo.FullPath);
                await DispatcherHelper.RunOnUIThreadAsync(() =>
                {
                    targetVideo.SaveFailed = true;
                    _setError($"保存エラー: {ex.Message}", ex, targetVideo.FullPath);
                });
            }
            finally
            {
                targetVideo.IsSaving = false;
                _setBusy(false);
            }
        }

        public async Task SaveCoverArtAsync()
        {
            if (SelectedVideo is null)
            {
                return;
            }

            var targetVideo = SelectedVideo;
            targetVideo.IsSaving = true;
            targetVideo.SaveFailed = false;
            _setBusy(true);
            _setError(string.Empty, null, null); // Clear message
            try
            {
                var result = await SaveCoverArtAsync(targetVideo);

                await LoadCoverArtAsync(targetVideo, force: true);
                if (result.Succeeded)
                {
                    _onCoverArtSaved?.Invoke(targetVideo.FullPath);
                }

                await DispatcherHelper.RunOnUIThreadAsync(() =>
                {
                    targetVideo.SaveFailed = !result.Succeeded;
                    if (result.Succeeded)
                    {
                        _setSuccess(DisplayFormatHelper.FormatOperationMessage(result));
                    }
                    else
                    {
                        _setError(DisplayFormatHelper.FormatOperationMessage(result), null, null);
                    }

                    OnPropertyChanged(nameof(HasPendingChanges));
                });
            }
            catch (Exception ex)
            {
                AppLogger.Error("カバー画像の保存に失敗しました", ex, targetVideo.FullPath);
                await DispatcherHelper.RunOnUIThreadAsync(() =>
                {
                    targetVideo.SaveFailed = true;
                    _setError($"画像保存エラー: {ex.Message}", ex, targetVideo.FullPath);
                });
            }
            finally
            {
                targetVideo.IsSaving = false;
                _setBusy(false);
            }
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
                var result = await _metadataService.SaveMetadataAsync(file, metadata, originalMetadata, changedProperties);
                if (result.Succeeded)
                {
                    try
                    {
                        var rootFolder = _getRootFolderPath();
                        if (!string.IsNullOrWhiteSpace(rootFolder))
                        {
                            var fileInfo = new System.IO.FileInfo(item.FullPath);
                            var fileSize = (ulong)fileInfo.Length;
                            var lastWriteTime = fileInfo.LastWriteTimeUtc;
                            await MetadataCacheManager.UpdateEntryAsync(rootFolder, item.FullPath, lastWriteTime, fileSize, metadata);
                            _onMetadataUpdated?.Invoke(item.FullPath, fileSize, lastWriteTime, metadata);
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Error("メタデータ保存後のキャッシュ更新に失敗しました", ex, item.FullPath);
                    }
                }
                return result;
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
                AppLogger.Error("カバー画像の保存に失敗しました", ex, item.FullPath);
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
            if (SelectedVideo is null)
            {
                return;
            }

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
            if (file is null)
            {
                return;
            }

            try
            {
                using var stream = await file.OpenReadAsync();
                var bytes = new byte[stream.Size];
                using var reader = new DataReader(stream);
                await reader.LoadAsync((uint)stream.Size);
                reader.ReadBytes(bytes);

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
                SelectedVideo.IsCoverArtDirty = true;

                SelectedVideo.NotifyCoverArtPropertiesChanged();
                OnPropertyChanged(nameof(HasPendingChanges));
            }
            catch (Exception ex)
            {
                _setError($"画像の追加に失敗しました: {ex.Message}", ex, file.Path);
            }
        }

        public async Task ReplaceCoverArtImageAsync()
        {
            if (SelectedVideo is null)
            {
                return;
            }

            int index = SelectedVideo.SelectedCoverArtIndex;
            if (index < 0 || index >= SelectedVideo.CoverArts.Count)
            {
                return;
            }

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
            if (file is null)
            {
                return;
            }

            try
            {
                using var stream = await file.OpenReadAsync();
                var bytes = new byte[stream.Size];
                using var reader = new DataReader(stream);
                await reader.LoadAsync((uint)stream.Size);
                reader.ReadBytes(bytes);

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
                SelectedVideo.IsCoverArtDirty = true;

                SelectedVideo.NotifyCoverArtPropertiesChanged();
                OnPropertyChanged(nameof(HasPendingChanges));
            }
            catch (Exception ex)
            {
                _setError($"画像の差し替えに失敗しました: {ex.Message}", ex, file.Path);
            }
        }

        public void DeleteCoverArtImage()
        {
            if (SelectedVideo is null)
            {
                return;
            }

            int index = SelectedVideo.SelectedCoverArtIndex;
            if (index < 0 || index >= SelectedVideo.CoverArts.Count)
            {
                return;
            }

            SelectedVideo.CoverArtImages.RemoveAt(index);
            SelectedVideo.CoverArts.RemoveAt(index);
            SelectedVideo.IsCoverArtDirty = true;

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

        private void SelectedVideo_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(VideoFileItem.HasPendingChanges))
            {
                OnPropertyChanged(nameof(HasPendingChanges));
            }
        }
    }
}
