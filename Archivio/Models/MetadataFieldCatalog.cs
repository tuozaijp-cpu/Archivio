using System;
using System.Collections.Generic;
using System.Linq;

namespace Archivio.Models
{
    internal enum MetadataFieldCategory
    {
        File,
        Work,
        PeopleAndStaff,
        Series,
        Publication,
        Dates,
        Evaluation,
        Identifiers,
        Acquisition,
        Rights,
        Location,
        Artwork,
        Technical
    }

    internal enum MetadataFieldValueType
    {
        Status,
        Text,
        MultipleText,
        Date,
        Rating,
        FileSize,
        Duration,
        Bitrate,
        Integer,
        Decimal,
        Boolean,
        Currency,
        Url,
        Identifier,
        Person,
        PersonRole,
        Coordinates,
        Image,
        ImageCollection
    }

    internal sealed record MetadataFieldDefinition(
        string Id,
        string LocalizationKey,
        MetadataFieldCategory Category,
        MetadataFieldValueType ValueType,
        bool IsMultiple,
        bool IsEditable,
        bool IsAutomaticallyCollected,
        bool IsInListByDefault,
        bool IsInDetailsByDefault,
        string? ListBindingPath = null,
        string? DetailsBindingPath = null,
        string? ExistingColumnTag = null,
        double DefaultListWidth = 140)
    {
        public string? CustomDisplayName { get; init; }
        public string? CustomFieldId { get; init; }
        public bool IsFilterable { get; init; } = true;
        public bool IsGlobalSearchable { get; init; }
        public string? SortBindingPath { get; init; }
        public string? DetailsElementName { get; init; }

        /// <summary>Archivioとして入力を必須にする項目かどうか。</summary>
        public bool IsRequired { get; init; }

        /// <summary>項目定義がシステム管理の値で、ユーザー削除不可かどうか。</summary>
        public bool IsSystem { get; init; } = true;

        /// <summary>将来のカスタム項目と標準項目を識別するためのフラグ。</summary>
        public bool IsCustom { get; init; }
    }

    internal static class MetadataFieldCatalog
    {
        private static IReadOnlyList<MetadataFieldDefinition>? _cachedDefinitions;
        private static readonly IReadOnlyList<MetadataFieldDefinition> StandardFieldDefinitions =
            Array.AsReadOnly(new MetadataFieldDefinition[]
            {
                new("file.status", "Col_StatusText", MetadataFieldCategory.File, MetadataFieldValueType.Status,
                    false, false, true, true, false, "StatusText", ExistingColumnTag: "StatusText", DefaultListWidth: 80)
                    { IsFilterable = false },
                new("file.name", "Col_FileName", MetadataFieldCategory.File, MetadataFieldValueType.Text,
                    false, true, true, true, false, "FileName", ExistingColumnTag: "FileName", DefaultListWidth: 220)
                    { IsGlobalSearchable = true },
                new("file.size", "Col_FileSizeText", MetadataFieldCategory.File, MetadataFieldValueType.FileSize,
                    false, false, true, true, false, "FileSizeText", ExistingColumnTag: "FileSizeText", DefaultListWidth: 90)
                    { SortBindingPath = "FileSizeBytes" },
                new("work.title", "Col_Title", MetadataFieldCategory.Work, MetadataFieldValueType.Text,
                    false, true, false, true, true, "Title", "Title", "Title", 180)
                    { IsGlobalSearchable = true, DetailsElementName = "TitleTextBox", IsRequired = true },
                new("credits.participants", "Col_Participants", MetadataFieldCategory.PeopleAndStaff, MetadataFieldValueType.MultipleText,
                    true, true, false, true, true, "Participants", "Participants", "Participants", 180)
                    { IsGlobalSearchable = true, DetailsElementName = "ParticipantsTextBox", IsRequired = true },
                new("dates.release", "Col_ReleaseDateText", MetadataFieldCategory.Dates, MetadataFieldValueType.Date,
                    false, true, false, true, true, "ReleaseDateText", "ReleaseDate", "ReleaseDateText", 120)
                    { SortBindingPath = "ReleaseDate", DetailsElementName = "ReleaseDatePicker", IsRequired = true },
                new("identifiers.catalog-number", "Col_CatalogNumber", MetadataFieldCategory.Identifiers, MetadataFieldValueType.Identifier,
                    false, true, false, true, true, "CatalogNumber", "CatalogNumber", "CatalogNumber", 140)
                    { IsGlobalSearchable = true, DetailsElementName = "CatalogNumberTextBox", IsRequired = true },
                new("evaluation.rating", "Col_Rating", MetadataFieldCategory.Evaluation, MetadataFieldValueType.Rating,
                    false, true, false, true, true, "RatingStarsText", "RatingStarsIndex", "Rating", 120)
                    { SortBindingPath = "RatingStarsIndex", DetailsElementName = "RatingComboBox", IsRequired = true },
                new("publication.publisher", "Col_Publisher", MetadataFieldCategory.Publication, MetadataFieldValueType.Text,
                    false, true, false, true, true, "Publisher", "Publisher", "Publisher", 140)
                    { IsGlobalSearchable = true, DetailsElementName = "PublisherTextBox" },
                new("publication.label", "Col_ContentDistributor", MetadataFieldCategory.Publication, MetadataFieldValueType.Text,
                    false, true, false, true, true, "ContentDistributor", "ContentDistributor", "ContentDistributor", 140)
                    { IsGlobalSearchable = true, DetailsElementName = "ContentDistributorTextBox" },
                new("work.category", "Col_Category", MetadataFieldCategory.Work, MetadataFieldValueType.MultipleText,
                    true, true, false, true, true, "Category", "Category", "Category", 120)
                    { IsGlobalSearchable = true, DetailsElementName = "CategoryTextBox" },
                new("work.comment", "Col_Comment", MetadataFieldCategory.Work, MetadataFieldValueType.Text,
                    false, true, false, true, true, "Comment", "Comment", "Comment", 220)
                    { IsGlobalSearchable = true, DetailsElementName = "CommentTextBox" },
                new("technical.duration", "Col_Duration", MetadataFieldCategory.Technical, MetadataFieldValueType.Duration,
                    false, false, true, true, true, "Duration", "Duration", "Duration", 100)
                    { SortBindingPath = "DurationValue", DetailsElementName = "DurationTextBox" },
                new("technical.frame-width", "Col_FrameWidth", MetadataFieldCategory.Technical, MetadataFieldValueType.Integer,
                    false, false, true, true, true, "FrameWidth", "FrameWidth", "FrameWidth", 90)
                    { DetailsElementName = "FrameWidthTextBox" },
                new("technical.frame-height", "Col_FrameHeight", MetadataFieldCategory.Technical, MetadataFieldValueType.Integer,
                    false, false, true, true, true, "FrameHeight", "FrameHeight", "FrameHeight", 90)
                    { DetailsElementName = "FrameHeightTextBox" },
                new("technical.frame-rate", "Col_FrameRate", MetadataFieldCategory.Technical, MetadataFieldValueType.Decimal,
                    false, false, true, true, true, "FrameRate", "FrameRate", "FrameRate", 90)
                    { DetailsElementName = "FrameRateTextBox" },
                new("technical.video-bitrate", "Col_VideoBitrate", MetadataFieldCategory.Technical, MetadataFieldValueType.Bitrate,
                    false, false, true, true, true, "VideoBitrate", "VideoBitrate", "VideoBitrate", 110)
                    { DetailsElementName = "VideoBitrateTextBox" },
                new("technical.video-codec", "Col_VideoCompression", MetadataFieldCategory.Technical, MetadataFieldValueType.Text,
                    false, false, true, true, true, "VideoCompression", "VideoCompression", "VideoCompression", 120)
                    { DetailsElementName = "VideoCompressionTextBox" },
                new("technical.audio-sample-rate", "Col_AudioSampleRate", MetadataFieldCategory.Technical, MetadataFieldValueType.Integer,
                    false, false, true, true, true, "AudioSampleRate", "AudioSampleRate", "AudioSampleRate", 110)
                    { DetailsElementName = "AudioSampleRateTextBox" },
                new("technical.audio-bitrate", "Col_AudioBitrate", MetadataFieldCategory.Technical, MetadataFieldValueType.Bitrate,
                    false, false, true, true, true, "AudioBitrate", "AudioBitrate", "AudioBitrate", 110)
                    { DetailsElementName = "AudioBitrateTextBox" },
                new("technical.audio-codec", "Col_AudioFormat", MetadataFieldCategory.Technical, MetadataFieldValueType.Text,
                    false, false, true, true, true, "AudioFormat", "AudioFormat", "AudioFormat", 110)
                    { DetailsElementName = "AudioFormatTextBox" },
                new("artwork.cover", "Prop_CoverArt", MetadataFieldCategory.Artwork, MetadataFieldValueType.ImageCollection,
                    true, true, false, false, true, DetailsBindingPath: "CoverArts")
                    { DetailsElementName = "CoverArtPane" },
                new("artwork.thumbnail", "Prop_CoverArt", MetadataFieldCategory.Artwork, MetadataFieldValueType.Image,
                    false, false, true, true, false, "ThumbnailImage")
            });

        internal static IReadOnlyList<MetadataFieldDefinition> All
        {
            get
            {
                if (_cachedDefinitions is not null) return _cachedDefinitions;
                RefreshCustomFields(ViewModels.SettingsManager.LoadSettings().CustomMetadataFields);
                return _cachedDefinitions!;
            }
        }

        internal static void RefreshCustomFields(IEnumerable<CustomMetadataFieldDefinition>? custom)
        {
                var validCustomFields = new List<MetadataFieldDefinition>();
                var seenIds = StandardFieldDefinitions.Select(field => field.Id).ToHashSet(StringComparer.Ordinal);
                foreach (var definition in custom ?? Array.Empty<CustomMetadataFieldDefinition>())
                {
                    if (!TryCreateCustomField(definition, seenIds, out var field))
                    {
                        continue;
                    }

                    validCustomFields.Add(field);
                    seenIds.Add(field.Id);
                }

                _cachedDefinitions = StandardFieldDefinitions.Concat(validCustomFields).ToArray();
        }

        internal static IReadOnlyList<MetadataFieldDefinition> ListFields =>
            All.Where(definition => !string.IsNullOrWhiteSpace(definition.ListBindingPath) || definition.IsCustom).ToArray();

        internal static IReadOnlyList<MetadataFieldDefinition> DetailFields =>
            All.Where(definition => !string.IsNullOrWhiteSpace(definition.DetailsBindingPath)
                || !string.IsNullOrWhiteSpace(definition.DetailsElementName)
                || definition.IsCustom).ToArray();

        internal static IEnumerable<MetadataFieldDefinition> GetByCategory(MetadataFieldCategory category)
        {
            return All.Where(field => field.Category == category);
        }

        internal static MetadataFieldDefinition? FindById(string id)
        {
            return string.IsNullOrWhiteSpace(id) ? null : All.FirstOrDefault(field => field.Id == id);
        }

        internal static bool IsKnownId(string id)
        {
            return !string.IsNullOrWhiteSpace(id) && All.Any(field => field.Id == id);
        }

        internal static bool TryCreateCustomField(
            CustomMetadataFieldDefinition definition,
            ISet<string> existingIds,
            out MetadataFieldDefinition field)
        {
            field = null!;
            if (definition is null
                || string.IsNullOrWhiteSpace(definition.Id)
                || !definition.Id.StartsWith("custom.", StringComparison.Ordinal)
                || definition.Id.Length <= "custom.".Length
                || definition.Id.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_'))
                || !existingIds.Add(definition.Id)
                || string.IsNullOrWhiteSpace(definition.DisplayName)
                || !Enum.TryParse<MetadataFieldCategory>(definition.Category, true, out var category)
                || !Enum.TryParse<MetadataFieldValueType>(definition.ValueType, true, out var valueType)
                || valueType is not (MetadataFieldValueType.Text or MetadataFieldValueType.MultipleText or MetadataFieldValueType.Date
                    or MetadataFieldValueType.Integer or MetadataFieldValueType.Decimal or MetadataFieldValueType.Boolean or MetadataFieldValueType.Rating))
            {
                return false;
            }

            field = new MetadataFieldDefinition(
                definition.Id,
                definition.DisplayName,
                category,
                valueType,
                valueType == MetadataFieldValueType.MultipleText,
                definition.IsEditable,
                false,
                definition.IsInListByDefault,
                definition.IsInDetailsByDefault)
            {
                IsCustom = true,
                CustomDisplayName = definition.DisplayName,
                CustomFieldId = definition.Id,
                IsSystem = false,
                IsGlobalSearchable = valueType is MetadataFieldValueType.Text or MetadataFieldValueType.MultipleText
            };
            return true;
        }
    }
}
