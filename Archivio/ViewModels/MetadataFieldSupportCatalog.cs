using Archivio.Models;

namespace Archivio.ViewModels;

internal enum MetadataContainerFormat
{
    Mp4,
    Mkv,
    Other
}

internal sealed record MetadataFormatCapabilities(
    string FieldId,
    MetadataFieldCapability Capability,
    bool CanRead,
    bool CanWrite,
    bool IsArchivioPayload,
    bool IsStandardTag);

/// <summary>
/// The single format capability table used by adapters and persistence planning.
/// </summary>
internal static class MetadataFieldSupportCatalog
{
    internal static MetadataFormatCapabilities Get(string fieldId, MetadataContainerFormat format)
    {
        var field = MetadataFieldCatalog.FindById(fieldId);
        if (field is null)
        {
            return Unsupported(fieldId);
        }

        var readOnly = field.Category == MetadataFieldCategory.Technical
            || field.Id == "artwork.thumbnail"
            || field.Category == MetadataFieldCategory.File && field.Id != "file.name"
            || field.IsCustom && !field.IsEditable;
        var standard = MetadataFieldIds.StandardTagIds.Contains(fieldId);
        var payload = MetadataFieldIds.IsArchivioPayloadId(fieldId);

        if (readOnly)
        {
            return new MetadataFormatCapabilities(fieldId, MetadataFieldCapability.ReadOnly, true, false, payload, standard);
        }

        if (format == MetadataContainerFormat.Other)
        {
            return standard
                ? new MetadataFormatCapabilities(fieldId, MetadataFieldCapability.ReadOnly, true, false, false, true)
                : Unsupported(fieldId);
        }

        if (standard)
        {
            return new MetadataFormatCapabilities(fieldId, MetadataFieldCapability.Native, true, true, false, true);
        }

        if (payload || field.IsCustom)
        {
            return new MetadataFormatCapabilities(fieldId, MetadataFieldCapability.ArchivioCustom, true, true, true, false);
        }

        return Unsupported(fieldId);
    }

    internal static MetadataFieldCapability GetCapability(string fieldId, MetadataContainerFormat format)
        => Get(fieldId, format).Capability;

    private static MetadataFormatCapabilities Unsupported(string fieldId)
        => new(fieldId, MetadataFieldCapability.Unsupported, false, false, false, false);
}
