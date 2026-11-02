using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Archivio.Models;

namespace Archivio.ViewModels
{
    /// <summary>ファイル内に埋め込むArchivio独自ペイロードの変換と検証。</summary>
    internal static class ArchivioMetadataPayloadMapper
    {
        internal const int MaxPayloadBytes = 64 * 1024;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        internal static ArchivioMetadataPayload FromDocument(MetadataDocument document)
        {
            ArgumentNullException.ThrowIfNull(document);
            document.EnsureInitialized();
            return new ArchivioMetadataPayload
            {
                SchemaVersion = "2",
                OriginalTitle = document.Work.OriginalTitle ?? string.Empty,
                Subtitle = document.Work.Subtitle ?? string.Empty,
                AlternateTitles = document.Work.AlternateTitles ?? new(),
                Cast = document.PeopleAndStaff.Cast ?? new(),
                Series = document.Series ?? new(),
                ArchivioId = document.Identifiers.ArchivioId ?? string.Empty,
                SourceFileId = document.Identifiers.SourceFileId ?? string.Empty,
                Work = document.Work,
                PeopleAndStaff = document.PeopleAndStaff,
                Dates = document.Dates,
                Identifiers = document.Identifiers,
                Acquisition = document.Acquisition,
                Rights = document.Rights,
                Location = document.Location,
                Evaluation = document.Evaluation ?? new(),
                CustomFields = document.CustomFields ?? new(StringComparer.Ordinal)
            };
        }

        internal static void ApplyToDocument(MetadataDocument document, ArchivioMetadataPayload payload)
        {
            ArgumentNullException.ThrowIfNull(document);
            ArgumentNullException.ThrowIfNull(payload);
            document.EnsureInitialized();
            document.SchemaVersion = string.IsNullOrWhiteSpace(payload.SchemaVersion) ? "1" : payload.SchemaVersion;
            if (payload.SchemaVersion == "2")
            {
                var nativeWork = document.Work;
                var nativePeopleAndStaff = document.PeopleAndStaff;
                var nativeDates = document.Dates;
                var nativeEvaluation = document.Evaluation;
                var nativeIdentifiers = document.Identifiers;
                document.Work = payload.Work ?? new WorkMetadata();
                document.PeopleAndStaff = payload.PeopleAndStaff ?? new PeopleAndStaffMetadata();
                document.Dates = payload.Dates ?? new MetadataDates();
                document.Identifiers = payload.Identifiers ?? new IdentifierMetadata();
                document.Acquisition = payload.Acquisition ?? new AcquisitionMetadata();
                document.Rights = payload.Rights ?? new RightsMetadata();
                document.Location = payload.Location ?? new LocationMetadata();
                document.Evaluation = payload.Evaluation ?? new EvaluationMetadata();
                document.CustomFields = payload.CustomFields ?? new(StringComparer.Ordinal);

                document.Work.Title = nativeWork.Title;
                document.Work.Comment = nativeWork.Comment;
                document.Work.Genres = nativeWork.Genres;
                document.Identifiers.CatalogNumber = nativeIdentifiers.CatalogNumber;
                document.PeopleAndStaff.Publishers = nativePeopleAndStaff.Publishers;
                document.PeopleAndStaff.Distributors = nativePeopleAndStaff.Distributors;
                if (nativePeopleAndStaff.Cast.Count > 0)
                {
                    document.PeopleAndStaff.Cast = MetadataDocumentMapper.MergeCastCredits(
                        nativePeopleAndStaff.Cast
                            .OrderBy(credit => credit.Order)
                            .Select(credit => credit.PersonName),
                        document.PeopleAndStaff.Cast);
                }
                document.Dates.ReleaseDate = nativeDates.ReleaseDate;
                document.Evaluation.PersonalRating = nativeEvaluation.PersonalRating;
                document.Evaluation.PersonalRatingRaw = nativeEvaluation.PersonalRatingRaw;
                return;
            }
            document.Work.OriginalTitle = payload.OriginalTitle ?? string.Empty;
            document.Work.Subtitle = payload.Subtitle ?? string.Empty;
            document.Work.AlternateTitles = payload.AlternateTitles ?? new();
            document.PeopleAndStaff.Cast = payload.Cast ?? new();
            document.Series = payload.Series ?? new();
            document.Identifiers.ArchivioId = payload.ArchivioId ?? string.Empty;
            document.Identifiers.SourceFileId = payload.SourceFileId ?? string.Empty;
            document.Evaluation = payload.Evaluation ?? new();
            document.CustomFields = payload.CustomFields ?? new(StringComparer.Ordinal);
        }

        internal static string Serialize(ArchivioMetadataPayload payload)
        {
            ArgumentNullException.ThrowIfNull(payload);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
            if (bytes.Length > MaxPayloadBytes)
            {
                throw new InvalidDataException($"Archivio独自メタデータが上限({MaxPayloadBytes} bytes)を超えています。");
            }

            return Encoding.UTF8.GetString(bytes);
        }

        internal static ArchivioMetadataReadResult Deserialize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return new ArchivioMetadataReadResult { IsPresent = false };
            }

            try
            {
                var bytes = Encoding.UTF8.GetBytes(value);
                if (bytes.Length > MaxPayloadBytes)
                {
                    return new ArchivioMetadataReadResult
                    {
                        IsPresent = true,
                        Error = $"Archivio独自メタデータが上限({MaxPayloadBytes} bytes)を超えています。"
                    };
                }

                var payload = JsonSerializer.Deserialize<ArchivioMetadataPayload>(bytes, JsonOptions);
                if (payload is null || string.IsNullOrWhiteSpace(payload.SchemaVersion))
                {
                    return new ArchivioMetadataReadResult { IsPresent = true, Error = "Archivio独自メタデータのスキーマが不正です。" };
                }

                return new ArchivioMetadataReadResult { IsPresent = true, Payload = payload };
            }
            catch (JsonException ex)
            {
                return new ArchivioMetadataReadResult { IsPresent = true, Error = $"Archivio独自メタデータのJSONが破損しています: {ex.Message}" };
            }
            catch (Exception ex)
            {
                return new ArchivioMetadataReadResult { IsPresent = true, Error = $"Archivio独自メタデータを読み込めません: {ex.Message}" };
            }
        }

        internal static bool HasData(MetadataDocument document)
        {
            var payload = FromDocument(document);
            return !string.IsNullOrWhiteSpace(payload.Work.Title)
                || !string.IsNullOrWhiteSpace(payload.Work.Subtitle)
                || !string.IsNullOrWhiteSpace(payload.Work.Description)
                || payload.PeopleAndStaff.Cast.Count > 0
                || payload.PeopleAndStaff.Crew.Count > 0
                || payload.Dates.ReleaseDate is not null
                || !string.IsNullOrWhiteSpace(payload.OriginalTitle)
                || !string.IsNullOrWhiteSpace(payload.Subtitle)
                || payload.AlternateTitles.Count > 0
                || payload.Cast.Count > 0
                || !string.IsNullOrWhiteSpace(payload.Series.Name)
                || !string.IsNullOrWhiteSpace(payload.ArchivioId)
                || !string.IsNullOrWhiteSpace(payload.SourceFileId)
                || payload.Evaluation.ViewCount != 0
                || payload.Evaluation.IsFavorite
                || payload.Evaluation.IsWatched
                || payload.Evaluation.WatchedAt is not null
                || payload.Evaluation.LastPlayedAt is not null
                || payload.Evaluation.PlaybackPosition is not null
                || payload.Evaluation.ResumePosition is not null
                || payload.Evaluation.UserTags.Count > 0
                || payload.CustomFields.Count > 0;
        }

        internal static bool IsPayloadFieldId(string fieldId)
            => MetadataFieldIds.IsArchivioPayloadId(fieldId);
    }
}
