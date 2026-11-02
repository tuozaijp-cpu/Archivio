using System;
using System.Collections.Generic;
using System.Linq;

namespace Archivio.Models
{
    /// <summary>
    /// 作品メタデータの共通表現。保存形式ではなくArchivioの安定した項目構造を表す。
    /// </summary>
    public sealed class MetadataDocument
    {
        public string SchemaVersion { get; set; } = "1";
        public WorkMetadata Work { get; set; } = new();
        public PeopleAndStaffMetadata PeopleAndStaff { get; set; } = new();
        public SeriesMetadata Series { get; set; } = new();
        public MetadataDates Dates { get; set; } = new();
        public EvaluationMetadata Evaluation { get; set; } = new();
        public IdentifierMetadata Identifiers { get; set; } = new();
        public AcquisitionMetadata Acquisition { get; set; } = new();
        public RightsMetadata Rights { get; set; } = new();
        public LocationMetadata Location { get; set; } = new();
        public ArtworkMetadata Artwork { get; set; } = new();

        /// <summary>標準項目に含まれない、将来のカスタム項目。</summary>
        public Dictionary<string, MetadataValue> CustomFields { get; set; } = new(StringComparer.Ordinal);

        /// <summary>
        /// Creates an independent document snapshot. Metadata documents are mutable because
        /// they are bound to the editor, so snapshot boundaries must never share references.
        /// </summary>
        public MetadataDocument Clone()
        {
            EnsureInitialized();
            var clone = new MetadataDocument
            {
                SchemaVersion = SchemaVersion,
                Work = new WorkMetadata
                {
                    Title = Work.Title, Subtitle = Work.Subtitle, OriginalTitle = Work.OriginalTitle,
                    AlternateTitles = Work.AlternateTitles.ToList(), SortTitle = Work.SortTitle,
                    Description = Work.Description, Synopsis = Work.Synopsis, Comment = Work.Comment,
                    Keywords = Work.Keywords.ToList(), Genres = Work.Genres.ToList(), Subgenres = Work.Subgenres.ToList(),
                    ContentType = Work.ContentType, Moods = Work.Moods.ToList(), Themes = Work.Themes.ToList(),
                    OriginalMediaType = Work.OriginalMediaType, OriginalWork = Work.OriginalWork
                },
                PeopleAndStaff = new PeopleAndStaffMetadata
                {
                    Cast = PeopleAndStaff.Cast.Select(Clone).ToList(),
                    Crew = PeopleAndStaff.Crew.Select(Clone).ToList(),
                    Artists = PeopleAndStaff.Artists.ToList(), ProductionCompanies = PeopleAndStaff.ProductionCompanies.ToList(),
                    Distributors = PeopleAndStaff.Distributors.ToList(), Publishers = PeopleAndStaff.Publishers.ToList(),
                    Labels = PeopleAndStaff.Labels.ToList()
                },
                Series = new SeriesMetadata
                {
                    Name = Series.Name, SeasonNumber = Series.SeasonNumber, EpisodeNumber = Series.EpisodeNumber,
                    EpisodeTitle = Series.EpisodeTitle, EpisodeId = Series.EpisodeId, Network = Series.Network,
                    SeasonCount = Series.SeasonCount, EpisodeCount = Series.EpisodeCount, PublicationYear = Series.PublicationYear,
                    SeasonReleaseDate = Series.SeasonReleaseDate, PartNumber = Series.PartNumber, PartCount = Series.PartCount,
                    PartOffset = Series.PartOffset
                },
                Dates = new MetadataDates
                {
                    ReleaseDate = Dates.ReleaseDate, RecordingDate = Dates.RecordingDate, FilmingDate = Dates.FilmingDate,
                    DigitizedDate = Dates.DigitizedDate, EncodedAt = Dates.EncodedAt, TaggedAt = Dates.TaggedAt,
                    PurchasedAt = Dates.PurchasedAt, FileCreatedAt = Dates.FileCreatedAt,
                    ArchivioRegisteredAt = Dates.ArchivioRegisteredAt, ArchivioUpdatedAt = Dates.ArchivioUpdatedAt
                },
                Evaluation = new EvaluationMetadata
                {
                    PersonalRating = Evaluation.PersonalRating, PersonalRatingRaw = Evaluation.PersonalRatingRaw,
                    ContentRating = Evaluation.ContentRating, ViewCount = Evaluation.ViewCount,
                    IsFavorite = Evaluation.IsFavorite, IsWatched = Evaluation.IsWatched, WatchedAt = Evaluation.WatchedAt,
                    LastPlayedAt = Evaluation.LastPlayedAt, PlaybackPosition = Evaluation.PlaybackPosition,
                    ResumePosition = Evaluation.ResumePosition, PersonalNotes = Evaluation.PersonalNotes,
                    UserTags = Evaluation.UserTags.ToList()
                },
                Identifiers = new IdentifierMetadata
                {
                    CatalogNumber = Identifiers.CatalogNumber, Barcode = Identifiers.Barcode, Isbn = Identifiers.Isbn,
                    ImdbId = Identifiers.ImdbId, TmdbId = Identifiers.TmdbId, TvdbId = Identifiers.TvdbId,
                    Tvdb2Id = Identifiers.Tvdb2Id, Isrc = Identifiers.Isrc, Lccn = Identifiers.Lccn,
                    LabelCode = Identifiers.LabelCode, ArchivioId = Identifiers.ArchivioId, SourceFileId = Identifiers.SourceFileId
                },
                Acquisition = new AcquisitionMetadata
                {
                    Price = Acquisition.Price, Currency = Acquisition.Currency, Seller = Acquisition.Seller,
                    PurchaseUrl = Acquisition.PurchaseUrl, Buyer = Acquisition.Buyer, Source = Acquisition.Source,
                    TransactionType = Acquisition.TransactionType, OriginalMedia = Acquisition.OriginalMedia
                },
                Rights = new RightsMetadata
                {
                    CopyrightHolder = Rights.CopyrightHolder, ProductionCopyright = Rights.ProductionCopyright,
                    License = Rights.License, UsageTerms = Rights.UsageTerms, Disclaimer = Rights.Disclaimer,
                    Source = Rights.Source, OriginalWorkInformation = Rights.OriginalWorkInformation
                },
                Location = new LocationMetadata
                {
                    FilmingLocation = Location.FilmingLocation, ProductionLocation = Location.ProductionLocation,
                    Country = Location.Country, Region = Location.Region, Municipality = Location.Municipality,
                    Latitude = Location.Latitude, Longitude = Location.Longitude, Altitude = Location.Altitude
                },
                Artwork = new ArtworkMetadata { Assets = Artwork.Assets.Select(Clone).ToList() },
                CustomFields = CustomFields.ToDictionary(pair => pair.Key, pair => Clone(pair.Value), StringComparer.Ordinal)
            };
            clone.EnsureInitialized();
            return clone;
        }

        private static CastCredit Clone(CastCredit value) => new()
        {
            PersonName = value.PersonName, CharacterName = value.CharacterName, IsLead = value.IsLead, Order = value.Order
        };

        private static CrewCredit Clone(CrewCredit value) => new()
        {
            RoleId = value.RoleId, PersonName = value.PersonName, Department = value.Department, Order = value.Order
        };

        private static ArtworkAsset Clone(ArtworkAsset value) => new()
        {
            Data = value.Data?.ToArray() ?? Array.Empty<byte>(), MimeType = value.MimeType,
            Description = value.Description, Role = value.Role, Order = value.Order
        };

        private static MetadataValue Clone(MetadataValue value) => new()
        {
            Kind = value.Kind, Text = value.Text, TextValues = value.TextValues?.ToList() ?? new(),
            Date = value.Date, Integer = value.Integer, Decimal = value.Decimal, Rating = value.Rating,
            Boolean = value.Boolean, Images = value.Images?.Select(Clone).ToList() ?? new()
        };

        /// <summary>Restores defaults after reading old or partially populated JSON.</summary>
        public void EnsureInitialized()
        {
            Work ??= new WorkMetadata();
            PeopleAndStaff ??= new PeopleAndStaffMetadata();
            Series ??= new SeriesMetadata();
            Dates ??= new MetadataDates();
            Evaluation ??= new EvaluationMetadata();
            Identifiers ??= new IdentifierMetadata();
            Acquisition ??= new AcquisitionMetadata();
            Rights ??= new RightsMetadata();
            Location ??= new LocationMetadata();
            Artwork ??= new ArtworkMetadata();
            CustomFields ??= new Dictionary<string, MetadataValue>(StringComparer.Ordinal);

            Work.AlternateTitles ??= new();
            Work.Keywords ??= new();
            Work.Genres ??= new();
            Work.Subgenres ??= new();
            Work.Moods ??= new();
            Work.Themes ??= new();
            PeopleAndStaff.Cast ??= new();
            PeopleAndStaff.Crew ??= new();
            PeopleAndStaff.Artists ??= new();
            PeopleAndStaff.ProductionCompanies ??= new();
            PeopleAndStaff.Distributors ??= new();
            PeopleAndStaff.Publishers ??= new();
            PeopleAndStaff.Labels ??= new();
            Evaluation.UserTags ??= new();
            Artwork.Assets ??= new();
        }
    }

    public sealed class WorkMetadata
    {
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public string OriginalTitle { get; set; } = string.Empty;
        public List<string> AlternateTitles { get; set; } = new();
        public string SortTitle { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Synopsis { get; set; } = string.Empty;
        public string Comment { get; set; } = string.Empty;
        public List<string> Keywords { get; set; } = new();
        public List<string> Genres { get; set; } = new();
        public List<string> Subgenres { get; set; } = new();
        public string ContentType { get; set; } = string.Empty;
        public List<string> Moods { get; set; } = new();
        public List<string> Themes { get; set; } = new();
        public string OriginalMediaType { get; set; } = string.Empty;
        public string OriginalWork { get; set; } = string.Empty;
    }

    public sealed class PeopleAndStaffMetadata
    {
        public List<CastCredit> Cast { get; set; } = new();
        public List<CrewCredit> Crew { get; set; } = new();
        public List<string> Artists { get; set; } = new();
        public List<string> ProductionCompanies { get; set; } = new();
        public List<string> Distributors { get; set; } = new();
        public List<string> Publishers { get; set; } = new();
        public List<string> Labels { get; set; } = new();
    }

    public sealed class CastCredit
    {
        public string PersonName { get; set; } = string.Empty;
        public string CharacterName { get; set; } = string.Empty;
        public bool IsLead { get; set; }
        public int Order { get; set; }
    }

    public sealed class CrewCredit
    {
        /// <summary>監督、脚本家、プロデューサー等を表す安定した役割ID。</summary>
        public string RoleId { get; set; } = string.Empty;
        public string PersonName { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public int Order { get; set; }
    }

    public sealed class SeriesMetadata
    {
        public string Name { get; set; } = string.Empty;
        public int? SeasonNumber { get; set; }
        public int? EpisodeNumber { get; set; }
        public string EpisodeTitle { get; set; } = string.Empty;
        public string EpisodeId { get; set; } = string.Empty;
        public string Network { get; set; } = string.Empty;
        public int? SeasonCount { get; set; }
        public int? EpisodeCount { get; set; }
        public int? PublicationYear { get; set; }
        public DateTimeOffset? SeasonReleaseDate { get; set; }
        public int? PartNumber { get; set; }
        public int? PartCount { get; set; }
        public int? PartOffset { get; set; }
    }

    public sealed class MetadataDates
    {
        public DateTimeOffset? ReleaseDate { get; set; }
        public DateTimeOffset? RecordingDate { get; set; }
        public DateTimeOffset? FilmingDate { get; set; }
        public DateTimeOffset? DigitizedDate { get; set; }
        public DateTimeOffset? EncodedAt { get; set; }
        public DateTimeOffset? TaggedAt { get; set; }
        public DateTimeOffset? PurchasedAt { get; set; }
        public DateTimeOffset? FileCreatedAt { get; set; }
        public DateTimeOffset? ArchivioRegisteredAt { get; set; }
        public DateTimeOffset? ArchivioUpdatedAt { get; set; }
    }

    public sealed class EvaluationMetadata
    {
        public int? PersonalRating { get; set; }
        public string PersonalRatingRaw { get; set; } = string.Empty;
        public string ContentRating { get; set; } = string.Empty;
        public int ViewCount { get; set; }
        public bool IsFavorite { get; set; }
        public bool IsWatched { get; set; }
        public DateTimeOffset? WatchedAt { get; set; }
        public DateTimeOffset? LastPlayedAt { get; set; }
        public TimeSpan? PlaybackPosition { get; set; }
        public TimeSpan? ResumePosition { get; set; }
        public string PersonalNotes { get; set; } = string.Empty;
        public List<string> UserTags { get; set; } = new();
    }

    public sealed class IdentifierMetadata
    {
        public string CatalogNumber { get; set; } = string.Empty;
        public string Barcode { get; set; } = string.Empty;
        public string Isbn { get; set; } = string.Empty;
        public string ImdbId { get; set; } = string.Empty;
        public string TmdbId { get; set; } = string.Empty;
        public string TvdbId { get; set; } = string.Empty;
        public string Tvdb2Id { get; set; } = string.Empty;
        public string Isrc { get; set; } = string.Empty;
        public string Lccn { get; set; } = string.Empty;
        public string LabelCode { get; set; } = string.Empty;
        public string ArchivioId { get; set; } = string.Empty;
        public string SourceFileId { get; set; } = string.Empty;
    }

    public sealed class AcquisitionMetadata
    {
        public decimal? Price { get; set; }
        public string Currency { get; set; } = string.Empty;
        public string Seller { get; set; } = string.Empty;
        public string PurchaseUrl { get; set; } = string.Empty;
        public string Buyer { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string TransactionType { get; set; } = string.Empty;
        public string OriginalMedia { get; set; } = string.Empty;
    }

    public sealed class RightsMetadata
    {
        public string CopyrightHolder { get; set; } = string.Empty;
        public string ProductionCopyright { get; set; } = string.Empty;
        public string License { get; set; } = string.Empty;
        public string UsageTerms { get; set; } = string.Empty;
        public string Disclaimer { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string OriginalWorkInformation { get; set; } = string.Empty;
    }

    public sealed class LocationMetadata
    {
        public string FilmingLocation { get; set; } = string.Empty;
        public string ProductionLocation { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;
        public string Municipality { get; set; } = string.Empty;
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
        public decimal? Altitude { get; set; }
    }

    public sealed class ArtworkMetadata
    {
        public List<ArtworkAsset> Assets { get; set; } = new();
    }

    public sealed class ArtworkAsset
    {
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public string MimeType { get; set; } = "image/jpeg";
        public string Description { get; set; } = string.Empty;
        public string Role { get; set; } = "cover";
        public int Order { get; set; }
    }

    public enum MetadataValueKind
    {
        Text,
        MultipleText,
        Date,
        Integer,
        Decimal,
        Rating,
        Boolean,
        Image,
        ImageCollection,
        Url,
        Identifier
    }

    /// <summary>カスタム項目の値。値型を失わずに将来の項目を保持する。</summary>
    public sealed class MetadataValue
    {
        public MetadataValueKind Kind { get; set; } = MetadataValueKind.Text;
        public string? Text { get; set; }
        public List<string> TextValues { get; set; } = new();
        public DateTimeOffset? Date { get; set; }
        public long? Integer { get; set; }
        public decimal? Decimal { get; set; }
        public decimal? Rating { get; set; }
        public bool? Boolean { get; set; }
        public List<ArtworkAsset> Images { get; set; } = new();
    }
}
