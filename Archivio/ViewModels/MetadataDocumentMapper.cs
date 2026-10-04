using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Archivio.Models;

namespace Archivio.ViewModels
{
    /// <summary>
    /// 共通MetadataDocumentと、既存の画面・保存処理用モデルを相互変換する互換レイヤー。
    /// </summary>
    public static class MetadataDocumentMapper
    {
        public static MetadataDocument FromVideoFileItem(VideoFileItem item)
        {
            ArgumentNullException.ThrowIfNull(item);
            var document = FromSnapshot(item.CreateCurrentMetadataSnapshot());

            document.Artwork.Assets = item.CoverArtImages
                .Select((image, index) => new ArtworkAsset
                {
                    Data = image.Data.ToArray(),
                    MimeType = image.MimeType,
                    Description = image.Description,
                    Role = image.Type.ToString(),
                    Order = index
                })
                .ToList();
            return document;
        }

        public static MetadataDocument FromSnapshot(VideoMetadataSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            // 埋め込みArchivioペイロードを読み込んだスナップショットでは、独自項目を保持したまま
            // 画面互換用の標準項目だけを更新する。
            var document = snapshot.StructuredMetadata ?? new MetadataDocument();
            document.Work.Title = snapshot.Title ?? string.Empty;
            document.Work.Comment = snapshot.Comment ?? string.Empty;
            document.Work.Genres = SplitValues(snapshot.Category);
            document.PeopleAndStaff.Cast = SplitValues(snapshot.Participants)
                .Select((name, index) => new CastCredit { PersonName = name, Order = index })
                .ToList();
            document.Identifiers.CatalogNumber = snapshot.CatalogNumber ?? string.Empty;
            document.PeopleAndStaff.Publishers = SplitValues(snapshot.Publisher);
            document.PeopleAndStaff.Distributors = SplitValues(snapshot.ContentDistributor);
            document.Dates.ReleaseDate = snapshot.ReleaseDate.Year > 1900 ? snapshot.ReleaseDate : null;
            document.Evaluation.PersonalRatingRaw = snapshot.Rating ?? string.Empty;
            document.Evaluation.PersonalRating = ParseRating(snapshot.Rating);
            return document;
        }

        public static VideoMetadataSnapshot ToSnapshot(MetadataDocument document)
        {
            ArgumentNullException.ThrowIfNull(document);
            var releaseDate = document.Dates.ReleaseDate ??
                new DateTimeOffset(1900, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var rating = !string.IsNullOrWhiteSpace(document.Evaluation.PersonalRatingRaw)
                ? document.Evaluation.PersonalRatingRaw
                : document.Evaluation.PersonalRating?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

            return new VideoMetadataSnapshot
            {
                Title = document.Work.Title,
                Participants = string.Join("; ", document.PeopleAndStaff.Cast
                    .OrderBy(credit => credit.Order)
                    .Select(credit => credit.PersonName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))),
                CatalogNumber = document.Identifiers.CatalogNumber,
                Category = string.Join("; ", document.Work.Genres),
                Rating = rating,
                ContentDistributor = string.Join("; ", document.PeopleAndStaff.Distributors),
                Publisher = string.Join("; ", document.PeopleAndStaff.Publishers),
                Comment = document.Work.Comment,
                ReleaseDate = releaseDate,
                ReleaseDateText = releaseDate.Year > 1900
                    ? releaseDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : string.Empty,
                StructuredMetadata = document
            };
        }

        public static void ApplyToVideoFileItem(VideoFileItem item, MetadataDocument document)
        {
            ArgumentNullException.ThrowIfNull(item);
            var snapshot = ToSnapshot(document);
            item.Title = snapshot.Title;
            item.Participants = snapshot.Participants;
            item.CatalogNumber = snapshot.CatalogNumber;
            item.Category = snapshot.Category;
            item.Rating = snapshot.Rating;
            item.ContentDistributor = snapshot.ContentDistributor;
            item.Publisher = snapshot.Publisher;
            item.Comment = snapshot.Comment;
            item.ReleaseDate = snapshot.ReleaseDate;
            item.StructuredMetadata = document;
        }

        private static int? ParseRating(string? value)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rating)
                ? rating
                : null;
        }

        private static List<string> SplitValues(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? new List<string>()
                : value.Split(new[] { ';', ',', '，', '；' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(item => item.Trim())
                    .Where(item => item.Length > 0)
                    .ToList();
        }
    }
}
