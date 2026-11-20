using System;
using System.Collections.Generic;
using System.Linq;
using Archivio.Models;
using TagLib;

namespace Archivio.ViewModels;

/// <summary>TagLib標準タグへの書き込みだけを担当する。</summary>
internal static class MetadataTagWriter
{
    internal static void Write(
        TagLib.File file,
        IEmbeddedMetadataFormatAdapter adapter,
        MetadataDocument document,
        IReadOnlyCollection<string> fieldIds)
    {
        adapter.WriteMetadataDocument(file, document, fieldIds);
    }
}

internal sealed class ArchivioPayloadWriteResult
{
    public ArchivioMetadataPayload? Payload { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public bool Changed { get; init; }
}

/// <summary>Archivio独自JSONの差分判定と書き込みを担当する。</summary>
internal static class ArchivioPayloadWriter
{
    internal static ArchivioPayloadWriteResult WriteIfChanged(
        TagLib.File file,
        IEmbeddedMetadataFormatAdapter adapter,
        MetadataDocument document,
        IReadOnlyCollection<string> fieldIds,
        bool participantsChanged,
        string workingPath)
    {
        var payloadFieldsChanged = fieldIds.Any(fieldId =>
            fieldId != MetadataFieldIds.ReleaseDate
            && adapter.GetFieldCapability(fieldId) == MetadataFieldCapability.ArchivioCustom);
        if (!payloadFieldsChanged && !participantsChanged)
        {
            return new ArchivioPayloadWriteResult();
        }

        var payload = ArchivioMetadataPayloadMapper.FromDocument(document);
        var existingPayload = adapter.ReadArchivioMetadata(file).Payload;
        var payloadChanged = existingPayload is null
            ? payloadFieldsChanged
            : false;
        if (existingPayload is not null)
        {
            var existingDocument = adapter.ReadMetadataDocument(file);
            ArchivioMetadataPayloadMapper.ApplyToDocument(existingDocument, existingPayload);
            var existingEffectivePayload = ArchivioMetadataPayloadMapper.FromDocument(existingDocument);
            payloadChanged = !string.Equals(
                ArchivioMetadataPayloadMapper.Serialize(payload),
                ArchivioMetadataPayloadMapper.Serialize(existingEffectivePayload),
                StringComparison.Ordinal);
        }

        if (!payloadChanged)
        {
            return new ArchivioPayloadWriteResult();
        }

        try
        {
            adapter.WriteArchivioMetadata(file, payload);
            return new ArchivioPayloadWriteResult { Payload = payload, Changed = true };
        }
        catch (Exception ex)
        {
            AppLogger.Error("Archivio独自メタデータの保存に失敗しました", ex, workingPath);
            return new ArchivioPayloadWriteResult { Errors = new[] { ex.Message } };
        }
    }
}
