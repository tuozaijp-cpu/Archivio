using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Archivio.Helpers;
using Archivio.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using TagLib;
using Windows.Storage;
using System.Threading.Tasks;

namespace Archivio.Models
{
    public sealed class VideoFileItem : ViewModelBase
    {
        private string _title = string.Empty;
        private string _fileSizeText = string.Empty;
        private ulong _fileSizeBytes;
        private string _participants = string.Empty;
        private string _category = string.Empty;
        private string _comment = string.Empty;
        private string _catalogNumber = string.Empty;
        private string _rating = string.Empty;
        private string _contentDistributor = string.Empty;
        private string _publisher = string.Empty;
        private string _duration = string.Empty;
        private TimeSpan? _durationValue;
        private string _frameWidth = string.Empty;
        private string _frameHeight = string.Empty;
        private string _frameRate = string.Empty;
        private string _videoBitrate = string.Empty;
        private string _videoCompression = string.Empty;
        private string _audioSampleRate = string.Empty;
        private string _audioBitrate = string.Empty;
        private string _audioFormat = string.Empty;
        private string _releaseDateText = string.Empty;
        private DateTimeOffset _releaseDate = new(1900, 1, 1, 0, 0, 0, TimeSpan.Zero);
        private BitmapImage? _coverArt;
        private BitmapImage? _thumbnailImage;
        private ObservableCollection<BitmapImage> _coverArts = new();
        private bool _hasCoverArt;
        private bool _isApplyingInitialValues;
        private bool _isLoaded;
        private bool _isCoverArtLoaded;
        private bool _isSaving;
        private bool _saveFailed;
        private bool _isCoverArtDirty;
        private bool _isThumbnailLoading;
        private bool _thumbnailLoadFailed;
        private bool _isDeleted;
        private int _selectedCoverArtIndex;
        private int _originalPictureBytesCount;
        private int _originalPictureBytesHash;
        private List<CoverArtImageData> _coverArtImages = new();
        private Dictionary<string, string> _originalValues = new(StringComparer.OrdinalIgnoreCase);
        private string _fileName = string.Empty;
        private string _fullPath = string.Empty;

        public VideoFileItem(StorageFile file)
        {
            _file = file;
            _fileName = file.Name;
            _fullPath = file.Path;
        }

        public VideoFileItem(string path)
        {
            _fullPath = path;
            _fileName = System.IO.Path.GetFileName(path);
        }

        private StorageFile? _file;

        public StorageFile? File => _file;

        public async Task<StorageFile> GetFileAsync()
        {
            return _file ??= await StorageFile.GetFileFromPathAsync(_fullPath);
        }

        public string FileName
        {
            get => _fileName;
            set
            {
                if (SetProperty(ref _fileName, value))
                {
                    OnPropertyChanged(nameof(DisplayTitle));
                }
            }
        }

        public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? FileName : Title;

        public string DisplayParticipants => string.IsNullOrWhiteSpace(Participants)
            ? LanguageManager.GetString("Placeholder_None")
            : Participants;

        public string ThumbnailLoadingText => LanguageManager.GetString("Thumbnail_Loading");

        public string ThumbnailEmptyText => LanguageManager.GetString("Thumbnail_Empty");

        public string ThumbnailFailedText => LanguageManager.GetString("Thumbnail_Failed");

        public void RefreshLocalizedText()
        {
            OnPropertyChanged(nameof(DisplayParticipants));
            OnPropertyChanged(nameof(ThumbnailLoadingText));
            OnPropertyChanged(nameof(ThumbnailEmptyText));
            OnPropertyChanged(nameof(ThumbnailFailedText));
        }

        public string FullPath
        {
            get => _fullPath;
            private set => SetProperty(ref _fullPath, value);
        }

        public void SyncFromStorageFile()
        {
            if (_file is null) return;
            _fileName = _file.Name;
            FullPath = _file.Path;
            OnPropertyChanged(nameof(FileName));
            OnPropertyChanged(nameof(DisplayTitle));
        }

        public ulong FileSizeBytes
        {
            get => _fileSizeBytes;
            set => SetProperty(ref _fileSizeBytes, value);
        }

        public string FileSizeText
        {
            get => _fileSizeText;
            set => SetProperty(ref _fileSizeText, value);
        }

        public TimeSpan? DurationValue
        {
            get => _durationValue;
            set => SetProperty(ref _durationValue, value);
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

        public bool IsCoverArtLoaded
        {
            get => _isCoverArtLoaded;
            set => SetProperty(ref _isCoverArtLoaded, value);
        }

        public string Title
        {
            get => _title;
            set
            {
                if (SetProperty(ref _title, value))
                {
                    OnPropertyChanged(nameof(DisplayTitle));
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
                    OnPropertyChanged(nameof(DisplayParticipants));
                    UpdateChangeState(nameof(Participants), value);
                }
            }
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
                    if (r >= 1 && r <= 5) return (int)r;

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
                return RatingStarsIndex switch
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
            set
            {
                if (SetProperty(ref _duration, value))
                {
                    DurationValue = DisplayFormatHelper.TryParseDuration(value);
                }
            }
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
                    _releaseDateText = DisplayFormatHelper.FormatReleaseDateDisplayText(normalizedDate);
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

        public BitmapImage? ThumbnailImage
        {
            get => _thumbnailImage;
            set
            {
                if (SetProperty(ref _thumbnailImage, value))
                {
                    NotifyThumbnailStateChanged();
                }
            }
        }

        public bool IsThumbnailLoading
        {
            get => _isThumbnailLoading;
            set
            {
                if (SetProperty(ref _isThumbnailLoading, value))
                {
                    NotifyThumbnailStateChanged();
                }
            }
        }

        public bool ThumbnailLoadFailed
        {
            get => _thumbnailLoadFailed;
            set
            {
                if (SetProperty(ref _thumbnailLoadFailed, value))
                {
                    NotifyThumbnailStateChanged();
                }
            }
        }

        public Visibility ThumbnailLoadingVisibility => IsThumbnailLoading ? Visibility.Visible : Visibility.Collapsed;

        public Visibility ThumbnailEmptyVisibility => !IsThumbnailLoading && !ThumbnailLoadFailed && ThumbnailImage is null
            ? Visibility.Visible
            : Visibility.Collapsed;

        public Visibility ThumbnailErrorVisibility => !IsThumbnailLoading && ThumbnailLoadFailed
            ? Visibility.Visible
            : Visibility.Collapsed;

        private void NotifyThumbnailStateChanged()
        {
            OnPropertyChanged(nameof(ThumbnailLoadingVisibility));
            OnPropertyChanged(nameof(ThumbnailEmptyVisibility));
            OnPropertyChanged(nameof(ThumbnailErrorVisibility));
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
                        SaveFailed = false;
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

        public bool IsDeleted
        {
            get => _isDeleted;
            set
            {
                if (SetProperty(ref _isDeleted, value))
                {
                    OnPropertyChanged(nameof(StatusText));
                }
            }
        }

        public string StatusText
        {
            get
            {
                if (IsDeleted) return LanguageManager.GetString("Status_Deleted");
                if (IsSaving) return LanguageManager.GetString("Status_Saving");
                if (SaveFailed) return LanguageManager.GetString("Status_Failed");
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

            CoverArt = CoverArts.Count > 0 ? CoverArts[0] : null;
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

        public bool HasPendingChanges => HasMetadataChanges() || HasCoverArtChanges();

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

        private static int CalculatePictureBytesHash(IReadOnlyList<CoverArtImageData> list)
        {
            if (list == null) return 0;

            int hash = 17;
            foreach (var coverArt in list)
            {
                hash = hash * 23 + (coverArt?.Data?.Length ?? 0);
                hash = hash * 23 + (coverArt?.MimeType?.GetHashCode(StringComparison.Ordinal) ?? 0);
                hash = hash * 23 + (coverArt?.Description?.GetHashCode(StringComparison.Ordinal) ?? 0);
                hash = hash * 23 + (int)(coverArt?.Type ?? PictureType.FrontCover);
            }

            return hash;
        }

        public void UpdateOriginalValuesFromSnapshot(VideoMetadataSnapshot snapshot)
        {
            if (snapshot == null) return;

            _isApplyingInitialValues = true;
            try
            {
                _originalValues[nameof(Title)] = snapshot.Title ?? string.Empty;
                _originalValues[nameof(Participants)] = snapshot.Participants ?? string.Empty;
                _originalValues[nameof(Category)] = snapshot.Category ?? string.Empty;
                _originalValues[nameof(Comment)] = snapshot.Comment ?? string.Empty;
                _originalValues[nameof(CatalogNumber)] = snapshot.CatalogNumber ?? string.Empty;
                _originalValues[nameof(Rating)] = snapshot.Rating ?? string.Empty;
                _originalValues[nameof(ContentDistributor)] = snapshot.ContentDistributor ?? string.Empty;
                _originalValues[nameof(Publisher)] = snapshot.Publisher ?? string.Empty;
                _originalValues[nameof(ReleaseDate)] = snapshot.ReleaseDate.ToString("yyyy-MM-dd");
                HasPendingChangesNotify();
            }
            finally
            {
                _isApplyingInitialValues = false;
            }
        }

        public bool HasMetadataChangesComparedTo(VideoMetadataSnapshot snapshot)
        {
            if (snapshot == null) return true;

            bool titleChanged = !string.Equals((_title ?? string.Empty).Trim(), (snapshot.Title ?? string.Empty).Trim(), StringComparison.Ordinal);
            
            bool participantsChanged = !ArePerformersEqual(_participants, snapshot.Participants);
            
            bool commentChanged = !string.Equals((_comment ?? string.Empty).Trim(), (snapshot.Comment ?? string.Empty).Trim(), StringComparison.Ordinal);
            bool categoryChanged = !AreCategoriesEqual(_category, snapshot.Category);
            bool catalogNumberChanged = !string.Equals((_catalogNumber ?? string.Empty).Trim(), (snapshot.CatalogNumber ?? string.Empty).Trim(), StringComparison.Ordinal);
            
            bool ratingChanged = !string.Equals((_rating ?? string.Empty).Trim(), (snapshot.Rating ?? string.Empty).Trim(), StringComparison.Ordinal);
            
            bool contentDistributorChanged = !string.Equals((_contentDistributor ?? string.Empty).Trim(), (snapshot.ContentDistributor ?? string.Empty).Trim(), StringComparison.Ordinal);
            bool publisherChanged = !string.Equals((_publisher ?? string.Empty).Trim(), (snapshot.Publisher ?? string.Empty).Trim(), StringComparison.Ordinal);
            
            bool releaseDateChanged = HasReleaseDateChanged(_releaseDate, snapshot.ReleaseDate);

            return titleChanged || participantsChanged || commentChanged || categoryChanged || catalogNumberChanged || ratingChanged || contentDistributorChanged || publisherChanged || releaseDateChanged;
        }

        private static bool ArePerformersEqual(string? p1, string? p2)
        {
            var arr1 = SplitValues(p1);
            var arr2 = SplitValues(p2);
            return arr1.SequenceEqual(arr2, StringComparer.Ordinal);
        }

        private static bool AreCategoriesEqual(string? c1, string? c2)
        {
            var arr1 = SplitValues(c1);
            var arr2 = SplitValues(c2);
            return arr1.SequenceEqual(arr2, StringComparer.Ordinal);
        }

        private static string[] SplitValues(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return Array.Empty<string>();
            return value.Split(new[] { ';', ',', '，', '；' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(p => p.Trim())
                        .Where(p => !string.IsNullOrEmpty(p))
                        .ToArray();
        }

        private static bool HasReleaseDateChanged(DateTimeOffset currentValue, DateTimeOffset? originalValue)
        {
            if (originalValue is null)
            {
                return currentValue.Year > 1900;
            }

            return currentValue.Date != originalValue.Value.Date;
        }

        private void UpdateChangeState(string propertyName, string value)
        {
            if (_isApplyingInitialValues)
            {
                return;
            }

            SaveFailed = false;
            HasPendingChangesNotify();
        }

        private void HasPendingChangesNotify()
        {
            OnPropertyChanged(nameof(HasPendingChanges));
            OnPropertyChanged(nameof(HasMetadataChangesNotify));
            OnPropertyChanged(nameof(HasCoverArtChangesNotify));
        }
    }
}
