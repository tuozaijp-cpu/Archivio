using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Archivio.Models;
using TagLib;
using Windows.Storage;

namespace Archivio.ViewModels;

internal sealed class MetadataSavePlan
{
    public IReadOnlyList<string> FieldsToPersist { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> UnsupportedFields { get; init; } = Array.Empty<string>();
    public IReadOnlyList<MetadataFieldCapabilityResult> Capabilities { get; init; } = Array.Empty<MetadataFieldCapabilityResult>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

internal static class MetadataChangePlanner
{
    public static MetadataSavePlan Create(
        string path,
        VideoMetadataSnapshot metadata,
        VideoMetadataSnapshot? originalMetadata,
        IEnumerable<string>? changedProperties)
    {
        var requested = changedProperties?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(ResolveFieldId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();
        var adapter = EmbeddedMetadataFormatAdapterSelector.Select(path);
        var candidateFields = new List<string>();

        AddChanged(candidateFields, requested, MetadataFieldIds.WorkTitle, metadata.Title, originalMetadata?.Title);
        AddChanged(candidateFields, requested, MetadataFieldIds.Participants, metadata.Participants, originalMetadata?.Participants);
        AddChanged(candidateFields, requested, MetadataFieldIds.Comment, metadata.Comment, originalMetadata?.Comment);
        AddChanged(candidateFields, requested, MetadataFieldIds.Category, metadata.Category, originalMetadata?.Category);
        AddChanged(candidateFields, requested, MetadataFieldIds.CatalogNumber, metadata.CatalogNumber, originalMetadata?.CatalogNumber);
        AddChanged(candidateFields, requested, MetadataFieldIds.Rating, metadata.Rating, originalMetadata?.Rating);
        AddChanged(candidateFields, requested, MetadataFieldIds.Label, metadata.ContentDistributor, originalMetadata?.ContentDistributor);
        AddChanged(candidateFields, requested, MetadataFieldIds.Publisher, metadata.Publisher, originalMetadata?.Publisher);
        if (requested.Contains(MetadataFieldIds.ReleaseDate) && ReleaseDateChanged(metadata.ReleaseDate, originalMetadata?.ReleaseDate))
        {
            candidateFields.Add(MetadataFieldIds.ReleaseDate);
        }

        candidateFields.AddRange(requested.Where(ArchivioMetadataPayloadMapper.IsPayloadFieldId));
        var capabilities = candidateFields
            .Select(field =>
            {
                var fieldId = ResolveFieldId(field);
                var support = adapter.GetFieldSupport(fieldId);
                return new MetadataFieldCapabilityResult
                {
                    FieldId = fieldId,
                    Capability = support.Capability,
                    CanRead = support.CanRead,
                    CanWrite = support.CanWrite,
                    FailureReason = support.FailureReason
                };
            })
            .ToList();
        var unsupported = capabilities
            .Where(value => !value.CanWrite)
            .Select(value => value.FieldId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var warnings = unsupported.Count == 0
            ? new List<string>()
            : new List<string>
            {
                LanguageManager.GetString(
                    "Msg_UnsupportedCustomMetadata",
                    Path.GetExtension(path),
                    string.Join("、", unsupported))
            };
        var writableIds = capabilities
            .Where(value => value.CanWrite)
            .Select(value => value.FieldId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new MetadataSavePlan
        {
            FieldsToPersist = candidateFields
                .Where(field => writableIds.Contains(ResolveFieldId(field)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            UnsupportedFields = unsupported,
            Capabilities = capabilities,
            Warnings = warnings
        };
    }

    private static void AddChanged(ICollection<string> fields, ICollection<string> requested, string field, string? current, string? original)
    {
        if (requested.Contains(field) && !string.Equals(current ?? string.Empty, original ?? string.Empty, StringComparison.Ordinal))
        {
            fields.Add(field);
        }
    }

    private static bool ReleaseDateChanged(DateTimeOffset current, DateTimeOffset? original)
        => original is null ? current.Year > 1900 : current.Date != original.Value.Date;

    internal static string ResolveFieldId(string propertyName)
        => MetadataFieldCatalog.ResolveId(propertyName);
}

internal sealed class MetadataWriteResult
{
    public ArchivioMetadataPayload? Payload { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public MetadataSaveErrorKind? ErrorKind { get; init; }
}

internal static class MetadataWriteExecutor
{
    public static MetadataWriteResult Execute(
        string workingPath,
        IEmbeddedMetadataFormatAdapter adapter,
        VideoMetadataSnapshot metadata,
        IReadOnlyCollection<string> fieldsToPersist)
    {
        var errors = new List<string>();
        ArchivioMetadataPayload? payloadToVerify = null;
        try
        {
            using var tagFile = TagLib.File.Create(workingPath);
            var tag = tagFile.Tag;
            if (tag is null)
            {
                errors.Add(LanguageManager.GetString("Msg_EmbeddedTagsUnavailable"));
                return new MetadataWriteResult { Errors = errors, ErrorKind = MetadataSaveErrorKind.Unknown };
            }

            var fieldIds = fieldsToPersist
                .Select(MetadataChangePlanner.ResolveFieldId)
                .ToArray();
            var participantsChanged = fieldIds.Contains(
                MetadataFieldIds.Participants,
                StringComparer.OrdinalIgnoreCase);
            var document = MetadataDocumentMapper.FromSnapshot(metadata);
            if (participantsChanged && string.IsNullOrWhiteSpace(metadata.Participants))
            {
                document.PeopleAndStaff.Cast.Clear();
            }
            MetadataTagWriter.Write(tagFile, adapter, document, fieldIds);
            var payloadResult = ArchivioPayloadWriter.WriteIfChanged(
                tagFile,
                adapter,
                document,
                fieldIds,
                participantsChanged,
                workingPath);
            payloadToVerify = payloadResult.Payload;
            errors.AddRange(payloadResult.Errors);

            var nativeTagFieldsChanged = fieldIds.Any(fieldId =>
                MetadataFieldIds.StandardTagIds.Contains(fieldId)
                || fieldId is MetadataFieldIds.ReleaseDate or MetadataFieldIds.Rating);
            if (errors.Count == 0 && (nativeTagFieldsChanged || payloadResult.Changed))
            {
                tagFile.Save();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("ファイル内タグの保存に失敗しました", ex, workingPath);
            errors.Add(LanguageManager.GetString("Msg_SaveError", ex.Message));
        }
        return new MetadataWriteResult
        {
            Payload = payloadToVerify,
            Errors = errors,
            ErrorKind = errors.Count > 0 ? MetadataSaveErrorKind.Unknown : null
        };
    }
}

internal static class MetadataVerificationService
{
    public static VideoMetadataSnapshot VerifyAndRead(
        string path,
        IEmbeddedMetadataFormatAdapter adapter,
        VideoMetadataSnapshot expected,
        IEnumerable<string> fields,
        ArchivioMetadataPayload? expectedPayload)
    {
        using var savedFile = TagLib.File.Create(path);
        var document = adapter.ReadMetadataDocument(savedFile);
        var archivioResult = adapter.ReadArchivioMetadata(savedFile);
        if (expectedPayload is not null)
        {
            if (archivioResult.Error is not null || archivioResult.Payload is null)
            {
                throw new InvalidDataException(archivioResult.Error ?? "Archivio独自メタデータを読み戻せません。");
            }

            var expectedJson = ArchivioMetadataPayloadMapper.Serialize(expectedPayload);
            var actualJson = ArchivioMetadataPayloadMapper.Serialize(archivioResult.Payload);
            if (!string.Equals(expectedJson, actualJson, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Archivio独自メタデータの保存後検証に失敗しました。");
            }
        }

        if (archivioResult.Payload is not null)
        {
            ArchivioMetadataPayloadMapper.ApplyToDocument(document, archivioResult.Payload);
        }

        var actual = MetadataDocumentMapper.ToSnapshot(document);
        foreach (var field in fields)
        {
            var fieldId = MetadataChangePlanner.ResolveFieldId(field);
            var matches = fieldId switch
            {
                MetadataFieldIds.WorkTitle => string.Equals(actual.Title, expected.Title, StringComparison.Ordinal),
                MetadataFieldIds.Participants => string.Equals(actual.Participants, expected.Participants, StringComparison.Ordinal),
                MetadataFieldIds.Comment => string.Equals(actual.Comment, expected.Comment, StringComparison.Ordinal),
                MetadataFieldIds.Category => string.Equals(actual.Category, expected.Category, StringComparison.Ordinal),
                MetadataFieldIds.CatalogNumber => string.Equals(actual.CatalogNumber, expected.CatalogNumber, StringComparison.Ordinal),
                MetadataFieldIds.Publisher => string.Equals(actual.Publisher, expected.Publisher, StringComparison.Ordinal),
                MetadataFieldIds.Label => string.Equals(actual.ContentDistributor, expected.ContentDistributor, StringComparison.Ordinal),
                MetadataFieldIds.Rating => string.Equals(actual.Rating, expected.Rating, StringComparison.Ordinal),
                MetadataFieldIds.ReleaseDate => actual.ReleaseDate.Date == expected.ReleaseDate.Date,
                _ => true
            };
            if (!matches)
            {
                throw new InvalidDataException($"{field} が保存後に一致しません。");
            }
        }

        actual.Duration = expected.Duration;
        actual.FrameWidth = expected.FrameWidth;
        actual.FrameHeight = expected.FrameHeight;
        actual.FrameRate = expected.FrameRate;
        actual.VideoBitrate = expected.VideoBitrate;
        actual.VideoCompression = expected.VideoCompression;
        actual.AudioSampleRate = expected.AudioSampleRate;
        actual.AudioBitrate = expected.AudioBitrate;
        actual.AudioFormat = expected.AudioFormat;
        actual.Technical = expected.Technical;
        actual.TechnicalInfo = expected.TechnicalInfo;

        return actual;
    }
}

internal static class MetadataSaveResultBuilder
{
    public static VideoMetadataOperationResult Build(
        MetadataSavePlan plan,
        IReadOnlyList<string> errors,
        IReadOnlyList<string> archivioErrors,
        VideoMetadataSnapshot? reloaded,
        bool succeeded,
        string targetPath = "",
        IReadOnlyList<MetadataSaveError>? typedErrors = null,
        bool originalFilePreserved = true)
    {
        var capabilities = plan.Capabilities;
        if (!succeeded)
        {
            capabilities = capabilities
                .Select(value => value.CanWrite
                    ? new MetadataFieldCapabilityResult
                    {
                        FieldId = value.FieldId,
                        Capability = MetadataFieldCapability.Failed,
                        CanRead = value.CanRead,
                        CanWrite = false,
                        FailureReason = errors.FirstOrDefault()
                    }
                    : value)
                .ToList();
        }

        var allErrors = plan.Warnings.Concat(archivioErrors).Concat(errors).ToList();
        var hasWarnings = plan.Warnings.Count > 0 || archivioErrors.Count > 0;
        var resultErrors = (typedErrors ?? Array.Empty<MetadataSaveError>()).ToList();
        resultErrors.AddRange(plan.UnsupportedFields.Select(field => new MetadataSaveError
        {
            Kind = MetadataSaveErrorKind.UnsupportedFormat,
            Message = plan.Warnings.FirstOrDefault() ?? "書き込み対象外の形式または項目です。",
            TargetPath = targetPath,
            OriginalFilePreserved = true,
            FieldIds = new[] { field }
        }));
        return new VideoMetadataOperationResult
        {
            Succeeded = succeeded,
            HasWarnings = succeeded && hasWarnings,
            Message = succeeded
                ? hasWarnings ? LanguageManager.GetString("Msg_PartialSaveSuccess") : LanguageManager.GetString("Msg_SaveSuccess")
                : LanguageManager.GetString("Msg_SaveFailed"),
            Details = allErrors.Count == 0 ? null : string.Join(Environment.NewLine, allErrors),
            SavedProperties = succeeded ? plan.FieldsToPersist : Array.Empty<string>(),
            FailedProperties = succeeded ? Array.Empty<string>() : plan.FieldsToPersist,
            UnsupportedProperties = plan.UnsupportedFields,
            FieldCapabilities = capabilities,
            ArchivioMetadataErrors = archivioErrors,
            ReloadedMetadata = succeeded ? reloaded : null,
            Errors = resultErrors,
            ErrorKind = resultErrors.FirstOrDefault()?.Kind,
            TargetPath = targetPath,
            OriginalFilePreserved = originalFilePreserved
        };
    }
}

internal static class MetadataSaveErrorFactory
{
    internal static MetadataSaveError FromException(
        Exception exception,
        string path,
        MetadataSaveErrorKind fallback,
        bool originalFilePreserved,
        IReadOnlyList<string>? fields = null)
    {
        var kind = exception switch
        {
            OperationCanceledException => MetadataSaveErrorKind.Cancelled,
            TimeoutException => MetadataSaveErrorKind.Timeout,
            IOException when exception.Message.Contains("外部", StringComparison.OrdinalIgnoreCase)
                => MetadataSaveErrorKind.ExternalModification,
            IOException => MetadataSaveErrorKind.FileLocked,
            UnauthorizedAccessException => MetadataSaveErrorKind.FileLocked,
            InvalidDataException when exception.Message.Contains("Archivio", StringComparison.OrdinalIgnoreCase)
                => MetadataSaveErrorKind.InvalidArchivioJson,
            _ => fallback
        };
        return new MetadataSaveError
        {
            Kind = kind,
            Message = exception.Message,
            TargetPath = path,
            OriginalFilePreserved = originalFilePreserved,
            FieldIds = fields ?? Array.Empty<string>()
        };
    }

    internal static MetadataSaveError Create(
        MetadataSaveErrorKind kind,
        string message,
        string path,
        bool originalFilePreserved,
        IReadOnlyList<string>? fields = null)
        => new()
        {
            Kind = kind,
            Message = message,
            TargetPath = path,
            OriginalFilePreserved = originalFilePreserved,
            FieldIds = fields ?? Array.Empty<string>()
        };
}

internal static class MetadataSaveCoordinator
{
    public static Task<VideoMetadataOperationResult> ExecuteAsync(
        StorageFile file,
        VideoMetadataSnapshot metadata,
        VideoMetadataSnapshot? originalMetadata,
        IEnumerable<string> changedProperties)
    {
        return Task.Run(() => Execute(
            file.Path,
            metadata,
            originalMetadata,
            changedProperties,
            SettingsManager.LoadSettings().UseDirectFileWriteMode));
    }

    internal static Task<VideoMetadataOperationResult> ExecuteAsync(
        string path,
        VideoMetadataSnapshot metadata,
        VideoMetadataSnapshot? originalMetadata,
        IEnumerable<string> changedProperties,
        bool directWrite)
        => Task.Run(() => Execute(path, metadata, originalMetadata, changedProperties, directWrite));

    private static VideoMetadataOperationResult Execute(
        string path,
        VideoMetadataSnapshot metadata,
        VideoMetadataSnapshot? originalMetadata,
        IEnumerable<string> changedProperties,
        bool directWrite)
    {
        var plan = MetadataChangePlanner.Create(path, metadata, originalMetadata, changedProperties);
        if (plan.FieldsToPersist.Count == 0)
        {
            if (plan.UnsupportedFields.Count > 0)
            {
                return MetadataSaveResultBuilder.Build(
                    plan,
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    null,
                    succeeded: true,
                    targetPath: path);
            }

            return new VideoMetadataOperationResult
            {
                Succeeded = true,
                Message = LanguageManager.GetString("Msg_NoChanges"),
                TargetPath = path,
                OriginalFilePreserved = true
            };
        }

        var adapter = EmbeddedMetadataFormatAdapterSelector.Select(path);
        var errors = new List<string>();
        var archivioErrors = new List<string>();
        var typedErrors = new List<MetadataSaveError>();
        VideoMetadataSnapshot? reloaded = null;
        var write = new MetadataWriteResult();
        try
        {
            using var commit = FileCommitService.Open(path, directWrite);
            write = MetadataWriteExecutor.Execute(
                commit.WorkingPath,
                adapter,
                metadata,
                plan.FieldsToPersist);

            archivioErrors.AddRange(write.Errors);
            if (write.Errors.Count > 0)
            {
                errors.AddRange(write.Errors);
                typedErrors.Add(MetadataSaveErrorFactory.Create(
                    write.ErrorKind ?? MetadataSaveErrorKind.InvalidArchivioJson,
                    string.Join(Environment.NewLine, write.Errors),
                    path,
                    originalFilePreserved: !directWrite,
                    plan.FieldsToPersist));
            }
            else
            {
                try
                {
                    reloaded = MetadataVerificationService.VerifyAndRead(
                        commit.WorkingPath,
                        adapter,
                        metadata,
                        plan.FieldsToPersist,
                        write.Payload);
                }
                catch (Exception ex)
                {
                    errors.Add(LanguageManager.GetString("Msg_VerifyMetadataFailed", ex.Message));
                    typedErrors.Add(MetadataSaveErrorFactory.FromException(
                        ex,
                        path,
                        ex is InvalidDataException && ex.Message.Contains("Archivio", StringComparison.OrdinalIgnoreCase)
                            ? MetadataSaveErrorKind.InvalidArchivioJson
                            : MetadataSaveErrorKind.VerificationFailed,
                        originalFilePreserved: !directWrite,
                        plan.FieldsToPersist));
                }
            }

            if (errors.Count == 0)
            {
                try
                {
                    commit.Commit();
                }
                catch (Exception ex)
                {
                    errors.Add(LanguageManager.GetString("Msg_SaveError", ex.Message));
                    typedErrors.Add(MetadataSaveErrorFactory.FromException(
                        ex,
                        path,
                        ex.Message.Contains("外部", StringComparison.OrdinalIgnoreCase)
                            ? MetadataSaveErrorKind.ExternalModification
                            : MetadataSaveErrorKind.CommitFailed,
                        originalFilePreserved: !directWrite,
                        plan.FieldsToPersist));
                }
            }

            var succeeded = errors.Count == 0;
            return MetadataSaveResultBuilder.Build(
                plan,
                errors,
                archivioErrors,
                reloaded,
                succeeded,
                path,
                typedErrors,
                originalFilePreserved: succeeded || !directWrite);
        }
        catch (Exception ex)
        {
            AppLogger.Error("メタデータの保存に失敗しました", ex, path);
            errors.Add(LanguageManager.GetString("Msg_SaveError", ex.Message));
            typedErrors.Add(MetadataSaveErrorFactory.FromException(
                ex,
                path,
                MetadataSaveErrorKind.Unknown,
                originalFilePreserved: !directWrite,
                plan.FieldsToPersist));
            return MetadataSaveResultBuilder.Build(plan, errors, archivioErrors, null, false, path, typedErrors, !directWrite);
        }
    }
}
