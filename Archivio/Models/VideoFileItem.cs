using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using Archivio.Helpers;
using Archivio.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using TagLib;

namespace Archivio.Models
{
    public sealed class VideoFileItem : ViewModelBase
    {
        /// <summary>ドメイン状態。既存のフラットな公開プロパティは互換ファサードとして維持する。</summary>
        private readonly VideoFileInfoState _fileInfoState = new();
        private readonly VideoMetadataState _metadataState = new();
        private readonly VideoTechnicalState _technicalState = new();
        private readonly VideoCoverArtState _coverArtState = new();
        private readonly VideoChangeTrackingState _changeTrackingState = new();

        public VideoFileInfoState FileInfoState => _fileInfoState;
        public VideoMetadataState MetadataState => _metadataState;
        public VideoTechnicalState TechnicalState => _technicalState;
        public VideoCoverArtState CoverArtState => _coverArtState;
        public VideoChangeTrackingState ChangeTrackingState => _changeTrackingState;

        /// <summary>動画ファイルから読み込んだArchivio共通メタデータ。既存プロパティとの互換用に併存する。</summary>
        public MetadataDocument StructuredMetadata
        {
            get => _metadataState.StructuredMetadata;
            set
            {
                var next = value ?? new MetadataDocument();
                if (ReferenceEquals(_metadataState.StructuredMetadata, next))
                {
                    return;
                }

                _metadataState.StructuredMetadata = next;
                OnPropertyChanged();
                // 一覧・詳細画面はこのプロパティ配下の値へバインドしているため、
                // 保存後のファイル再読込時に親プロパティの変更を通知する。
                OnPropertyChanged(nameof(CastCreditsText));
                OnPropertyChanged(nameof(CrewCreditsText));
            }
        }
        /// <summary>FFprobe等で取得した全ストリームの技術情報。保存対象外。</summary>
        public VideoTechnicalInfo TechnicalInfo => _technicalState.Info;

        public void SetTechnicalInfo(VideoTechnicalInfo value)
        {
            _technicalState.Info = value ?? new VideoTechnicalInfo();
            OnPropertyChanged(nameof(TechnicalInfo));
        }

        /// <summary>画面編集用の出演者表現。保存時は構造化された出演者・役名・主演へ戻す。</summary>
        public string CastCreditsText
        {
            get => string.Join("; ", StructuredMetadata.PeopleAndStaff.Cast
                .OrderBy(credit => credit.Order)
                .Select(credit => (credit.IsLead ? "*" : string.Empty) +
                    (string.IsNullOrWhiteSpace(credit.CharacterName)
                        ? credit.PersonName
                        : $"{credit.PersonName} ({credit.CharacterName})")));
            set
            {
                StructuredMetadata.PeopleAndStaff.Cast = (value ?? string.Empty)
                    .Split(new[] { ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select((entry, index) =>
                    {
                        var text = entry.Trim();
                        var isLead = text.StartsWith("*", StringComparison.Ordinal);
                        if (isLead) text = text[1..].Trim();
                        var separator = text.LastIndexOf('(');
                        var hasRole = separator > 0 && text.EndsWith(")", StringComparison.Ordinal);
                        return new CastCredit
                        {
                            PersonName = hasRole ? text[..separator].Trim() : text,
                            CharacterName = hasRole ? text[(separator + 1)..^1].Trim() : string.Empty,
                            IsLead = isLead,
                            Order = index
                        };
                    })
                    .Where(credit => !string.IsNullOrWhiteSpace(credit.PersonName))
                    .ToList();
                OnPropertyChanged();
            }
        }

        public string CrewCreditsText
        {
            get => string.Join("; ", StructuredMetadata.PeopleAndStaff.Crew
                .OrderBy(credit => credit.Order)
                .Select(credit => string.IsNullOrWhiteSpace(credit.RoleId)
                    ? credit.PersonName
                    : $"{credit.RoleId}: {credit.PersonName}"));
            set
            {
                StructuredMetadata.PeopleAndStaff.Crew = (value ?? string.Empty)
                    .Split(new[] { ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select((entry, index) =>
                    {
                        var text = entry.Trim();
                        var separator = text.IndexOf(':');
                        return new CrewCredit
                        {
                            RoleId = separator > 0 ? text[..separator].Trim() : string.Empty,
                            PersonName = separator > 0 ? text[(separator + 1)..].Trim() : text,
                            Order = index
                        };
                    })
                    .Where(credit => !string.IsNullOrWhiteSpace(credit.PersonName))
                    .ToList();
                OnPropertyChanged();
            }
        }

        public IReadOnlyCollection<string> DirtyCustomMetadataFieldIds => _changeTrackingState.DirtyCustomFieldIds;

        public bool IsFileSaveInProgress => _changeTrackingState.ActiveFileSaveOperationCount > 0;

        public void BeginFileSaveOperation()
        {
            if (_changeTrackingState.IncrementSaveOperation() == 1)
            {
                OnPropertyChanged(nameof(IsFileSaveInProgress));
            }
        }

        public void EndFileSaveOperation()
        {
            var remainingOperations = _changeTrackingState.DecrementSaveOperation();
            if (remainingOperations < 0)
            {
                _changeTrackingState.ResetSaveOperations();
                throw new InvalidOperationException("No file save operation is in progress.");
            }

            if (remainingOperations == 0)
            {
                OnPropertyChanged(nameof(IsFileSaveInProgress));
            }
        }

        public void MarkCustomMetadataFieldChanged(string fieldId)
        {
            if (string.IsNullOrWhiteSpace(fieldId)) return;
            _changeTrackingState.DirtyCustomFieldIds.Add(fieldId);
            HasPendingChangesNotify();
        }

        public void ResetCustomMetadataChanges()
        {
            _changeTrackingState.DirtyCustomFieldIds.Clear();
            HasPendingChangesNotify();
        }

        private bool SetStateProperty<T>(string propertyName, T current, T value, Action<T> setter)
        {
            if (EqualityComparer<T>.Default.Equals(current, value)) return false;
            setter(value);
            OnPropertyChanged(propertyName);
            return true;
        }

        private BitmapImage? _coverArt;
        private BitmapImage? _thumbnailImage;
        private ObservableCollection<BitmapImage> _coverArts = new();
        private bool _isThumbnailLoading;
        private bool _thumbnailLoadFailed;

        public VideoFileItem(string path)
        {
            _fileInfoState.FullPath = path;
            _fileInfoState.FileName = System.IO.Path.GetFileName(path);
        }

        public string FileName
        {
            get => _fileInfoState.FileName;
            set { if (SetStateProperty(nameof(FileName), _fileInfoState.FileName, value ?? string.Empty, v => _fileInfoState.FileName = v)) OnPropertyChanged(nameof(DisplayTitle)); }
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
            get => _fileInfoState.FullPath;
            private set
            {
                SetStateProperty(nameof(FullPath), _fileInfoState.FullPath, value ?? string.Empty, v => _fileInfoState.FullPath = v);
            }
        }

        public void UpdatePath(string path)
        {
            _fileInfoState.FileName = System.IO.Path.GetFileName(path);
            FullPath = path;
            OnPropertyChanged(nameof(FileName));
            OnPropertyChanged(nameof(DisplayTitle));
        }

        public ulong FileSizeBytes
        {
            get => _fileInfoState.FileSizeBytes;
            set
            {
                SetStateProperty(nameof(FileSizeBytes), _fileInfoState.FileSizeBytes, value, v => _fileInfoState.FileSizeBytes = v);
            }
        }

        public string FileSizeText
        {
            get => _fileInfoState.FileSizeText;
            set
            {
                SetStateProperty(nameof(FileSizeText), _fileInfoState.FileSizeText, value ?? string.Empty, v => _fileInfoState.FileSizeText = v);
            }
        }

        public TimeSpan? DurationValue
        {
            get => _metadataState.DurationValue;
            set
            {
                SetStateProperty(nameof(DurationValue), _metadataState.DurationValue, value, v => _metadataState.DurationValue = v);
            }
        }

        public VideoMetadataSnapshot CreateCurrentMetadataSnapshot()
        {
            return new VideoMetadataSnapshot
            {
                Title = Title,
                Participants = Participants,
                CatalogNumber = CatalogNumber,
                Category = Category,
                Rating = Rating,
                ContentDistributor = ContentDistributor,
                Publisher = Publisher,
                Duration = Duration,
                FrameWidth = FrameWidth,
                FrameHeight = FrameHeight,
                FrameRate = FrameRate,
                VideoBitrate = VideoBitrate,
                VideoCompression = VideoCompression,
                AudioSampleRate = AudioSampleRate,
                AudioBitrate = AudioBitrate,
                AudioFormat = AudioFormat,
                Comment = Comment,
                ReleaseDateText = ReleaseDateText,
                ReleaseDate = ReleaseDate,
                Technical = new VideoTechnicalMetadata
                {
                    Duration = TechnicalInfo.Duration ?? DurationValue,
                    FrameWidth = TechnicalInfo.PrimaryVideo?.Width,
                    FrameHeight = TechnicalInfo.PrimaryVideo?.Height,
                    FrameRate = TechnicalInfo.PrimaryVideo?.FrameRate,
                    VideoBitrate = TechnicalInfo.PrimaryVideo?.Bitrate,
                    VideoCodec = TechnicalInfo.PrimaryVideo?.Codec ?? VideoCompression,
                    AudioSampleRate = TechnicalInfo.PrimaryAudio?.SampleRate,
                    AudioBitrate = TechnicalInfo.PrimaryAudio?.Bitrate,
                    AudioCodec = TechnicalInfo.PrimaryAudio?.Codec ?? AudioFormat
                },
                TechnicalInfo = TechnicalInfo,
                StructuredMetadata = StructuredMetadata.Clone()
            };
        }

        public VideoMetadataSnapshot CreateOriginalMetadataSnapshot()
        {
            return new VideoMetadataSnapshot
            {
                Title = _changeTrackingState.OriginalValues.TryGetValue(nameof(Title), out var title) ? title : Title,
                Participants = _changeTrackingState.OriginalValues.TryGetValue(nameof(Participants), out var participants) ? participants : Participants,
                CatalogNumber = _changeTrackingState.OriginalValues.TryGetValue(nameof(CatalogNumber), out var catalogNumber) ? catalogNumber : CatalogNumber,
                Category = _changeTrackingState.OriginalValues.TryGetValue(nameof(Category), out var category) ? category : Category,
                Rating = _changeTrackingState.OriginalValues.TryGetValue(nameof(Rating), out var rating) ? rating : Rating,
                ContentDistributor = _changeTrackingState.OriginalValues.TryGetValue(nameof(ContentDistributor), out var contentDistributor) ? contentDistributor : ContentDistributor,
                Publisher = _changeTrackingState.OriginalValues.TryGetValue(nameof(Publisher), out var publisher) ? publisher : Publisher,
                Comment = _changeTrackingState.OriginalValues.TryGetValue(nameof(Comment), out var comment) ? comment : Comment,
                ReleaseDateText = _changeTrackingState.OriginalValues.TryGetValue(nameof(ReleaseDate), out var releaseDateText) ? releaseDateText : ReleaseDateText,
            ReleaseDate = DateTimeOffset.TryParse(_changeTrackingState.OriginalValues.TryGetValue(nameof(ReleaseDate), out var releaseDateValue) ? releaseDateValue : ReleaseDateText, out var parsedDate)
                    ? parsedDate
                    : ReleaseDate,
                StructuredMetadata = _changeTrackingState.OriginalStructuredMetadata.Clone()
            };
        }

        public bool IsLoaded
        {
            get => _metadataState.IsLoaded;
            set => SetStateProperty(nameof(IsLoaded), _metadataState.IsLoaded, value, v => _metadataState.IsLoaded = v);
        }

        public bool IsCoverArtLoaded
        {
            get => _coverArtState.IsLoaded;
            set
            {
                SetStateProperty(nameof(IsCoverArtLoaded), _coverArtState.IsLoaded, value, v => _coverArtState.IsLoaded = v);
            }
        }

        public string Title
        {
            get => _metadataState.Title;
            set
            {
                if (SetStateProperty(nameof(Title), _metadataState.Title, value ?? string.Empty, v => _metadataState.Title = v))
                {
                    OnPropertyChanged(nameof(DisplayTitle));
                    UpdateChangeState(nameof(Title), value ?? string.Empty);
                }
            }
        }

        public string Participants
        {
            get => _metadataState.Participants;
            set
            {
                if (SetStateProperty(nameof(Participants), _metadataState.Participants, value ?? string.Empty, v => _metadataState.Participants = v))
                {
                    OnPropertyChanged(nameof(DisplayParticipants));
                    UpdateChangeState(nameof(Participants), value ?? string.Empty);
                }
            }
        }

        public string Category
        {
            get => _metadataState.Category;
            set
            {
                if (SetStateProperty(nameof(Category), _metadataState.Category, value ?? string.Empty, v => _metadataState.Category = v))
                {
                    UpdateChangeState(nameof(Category), value ?? string.Empty);
                }
            }
        }

        public string Comment
        {
            get => _metadataState.Comment;
            set
            {
                if (SetStateProperty(nameof(Comment), _metadataState.Comment, value ?? string.Empty, v => _metadataState.Comment = v))
                {
                    UpdateChangeState(nameof(Comment), value ?? string.Empty);
                }
            }
        }

        public string CatalogNumber
        {
            get => _metadataState.CatalogNumber;
            set
            {
                if (SetStateProperty(nameof(CatalogNumber), _metadataState.CatalogNumber, value ?? string.Empty, v => _metadataState.CatalogNumber = v))
                {
                    UpdateChangeState(nameof(CatalogNumber), value ?? string.Empty);
                }
            }
        }

        public string Rating
        {
            get => _metadataState.Rating;
            set
            {
                if (SetStateProperty(nameof(Rating), _metadataState.Rating, value ?? string.Empty, v => _metadataState.Rating = v))
                {
                    UpdateChangeState(nameof(Rating), value ?? string.Empty);
                    OnPropertyChanged(nameof(RatingStarsIndex));
                    OnPropertyChanged(nameof(RatingStarsText));
                }
            }
        }

        public int RatingStarsIndex
        {
            get
            {
                if (uint.TryParse(Rating, out var r))
                {
                    if (r >= 1 && r <= 5) return (int)r;

                    if (r >= 80) return 5;
                    if (r >= 60) return 4;
                    if (r >= 40) return 3;
                    if (r >= 15) return 2;
                    if (r >= 1) return 1;
                }
                else if (!string.IsNullOrWhiteSpace(Rating))
                {
                    var filledStarsCount = Rating.Count(c => c == '★');
                    if (filledStarsCount > 0) return filledStarsCount;

                    var digits = new string(Rating.Where(char.IsDigit).ToArray());
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
            get => _metadataState.ContentDistributor;
            set
            {
                if (SetStateProperty(nameof(ContentDistributor), _metadataState.ContentDistributor, value ?? string.Empty, v => _metadataState.ContentDistributor = v))
                {
                    UpdateChangeState(nameof(ContentDistributor), value ?? string.Empty);
                }
            }
        }

        public string Publisher
        {
            get => _metadataState.Publisher;
            set
            {
                if (SetStateProperty(nameof(Publisher), _metadataState.Publisher, value ?? string.Empty, v => _metadataState.Publisher = v))
                {
                    UpdateChangeState(nameof(Publisher), value ?? string.Empty);
                }
            }
        }

        public string Duration
        {
            get => _metadataState.Duration;
            set
            {
                if (SetStateProperty(nameof(Duration), _metadataState.Duration, value ?? string.Empty, v => _metadataState.Duration = v))
                {
                    DurationValue = DisplayFormatHelper.TryParseDuration(value);
                }
            }
        }

        public string FrameWidth
        {
            get => _metadataState.FrameWidth;
            set => SetStateProperty(nameof(FrameWidth), _metadataState.FrameWidth, value ?? string.Empty, v => _metadataState.FrameWidth = v);
        }

        public string FrameHeight
        {
            get => _metadataState.FrameHeight;
            set => SetStateProperty(nameof(FrameHeight), _metadataState.FrameHeight, value ?? string.Empty, v => _metadataState.FrameHeight = v);
        }

        public string FrameRate
        {
            get => _metadataState.FrameRate;
            set => SetStateProperty(nameof(FrameRate), _metadataState.FrameRate, value ?? string.Empty, v => _metadataState.FrameRate = v);
        }

        public string VideoBitrate
        {
            get => _metadataState.VideoBitrate;
            set => SetStateProperty(nameof(VideoBitrate), _metadataState.VideoBitrate, value ?? string.Empty, v => _metadataState.VideoBitrate = v);
        }

        public string VideoCompression
        {
            get => _metadataState.VideoCompression;
            set => SetStateProperty(nameof(VideoCompression), _metadataState.VideoCompression, value ?? string.Empty, v => _metadataState.VideoCompression = v);
        }

        public string AudioSampleRate
        {
            get => _metadataState.AudioSampleRate;
            set => SetStateProperty(nameof(AudioSampleRate), _metadataState.AudioSampleRate, value ?? string.Empty, v => _metadataState.AudioSampleRate = v);
        }

        public string AudioBitrate
        {
            get => _metadataState.AudioBitrate;
            set => SetStateProperty(nameof(AudioBitrate), _metadataState.AudioBitrate, value ?? string.Empty, v => _metadataState.AudioBitrate = v);
        }

        public string AudioFormat
        {
            get => _metadataState.AudioFormat;
            set => SetStateProperty(nameof(AudioFormat), _metadataState.AudioFormat, value ?? string.Empty, v => _metadataState.AudioFormat = v);
        }

        public string ReleaseDateText
        {
            get => _metadataState.ReleaseDateText;
            set => SetStateProperty(nameof(ReleaseDateText), _metadataState.ReleaseDateText, value ?? string.Empty, v => _metadataState.ReleaseDateText = v);
        }

        public DateTimeOffset ReleaseDate
        {
            get => _metadataState.ReleaseDate;
            set
            {
                var normalizedDate = value.Date;
                if (SetStateProperty(nameof(ReleaseDate), _metadataState.ReleaseDate, normalizedDate, v => _metadataState.ReleaseDate = v))
                {
                    _metadataState.ReleaseDateText = DisplayFormatHelper.FormatReleaseDateDisplayText(normalizedDate);
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
            get => _coverArtState.HasCoverArt;
            set
            {
                if (SetStateProperty(nameof(HasCoverArt), _coverArtState.HasCoverArt, value, v => _coverArtState.HasCoverArt = v))
                {
                    _coverArtState.HasCoverArt = value;
                    NotifyCoverArtPropertiesChanged();
                }
            }
        }

        public int SelectedCoverArtIndex
        {
            get => _coverArtState.SelectedIndex;
            set
            {
                if (SetStateProperty(nameof(SelectedCoverArtIndex), _coverArtState.SelectedIndex, value, v => _coverArtState.SelectedIndex = v))
                {
                    _coverArtState.SelectedIndex = value;
                    OnPropertyChanged(nameof(IsImageControlsEnabled));
                }
            }
        }

        public List<CoverArtImageData> CoverArtImages
        {
            get => _coverArtState.Images;
            set => _coverArtState.Images = value ?? new();
        }

        public bool IsCoverArtDirty
        {
            get => _coverArtState.IsDirty;
            set
            {
                if (SetStateProperty(nameof(IsCoverArtDirty), _coverArtState.IsDirty, value, v => _coverArtState.IsDirty = v))
                {
                    _coverArtState.IsDirty = value;
                    OnPropertyChanged(nameof(HasCoverArtChangesNotify));
                }
            }
        }

        public bool IsDeleted
        {
            get => _fileInfoState.IsDeleted;
            set
            {
                SetStateProperty(nameof(IsDeleted), _fileInfoState.IsDeleted, value, v => _fileInfoState.IsDeleted = v);
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
                || CoverArtImages.Count != _coverArtState.OriginalImageCount
                || CalculatePictureBytesHash(CoverArtImages) != _coverArtState.OriginalImageHash;
        }

        public bool HasCoverArtChangesNotify => HasCoverArtChanges();

        public bool HasMetadataChangesNotify => HasMetadataChanges();

        public bool HasMetadataChanges()
        {
            return _changeTrackingState.DirtyCustomFieldIds.Count > 0
                || !MetadataChangeComparer.AreEquivalent(
                    CreateCurrentMetadataSnapshot(),
                    CreateOriginalMetadataSnapshot());
        }

        public bool HasPendingChanges => HasMetadataChanges() || HasCoverArtChanges();

        public void ResetMetadataChangeTracking()
        {
            _changeTrackingState.IsApplyingInitialValues = true;
            try
            {
                    _changeTrackingState.OriginalValues[nameof(Title)] = Title;
                    _changeTrackingState.OriginalValues[nameof(Participants)] = Participants;
                    _changeTrackingState.OriginalValues[nameof(Category)] = Category;
                    _changeTrackingState.OriginalValues[nameof(Comment)] = Comment;
                    _changeTrackingState.OriginalValues[nameof(CatalogNumber)] = CatalogNumber;
                    _changeTrackingState.OriginalValues[nameof(Rating)] = Rating;
                    _changeTrackingState.OriginalValues[nameof(ContentDistributor)] = ContentDistributor;
                    _changeTrackingState.OriginalValues[nameof(Publisher)] = Publisher;
                    _changeTrackingState.OriginalValues[nameof(ReleaseDate)] = ReleaseDate.ToString("yyyy-MM-dd");
                    _changeTrackingState.OriginalStructuredMetadata = StructuredMetadata.Clone();
                    _changeTrackingState.OriginalStructuredMetadataJson = MetadataChangeComparer.SerializeCanonical(_changeTrackingState.OriginalStructuredMetadata);

                HasPendingChangesNotify();
            }
            finally { _changeTrackingState.IsApplyingInitialValues = false; }
            ResetCustomMetadataChanges();
        }

        public void ResetCoverArtChangeTracking()
        {
            _coverArtState.OriginalImageCount = CoverArtImages.Count;
            _coverArtState.OriginalImageHash = CalculatePictureBytesHash(CoverArtImages);
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
                nameof(Title) => Title,
                nameof(Participants) => Participants,
                nameof(Category) => Category,
                nameof(Comment) => Comment,
                nameof(CatalogNumber) => CatalogNumber,
                nameof(Rating) => Rating,
                nameof(ContentDistributor) => ContentDistributor,
                nameof(Publisher) => Publisher,
                nameof(ReleaseDate) => ReleaseDate.ToString("yyyy-MM-dd"),
                _ => string.Empty,
            };

            var originalValue = _changeTrackingState.OriginalValues.TryGetValue(propertyName, out var storedValue)
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

            _changeTrackingState.IsApplyingInitialValues = true;
            try
            {
                _changeTrackingState.OriginalValues[nameof(Title)] = snapshot.Title ?? string.Empty;
                _changeTrackingState.OriginalValues[nameof(Participants)] = snapshot.Participants ?? string.Empty;
                _changeTrackingState.OriginalValues[nameof(Category)] = snapshot.Category ?? string.Empty;
                _changeTrackingState.OriginalValues[nameof(Comment)] = snapshot.Comment ?? string.Empty;
                _changeTrackingState.OriginalValues[nameof(CatalogNumber)] = snapshot.CatalogNumber ?? string.Empty;
                _changeTrackingState.OriginalValues[nameof(Rating)] = snapshot.Rating ?? string.Empty;
                _changeTrackingState.OriginalValues[nameof(ContentDistributor)] = snapshot.ContentDistributor ?? string.Empty;
                _changeTrackingState.OriginalValues[nameof(Publisher)] = snapshot.Publisher ?? string.Empty;
                _changeTrackingState.OriginalValues[nameof(ReleaseDate)] = snapshot.ReleaseDate.ToString("yyyy-MM-dd");
                _changeTrackingState.OriginalStructuredMetadata = snapshot.StructuredMetadata?.Clone() ?? new MetadataDocument();
                _changeTrackingState.OriginalStructuredMetadataJson = MetadataChangeComparer.SerializeCanonical(_changeTrackingState.OriginalStructuredMetadata);
                HasPendingChangesNotify();
            }
            finally { _changeTrackingState.IsApplyingInitialValues = false; }
        }

        public bool HasMetadataChangesComparedTo(VideoMetadataSnapshot snapshot)
        {
            return !MetadataChangeComparer.AreEquivalent(CreateCurrentMetadataSnapshot(), snapshot);
        }

        /// <summary>
        /// 画面互換用の単純項目に含まれない、サブタイトル・入手元などの変更を検出します。
        /// これを行わないと、保存前の再読み込み判定で「変更なし」と誤判定されます。
        /// </summary>
        public bool HasStructuredMetadataChangesComparedTo(MetadataDocument? original)
        {
            return !string.Equals(
                MetadataChangeComparer.SerializeCanonical(StructuredMetadata),
                MetadataChangeComparer.SerializeCanonical(original),
                StringComparison.Ordinal);
        }

        private static string SerializeStructuredMetadata(MetadataDocument? document)
            => MetadataChangeComparer.SerializeCanonical(document);

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
            if (_changeTrackingState.IsApplyingInitialValues)
            {
                return;
            }

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
