using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Archivio.Models;
using TagLib;

namespace Archivio.ViewModels
{
    public enum MetadataFieldCapability
    {
        Native,
        ArchivioCustom,
        ReadOnly,
        Unsupported,
        Failed
    }

    /// <summary>旧実装との互換用。新規コードではMetadataFieldCapabilityを使用する。</summary>
    internal enum MetadataFieldStorageCapability
    {
        StandardTag = (int)MetadataFieldCapability.Native,
        ArchivioTag = (int)MetadataFieldCapability.ArchivioCustom,
        ReadOnly = (int)MetadataFieldCapability.ReadOnly,
        Unsupported = (int)MetadataFieldCapability.Unsupported
    }

    internal sealed record MetadataFieldSupport(
        string FieldId,
        MetadataFieldCapability Capability,
        bool CanRead,
        bool CanWrite,
        string? FailureReason = null)
    {
        public static MetadataFieldSupport FromCapability(string fieldId, MetadataFieldCapability capability)
        {
            return new MetadataFieldSupport(
                fieldId,
                capability,
                capability is MetadataFieldCapability.Native
                    or MetadataFieldCapability.ArchivioCustom
                    or MetadataFieldCapability.ReadOnly,
                capability is MetadataFieldCapability.Native
                    or MetadataFieldCapability.ArchivioCustom);
        }
    }

    internal interface IEmbeddedMetadataFormatAdapter
    {
        bool CanHandle(string extension);
        MetadataDocument ReadMetadataDocument(TagLib.File file);
        void WriteMetadataDocument(TagLib.File file, MetadataDocument document, IReadOnlyCollection<string> fieldIds);
        ArchivioMetadataReadResult ReadArchivioMetadata(TagLib.File file);
        void WriteArchivioMetadata(TagLib.File file, ArchivioMetadataPayload payload);
        MetadataFieldCapability GetFieldCapability(string fieldId);
        MetadataFieldSupport GetFieldSupport(string fieldId);
        // 既存呼び出しとの互換性を維持する。
        MetadataFieldStorageCapability GetCapability(string fieldId);
        string ReadArchivioText(TagLib.File file, string fieldId);
        void WriteArchivioText(TagLib.File file, string fieldId, string value);
        string ReadPublisher(TagLib.File file);
        void WritePublisher(TagLib.File file, string value);
    }

    internal abstract class EmbeddedMetadataFormatAdapter : IEmbeddedMetadataFormatAdapter
    {
        private static readonly HashSet<string> StandardFieldIds = new(StringComparer.Ordinal)
        {
            "work.title",
            "credits.participants",
            "work.comment",
            "work.category",
            "identifiers.catalog-number",
            "publication.publisher",
            "publication.label",
            "artwork.cover"
        };

        public abstract bool CanHandle(string extension);

        public virtual MetadataDocument ReadMetadataDocument(TagLib.File file)
        {
            var document = new MetadataDocument();
            var tag = file.Tag;
            if (tag is null)
            {
                return document;
            }

            document.Work.Title = tag.Title ?? string.Empty;
            document.Work.Comment = tag.Comment ?? string.Empty;
            document.Work.Genres = SplitValues(tag.Genres);
            document.PeopleAndStaff.Cast = SplitValues(tag.Performers)
                .Select((name, index) => new CastCredit { PersonName = name, Order = index })
                .ToList();
            document.Identifiers.CatalogNumber = tag.Grouping ?? string.Empty;
            document.PeopleAndStaff.Distributors = SplitValues(new[] { tag.Copyright ?? string.Empty });
            document.PeopleAndStaff.Publishers = SplitValues(new[] { ReadPublisher(file) });

            var releaseDate = ReadArchivioText(file, "dates.release");
            if (DateTimeOffset.TryParse(
                releaseDate,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var parsedReleaseDate))
            {
                document.Dates.ReleaseDate = parsedReleaseDate.Date;
            }
            else if (tag.Year > 0)
            {
                document.Dates.ReleaseDate = new DateTimeOffset((int)tag.Year, 1, 1, 0, 0, 0, TimeSpan.Zero);
            }

            document.Evaluation.PersonalRatingRaw = ReadArchivioText(file, "evaluation.rating");
            if (int.TryParse(document.Evaluation.PersonalRatingRaw, out var rating))
            {
                document.Evaluation.PersonalRating = rating;
            }

            return document;
        }

        public virtual void WriteMetadataDocument(
            TagLib.File file,
            MetadataDocument document,
            IReadOnlyCollection<string> fieldIds)
        {
            var tag = file.Tag ?? throw new NotSupportedException("ファイル内タグを作成できません。");
            var isDirty = false;

            if (fieldIds.Contains("work.title"))
            {
                tag.Title = document.Work.Title ?? string.Empty;
                isDirty = true;
            }

            if (fieldIds.Contains("credits.participants"))
            {
                tag.Performers = document.PeopleAndStaff.Cast
                    .OrderBy(credit => credit.Order)
                    .Select(credit => credit.PersonName)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToArray();
                isDirty = true;
            }

            if (fieldIds.Contains("work.comment"))
            {
                tag.Comment = document.Work.Comment ?? string.Empty;
                isDirty = true;
            }

            if (fieldIds.Contains("work.category"))
            {
                tag.Genres = document.Work.Genres
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToArray();
                isDirty = true;
            }

            if (fieldIds.Contains("identifiers.catalog-number"))
            {
                tag.Grouping = document.Identifiers.CatalogNumber ?? string.Empty;
                isDirty = true;
            }

            if (fieldIds.Contains("publication.publisher"))
            {
                WritePublisher(file, string.Join("; ", document.PeopleAndStaff.Publishers));
                isDirty = true;
            }

            if (fieldIds.Contains("publication.label"))
            {
                tag.Copyright = string.Join("; ", document.PeopleAndStaff.Distributors);
                isDirty = true;
            }

            if (fieldIds.Contains("dates.release"))
            {
                tag.Year = document.Dates.ReleaseDate is { } releaseDate && releaseDate.Year > 1900
                    ? (uint)releaseDate.Year
                    : 0;
                WriteArchivioText(file, "dates.release",
                    document.Dates.ReleaseDate is { } date && date.Year > 1900
                        ? date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
                        : string.Empty);
                isDirty = true;
            }

            if (fieldIds.Contains("evaluation.rating"))
            {
                WriteArchivioText(file, "evaluation.rating", document.Evaluation.PersonalRatingRaw ?? string.Empty);
                isDirty = true;
            }

            if (isDirty)
            {
                file.Save();
            }
        }

        public virtual ArchivioMetadataReadResult ReadArchivioMetadata(TagLib.File file)
        {
            return new ArchivioMetadataReadResult { IsPresent = false };
        }

        public virtual void WriteArchivioMetadata(TagLib.File file, ArchivioMetadataPayload payload)
        {
            throw new NotSupportedException("このコンテナ形式はArchivio独自メタデータに対応していません。");
        }

        public virtual MetadataFieldCapability GetFieldCapability(string fieldId)
        {
            var field = MetadataFieldCatalog.FindById(fieldId);
            if (field is null)
            {
                return MetadataFieldCapability.Unsupported;
            }

            if (field.Category == MetadataFieldCategory.Technical
                || field.Id == "artwork.thumbnail"
                || field.Category == MetadataFieldCategory.File && field.Id != "file.name")
            {
                return MetadataFieldCapability.ReadOnly;
            }

            return StandardFieldIds.Contains(fieldId)
                ? MetadataFieldCapability.Native
                : MetadataFieldCapability.Unsupported;
        }

        public virtual MetadataFieldSupport GetFieldSupport(string fieldId)
        {
            return MetadataFieldSupport.FromCapability(fieldId, GetFieldCapability(fieldId));
        }

        public virtual MetadataFieldStorageCapability GetCapability(string fieldId)
        {
            return GetFieldCapability(fieldId) switch
            {
                MetadataFieldCapability.Native => MetadataFieldStorageCapability.StandardTag,
                MetadataFieldCapability.ArchivioCustom => MetadataFieldStorageCapability.ArchivioTag,
                MetadataFieldCapability.ReadOnly => MetadataFieldStorageCapability.ReadOnly,
                _ => MetadataFieldStorageCapability.Unsupported
            };
        }

        public virtual string ReadArchivioText(TagLib.File file, string fieldId)
        {
            return string.Empty;
        }

        public virtual void WriteArchivioText(TagLib.File file, string fieldId, string value)
        {
            throw new NotSupportedException($"{fieldId} is not supported by this container.");
        }

        public virtual string ReadPublisher(TagLib.File file)
        {
            return file.Tag.Publisher ?? string.Empty;
        }

        public virtual void WritePublisher(TagLib.File file, string value)
        {
            file.Tag.Publisher = value;
        }

        protected static string? GetArchivioFieldName(string fieldId)
        {
            return fieldId switch
            {
                "evaluation.rating" => "Rating",
                "dates.release" => "ReleaseDate",
                _ => null
            };
        }

        private static List<string> SplitValues(IEnumerable<string>? values)
        {
            return values?
                .SelectMany(value => value.Split(new[] { ';', ',', '，', '；' }, StringSplitOptions.RemoveEmptyEntries))
                .Select(value => value.Trim())
                .Where(value => value.Length > 0)
                .ToList() ?? new List<string>();
        }
    }

    internal sealed class Mp4MetadataAdapter : EmbeddedMetadataFormatAdapter
    {
        private const string ArchivioTagOwner = "com.archivio";
        private const string ArchivioMetadataBoxName = "Metadata";

        private static readonly HashSet<string> ArchivioCustomFieldIds = new(StringComparer.Ordinal)
        {
            "work.original-title", "work.subtitle", "work.alternate-titles", "credits.cast",
            "series.name", "series.season-number", "series.episode-number", "series.episode-title",
            "series.episode-id", "series.network", "identifiers.archivio-id", "identifiers.source-file-id",
            "evaluation.view-count", "evaluation.favorite", "evaluation.watched", "evaluation.watched-at",
            "evaluation.playback-position", "evaluation.resume-position", "evaluation.user-tags"
        };

        public override bool CanHandle(string extension)
        {
            return extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".m4v", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".mov", StringComparison.OrdinalIgnoreCase);
        }

        public override MetadataFieldCapability GetFieldCapability(string fieldId)
        {
            if (MetadataFieldCatalog.FindById(fieldId) is { IsCustom: true, IsEditable: false })
            {
                return MetadataFieldCapability.ReadOnly;
            }

            return GetArchivioFieldName(fieldId) is not null || ArchivioCustomFieldIds.Contains(fieldId) || fieldId.StartsWith("custom.", StringComparison.Ordinal)
                ? MetadataFieldCapability.ArchivioCustom
                : base.GetFieldCapability(fieldId);
        }

        public override ArchivioMetadataReadResult ReadArchivioMetadata(TagLib.File file)
        {
            var raw = (file.GetTag(TagTypes.Apple, create: false) as TagLib.Mpeg4.AppleTag)
                ?.GetDashBox(ArchivioTagOwner, ArchivioMetadataBoxName);
            return ArchivioMetadataPayloadMapper.Deserialize(raw);
        }

        public override void WriteArchivioMetadata(TagLib.File file, ArchivioMetadataPayload payload)
        {
            var appleTag = file.GetTag(TagTypes.Apple, create: true) as TagLib.Mpeg4.AppleTag
                ?? throw new NotSupportedException("Apple タグを作成できません。");
            appleTag.SetDashBox(ArchivioTagOwner, ArchivioMetadataBoxName, ArchivioMetadataPayloadMapper.Serialize(payload));
            file.Save();
        }

        public override string ReadArchivioText(TagLib.File file, string fieldId)
        {
            var fieldName = GetArchivioFieldName(fieldId);
            return fieldName is null
                ? string.Empty
                : (file.GetTag(TagTypes.Apple, create: false) as TagLib.Mpeg4.AppleTag)
                    ?.GetDashBox(ArchivioTagOwner, fieldName) ?? string.Empty;
        }

        public override void WriteArchivioText(TagLib.File file, string fieldId, string value)
        {
            var fieldName = GetArchivioFieldName(fieldId)
                ?? throw new NotSupportedException($"{fieldId} is not supported by MP4.");
            var appleTag = file.GetTag(TagTypes.Apple, create: true) as TagLib.Mpeg4.AppleTag
                ?? throw new NotSupportedException("Apple タグを作成できません。");
            appleTag.SetDashBox(ArchivioTagOwner, fieldName, value);
        }
    }

    internal sealed class MatroskaMetadataAdapter : EmbeddedMetadataFormatAdapter
    {
        private const string ArchivioMetadataTagName = "ARCHIVIO_METADATA_JSON";

        private static readonly HashSet<string> ArchivioCustomFieldIds = new(StringComparer.Ordinal)
        {
            "work.original-title", "work.subtitle", "work.alternate-titles", "credits.cast",
            "series.name", "series.season-number", "series.episode-number", "series.episode-title",
            "series.episode-id", "series.network", "identifiers.archivio-id", "identifiers.source-file-id",
            "evaluation.view-count", "evaluation.favorite", "evaluation.watched", "evaluation.watched-at",
            "evaluation.playback-position", "evaluation.resume-position", "evaluation.user-tags"
        };
        public override bool CanHandle(string extension)
        {
            return extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".webm", StringComparison.OrdinalIgnoreCase);
        }

        public override MetadataFieldCapability GetFieldCapability(string fieldId)
        {
            if (MetadataFieldCatalog.FindById(fieldId) is { IsCustom: true, IsEditable: false })
            {
                return MetadataFieldCapability.ReadOnly;
            }

            return GetArchivioFieldName(fieldId) is not null || ArchivioCustomFieldIds.Contains(fieldId) || fieldId.StartsWith("custom.", StringComparison.Ordinal)
                ? MetadataFieldCapability.ArchivioCustom
                : base.GetFieldCapability(fieldId);
        }

        public override ArchivioMetadataReadResult ReadArchivioMetadata(TagLib.File file)
        {
            var tag = file.GetTag(TagTypes.Matroska, create: false) as TagLib.Matroska.Tag;
            var raw = tag?.Get(ArchivioMetadataTagName, null, true)?.FirstOrDefault();
            return ArchivioMetadataPayloadMapper.Deserialize(raw);
        }

        public override void WriteArchivioMetadata(TagLib.File file, ArchivioMetadataPayload payload)
        {
            var tag = file.GetTag(TagTypes.Matroska, create: true) as TagLib.Matroska.Tag
                ?? throw new NotSupportedException("Matroska タグを作成できません。");
            tag.Set(ArchivioMetadataTagName, null, ArchivioMetadataPayloadMapper.Serialize(payload));
            file.Save();
        }

        public override string ReadArchivioText(TagLib.File file, string fieldId)
        {
            var fieldName = GetArchivioFieldName(fieldId);
            if (fieldName is null)
            {
                return string.Empty;
            }

            var tag = file.GetTag(TagTypes.Matroska, create: false) as TagLib.Matroska.Tag;
            return tag?.Get($"ARCHIVIO_{fieldName.ToUpperInvariant()}", null, true)?.FirstOrDefault() ?? string.Empty;
        }

        public override void WriteArchivioText(TagLib.File file, string fieldId, string value)
        {
            var fieldName = GetArchivioFieldName(fieldId)
                ?? throw new NotSupportedException($"{fieldId} is not supported by Matroska.");
            var tag = file.GetTag(TagTypes.Matroska, create: true) as TagLib.Matroska.Tag
                ?? throw new NotSupportedException("Matroska タグを作成できません。");
            tag.Set($"ARCHIVIO_{fieldName.ToUpperInvariant()}", null, string.IsNullOrWhiteSpace(value) ? null : value);
        }

        public override string ReadPublisher(TagLib.File file)
        {
            var tag = file.GetTag(TagTypes.Matroska, create: false) as TagLib.Matroska.Tag;
            return tag?.Get("PUBLISHER", null, true)?.FirstOrDefault() ?? file.Tag.Publisher ?? string.Empty;
        }

        public override void WritePublisher(TagLib.File file, string value)
        {
            var tag = file.GetTag(TagTypes.Matroska, create: true) as TagLib.Matroska.Tag
                ?? throw new NotSupportedException("Matroska タグを作成できません。");
            tag.Set("PUBLISHER", null, string.IsNullOrWhiteSpace(value) ? null : value);
            file.Tag.Publisher = value;
        }
    }

    internal sealed class OtherMetadataAdapter : EmbeddedMetadataFormatAdapter
    {
        public override bool CanHandle(string extension)
        {
            return true;
        }

        public override MetadataFieldCapability GetFieldCapability(string fieldId)
        {
            var field = MetadataFieldCatalog.FindById(fieldId);
            if (field is null)
            {
                return MetadataFieldCapability.Unsupported;
            }

            // その他形式は標準タグを読めても、Archivioからの書き込みは行わない。
            return base.GetFieldCapability(fieldId) == MetadataFieldCapability.Native
                ? MetadataFieldCapability.ReadOnly
                : base.GetFieldCapability(fieldId);
        }
    }

    internal static class EmbeddedMetadataFormatAdapterSelector
    {
        private static readonly IEmbeddedMetadataFormatAdapter Mp4Adapter = new Mp4MetadataAdapter();
        private static readonly IEmbeddedMetadataFormatAdapter MatroskaAdapter = new MatroskaMetadataAdapter();
        private static readonly IEmbeddedMetadataFormatAdapter OtherAdapter = new OtherMetadataAdapter();

        public static IEmbeddedMetadataFormatAdapter Select(string filePath)
        {
            var extension = Path.GetExtension(filePath);
            if (Mp4Adapter.CanHandle(extension))
            {
                return Mp4Adapter;
            }

            if (MatroskaAdapter.CanHandle(extension))
            {
                return MatroskaAdapter;
            }

            return OtherAdapter;
        }
    }
}
