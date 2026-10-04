using System;
using System.IO;
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
            return new ArchivioMetadataPayload
            {
                SchemaVersion = string.IsNullOrWhiteSpace(document.SchemaVersion) ? "1" : document.SchemaVersion,
                OriginalTitle = document.Work.OriginalTitle ?? string.Empty,
                Subtitle = document.Work.Subtitle ?? string.Empty,
                AlternateTitles = document.Work.AlternateTitles ?? new(),
                Cast = document.PeopleAndStaff.Cast ?? new(),
                Series = document.Series ?? new(),
                ArchivioId = document.Identifiers.ArchivioId ?? string.Empty,
                SourceFileId = document.Identifiers.SourceFileId ?? string.Empty,
                Evaluation = document.Evaluation ?? new(),
                CustomFields = document.CustomFields ?? new(StringComparer.Ordinal)
            };
        }

        internal static void ApplyToDocument(MetadataDocument document, ArchivioMetadataPayload payload)
        {
            ArgumentNullException.ThrowIfNull(document);
            ArgumentNullException.ThrowIfNull(payload);
            document.SchemaVersion = string.IsNullOrWhiteSpace(payload.SchemaVersion) ? "1" : payload.SchemaVersion;
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
            return !string.IsNullOrWhiteSpace(payload.OriginalTitle)
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
        {
            return fieldId is "work.original-title" or "work.subtitle" or "work.alternate-titles"
                or "credits.cast" or "series.name" or "series.season-number" or "series.episode-number"
                or "series.episode-title" or "series.episode-id" or "series.network"
                or "identifiers.archivio-id" or "identifiers.source-file-id"
                or "evaluation.view-count" or "evaluation.favorite" or "evaluation.watched"
                or "evaluation.watched-at" or "evaluation.playback-position"
                or "evaluation.resume-position" or "evaluation.user-tags"
                || fieldId.StartsWith("custom.", StringComparison.Ordinal);
        }
    }
}
