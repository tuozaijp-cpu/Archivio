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
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();
        var adapter = EmbeddedMetadataFormatAdapterSelector.Select(path);
        var candidateFields = new List<string>();

        AddChanged(candidateFields, requested, "Title", metadata.Title, originalMetadata?.Title);
        AddChanged(candidateFields, requested, "Participants", metadata.Participants, originalMetadata?.Participants);
        AddChanged(candidateFields, requested, "Comment", metadata.Comment, originalMetadata?.Comment);
        AddChanged(candidateFields, requested, "Category", metadata.Category, originalMetadata?.Category);
        AddChanged(candidateFields, requested, "CatalogNumber", metadata.CatalogNumber, originalMetadata?.CatalogNumber);
        AddChanged(candidateFields, requested, "Rating", metadata.Rating, originalMetadata?.Rating);
        AddChanged(candidateFields, requested, "ContentDistributor", metadata.ContentDistributor, originalMetadata?.ContentDistributor);
        AddChanged(candidateFields, requested, "Publisher", metadata.Publisher, originalMetadata?.Publisher);
        if (requested.Contains("ReleaseDate") && ReleaseDateChanged(metadata.ReleaseDate, originalMetadata?.ReleaseDate))
        {
            candidateFields.Add("ReleaseDate");
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
                return new MetadataWriteResult { Errors = errors };
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
            adapter.WriteMetadataDocument(tagFile, document, fieldIds);

            var payloadFieldsChanged = fieldIds
                .Any(fieldId =>
                    fieldId != MetadataFieldIds.ReleaseDate
                    && adapter.GetFieldCapability(fieldId) == MetadataFieldCapability.ArchivioCustom);
            var payloadChanged = false;
            if (payloadFieldsChanged || participantsChanged)
            {
                var payload = ArchivioMetadataPayloadMapper.FromDocument(document);
                var existingPayload = adapter.ReadArchivioMetadata(tagFile).Payload;
                if (existingPayload is null)
                {
                    payloadChanged = payloadFieldsChanged;
                }
                else
                {
                    var existingDocument = adapter.ReadMetadataDocument(tagFile);
                    ArchivioMetadataPayloadMapper.ApplyToDocument(existingDocument, existingPayload);
                    var existingEffectivePayload = ArchivioMetadataPayloadMapper.FromDocument(existingDocument);
                    payloadChanged = !string.Equals(
                        ArchivioMetadataPayloadMapper.Serialize(payload),
                        ArchivioMetadataPayloadMapper.Serialize(existingEffectivePayload),
                        StringComparison.Ordinal);
                }

                if (payloadChanged)
                {
                    try
                    {
                        adapter.WriteArchivioMetadata(tagFile, payload);
                        payloadToVerify = payload;
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Error("Archivio独自メタデータの保存に失敗しました", ex, workingPath);
                        errors.Add(ex.Message);
                    }
                }
            }

            var nativeTagFieldsChanged = fieldIds.Any(fieldId =>
                MetadataFieldIds.StandardTagIds.Contains(fieldId)
                || fieldId is MetadataFieldIds.ReleaseDate or MetadataFieldIds.Rating);
            if (errors.Count == 0 && (nativeTagFieldsChanged || payloadChanged))
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
            Errors = errors
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
            var matches = field switch
            {
                "Title" => string.Equals(actual.Title, expected.Title, StringComparison.Ordinal),
                "Participants" => string.Equals(actual.Participants, expected.Participants, StringComparison.Ordinal),
                "Comment" => string.Equals(actual.Comment, expected.Comment, StringComparison.Ordinal),
                "Category" => string.Equals(actual.Category, expected.Category, StringComparison.Ordinal),
                "CatalogNumber" => string.Equals(actual.CatalogNumber, expected.CatalogNumber, StringComparison.Ordinal),
                "Publisher" => string.Equals(actual.Publisher, expected.Publisher, StringComparison.Ordinal),
                "ContentDistributor" => string.Equals(actual.ContentDistributor, expected.ContentDistributor, StringComparison.Ordinal),
                "Rating" => string.Equals(actual.Rating, expected.Rating, StringComparison.Ordinal),
                "ReleaseDate" or "dates.release" => actual.ReleaseDate.Date == expected.ReleaseDate.Date,
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

internal sealed class FileCommitService : IDisposable
{
    private readonly string _targetPath;
    private readonly bool _directWrite;
    private readonly FileVersion _originalVersion;
    private bool _committed;

    private FileCommitService(string targetPath, bool directWrite, FileVersion originalVersion, string workingPath)
    {
        _targetPath = targetPath;
        _directWrite = directWrite;
        _originalVersion = originalVersion;
        WorkingPath = workingPath;
    }

    public string WorkingPath { get; }
    public bool IsDirectWrite => _directWrite;

    public static FileCommitService Open(string targetPath, bool directWrite)
    {
        var version = ReadVersion(targetPath);
        if (directWrite)
        {
            return new FileCommitService(targetPath, true, version, targetPath);
        }

        var directory = Path.GetDirectoryName(targetPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("対象ファイルの保存先を特定できません。");
        }

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileNameWithoutExtension(targetPath)}.archivio-{Guid.NewGuid():N}{Path.GetExtension(targetPath)}");
        System.IO.File.Copy(targetPath, temporaryPath, overwrite: false);
        return new FileCommitService(targetPath, false, version, temporaryPath);
    }

    public void Commit()
    {
        if (_directWrite)
        {
            _committed = true;
            return;
        }

        var current = ReadVersion(_targetPath);
        if (current != _originalVersion)
        {
            throw new InvalidOperationException("保存中に外部アプリケーションがファイルを変更したため、上書きしませんでした。再読み込みしてください。");
        }

        System.IO.File.Replace(WorkingPath, _targetPath, null, ignoreMetadataErrors: true);
        _committed = true;
    }

    public void Dispose()
    {
        if (_directWrite || _committed)
        {
            if (!_directWrite && System.IO.File.Exists(WorkingPath))
            {
                TryDelete(WorkingPath);
            }
            return;
        }

        TryDelete(WorkingPath);
    }

    private static FileVersion ReadVersion(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new FileNotFoundException("対象ファイルが存在しません。", path);
        }

        return new FileVersion(info.Length, info.LastWriteTimeUtc);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        }
        catch (Exception ex)
        {
            AppLogger.Error("保存用一時ファイルの削除に失敗しました", ex, path);
        }
    }

    private readonly record struct FileVersion(long Length, DateTime LastWriteTimeUtc);
}

internal static class MetadataSaveResultBuilder
{
    public static VideoMetadataOperationResult Build(
        MetadataSavePlan plan,
        IReadOnlyList<string> errors,
        IReadOnlyList<string> archivioErrors,
        VideoMetadataSnapshot? reloaded,
        bool succeeded)
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
            ReloadedMetadata = succeeded ? reloaded : null
        };
    }
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
            return new VideoMetadataOperationResult { Succeeded = true, Message = LanguageManager.GetString("Msg_NoChanges") };
        }

        var adapter = EmbeddedMetadataFormatAdapterSelector.Select(path);
        var errors = new List<string>();
        var archivioErrors = new List<string>();
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
                }
            }

            if (errors.Count == 0)
            {
                try
                {
                    commit.Commit();
                }
                catch (Exception ex) { errors.Add(LanguageManager.GetString("Msg_SaveError", ex.Message)); }
            }

            var succeeded = errors.Count == 0;
            return MetadataSaveResultBuilder.Build(
                plan,
                errors,
                archivioErrors,
                reloaded,
                succeeded);
        }
        catch (Exception ex)
        {
            AppLogger.Error("メタデータの保存に失敗しました", ex, path);
            errors.Add(LanguageManager.GetString("Msg_SaveError", ex.Message));
            return MetadataSaveResultBuilder.Build(plan, errors, archivioErrors, null, false);
        }
    }
}
