using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Archivio.Models;
using Archivio.ViewModels;
using TagLib;
using Windows.Storage;
using IoFile = System.IO.File;
using static Archivio.CompatibilityTests.TestAssertions;
using static Archivio.CompatibilityTests.TestFixture;
using static Archivio.CompatibilityTests.TestProcessRunner;

namespace Archivio.CompatibilityTests;

internal static class Mp4MkvIntegrationTests
{
internal static void CreateFixtures()
    {
        var mp4 = Path.Combine(TestFixture.Root, "sample.mp4");
        var mkv = Path.Combine(TestFixture.Root, "sample.mkv");
        var mp3 = Path.Combine(TestFixture.Root, "sample.mp3");
        RunFfmpeg("-f", "lavfi", "-i", "color=c=blue:s=96x64:r=12:d=1", "-f", "lavfi", "-i", "sine=frequency=440:duration=1", "-c:v", "mpeg4", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", mp4);
        RunFfmpeg("-f", "lavfi", "-i", "color=c=green:s=96x64:r=12:d=1", "-f", "lavfi", "-i", "sine=frequency=440:duration=1", "-f", "lavfi", "-i", "sine=frequency=660:duration=1", "-map", "0:v", "-map", "1:a", "-map", "2:a", "-c:v", "mpeg4", "-pix_fmt", "yuv420p", "-c:a", "libvorbis", "-shortest", mkv);
        RunFfmpeg("-f", "lavfi", "-i", "sine=frequency=440:duration=1", "-c:a", "libmp3lame", mp3);
        Assert(IoFile.Exists(mp4) && IoFile.Exists(mkv) && IoFile.Exists(mp3), "fixture files were not created");
    }

internal static void TestMp4RoundTrip() => TestContainerRoundTrip("sample.mp4");

internal static void TestMkvRoundTrip() => TestContainerRoundTrip("sample.mkv");

internal static void TestVerificationPreservesTechnicalInformation()
    {
        var path = CopyFixture("sample.mp4");
        var technicalInfo = new VideoTechnicalInfo
        {
            Duration = TimeSpan.FromSeconds(42),
            TotalBitrate = 1_234_000,
            VideoStreams =
            [
                new VideoStreamInfo { Width = 1920, Height = 1080, Codec = "h264" }
            ],
            AudioStreams =
            [
                new AudioStreamInfo { SampleRate = 48000, Codec = "aac" }
            ]
        };
        var expected = new VideoMetadataSnapshot
        {
            Duration = "00:00:42",
            FrameWidth = "1920",
            FrameHeight = "1080",
            FrameRate = "29.97",
            VideoBitrate = "1.2 Mbps",
            VideoCompression = "H.264",
            AudioSampleRate = "48 kHz",
            AudioBitrate = "192 kbps",
            AudioFormat = "AAC",
            Technical = new VideoTechnicalMetadata { FrameWidth = 1920, FrameHeight = 1080 },
            TechnicalInfo = technicalInfo
        };
        var actual = MetadataVerificationService.VerifyAndRead(
            path,
            EmbeddedMetadataFormatAdapterSelector.Select(path),
            expected,
            Array.Empty<string>(),
            null);

        Assert(actual.Duration == expected.Duration, "duration was cleared by save verification");
        Assert(actual.FrameWidth == expected.FrameWidth && actual.FrameHeight == expected.FrameHeight, "video dimensions were cleared by save verification");
        Assert(actual.FrameRate == expected.FrameRate && actual.VideoBitrate == expected.VideoBitrate, "video technical data was cleared by save verification");
        Assert(actual.AudioSampleRate == expected.AudioSampleRate && actual.AudioBitrate == expected.AudioBitrate, "audio technical data was cleared by save verification");
        Assert(actual.AudioFormat == expected.AudioFormat && actual.TechnicalInfo.PrimaryVideo?.Codec == "h264", "technical stream information was not preserved");
    }

internal static void TestMetadataSaveModes()
    {
        foreach (var fixture in new[] { "sample.mp4", "sample.mkv" })
        {
            foreach (var directWrite in new[] { false, true })
            {
                var path = CopyFixture(fixture);
                var adapter = EmbeddedMetadataFormatAdapterSelector.Select(path);
                using (var media = TagLib.File.Create(path))
                {
                    media.Tag.Album = "Preserved album tag";
                    var document = adapter.ReadMetadataDocument(media);
                    document.Work.Subtitle = "Keep this existing payload";
                    adapter.WriteArchivioMetadata(media, ArchivioMetadataPayloadMapper.FromDocument(document));
                    media.Save();
                }

                VideoMetadataSnapshot before;
                string payloadBefore;
                using (var media = TagLib.File.Create(path))
                {
                    var document = adapter.ReadMetadataDocument(media);
                    var payload = adapter.ReadArchivioMetadata(media).Payload
                        ?? throw new InvalidOperationException($"{fixture}: initial payload was missing");
                    ArchivioMetadataPayloadMapper.ApplyToDocument(document, payload);
                    before = MetadataDocumentMapper.ToSnapshot(document);
                    payloadBefore = ArchivioMetadataPayloadMapper.Serialize(payload);
                }

                var updated = MetadataDocumentMapper.ToSnapshot(before.StructuredMetadata.Clone());
                updated.Title = $"Saved in {(directWrite ? "direct" : "backup")} mode";
                var result = MetadataSaveCoordinator.ExecuteAsync(
                    path,
                    updated,
                    before,
                    new[] { "Title" },
                    directWrite).GetAwaiter().GetResult();
                Assert(result.Succeeded, $"{fixture}, direct={directWrite}: {result.Details}");

                using var saved = TagLib.File.Create(path);
                Assert(saved.Tag.Title == updated.Title, $"{fixture}, direct={directWrite}: title was not saved");
                Assert(saved.Tag.Album == "Preserved album tag", $"{fixture}, direct={directWrite}: unrelated tag was damaged");
                var savedPayload = adapter.ReadArchivioMetadata(saved).Payload
                    ?? throw new InvalidOperationException($"{fixture}, direct={directWrite}: payload was lost");
                Assert(
                    ArchivioMetadataPayloadMapper.Serialize(savedPayload) == payloadBefore,
                    $"{fixture}, direct={directWrite}: unchanged custom payload was altered");
            }
        }
    }

internal static void TestStandardOnlySaveKeepsPayload()
    {
        foreach (var fixture in new[] { "sample.mp4", "sample.mkv" })
        {
            var path = CopyFixture(fixture);
            var adapter = EmbeddedMetadataFormatAdapterSelector.Select(path);
            var initial = new VideoMetadataSnapshot();
            initial.StructuredMetadata.Work.Subtitle = "Keep this payload value";
            initial.StructuredMetadata.PeopleAndStaff.Cast =
            [
                new CastCredit { PersonName = "Actor One", CharacterName = "Hero", IsLead = true, Order = 0 }
            ];
            var createPayload = MetadataWriteExecutor.Execute(path, adapter, initial, new[] { "work.subtitle" });
            Assert(createPayload.Errors.Count == 0, $"{fixture}: initial payload save failed: {string.Join("; ", createPayload.Errors)}");

            string ReadPayload()
            {
                using var media = TagLib.File.Create(path);
                var result = adapter.ReadArchivioMetadata(media);
                Assert(result.Payload is not null, $"{fixture}: Archivio payload was missing");
                return JsonSerializer.Serialize(result.Payload);
            }

            VideoMetadataSnapshot ReadSnapshot()
            {
                using var media = TagLib.File.Create(path);
                var document = adapter.ReadMetadataDocument(media);
                var result = adapter.ReadArchivioMetadata(media);
                if (result.Payload is not null)
                {
                    ArchivioMetadataPayloadMapper.ApplyToDocument(document, result.Payload);
                }
                return MetadataDocumentMapper.ToSnapshot(document);
            }

            var unchangedSnapshot = ReadSnapshot();
            var unchangedBytes = IoFile.ReadAllBytes(path);
            var unchangedSave = MetadataWriteExecutor.Execute(
                path,
                adapter,
                unchangedSnapshot,
                new[] { "work.subtitle" });
            Assert(unchangedSave.Errors.Count == 0, $"{fixture}: unchanged payload save failed");
            Assert(unchangedSave.Payload is null, $"{fixture}: unchanged payload was marked for verification");
            Assert(unchangedBytes.SequenceEqual(IoFile.ReadAllBytes(path)), $"{fixture}: unchanged payload rewrote the media file");

            var originalPayload = ReadPayload();
            var original = ReadSnapshot();
            var changed = MetadataDocumentMapper.ToSnapshot(MetadataDocumentMapper.FromSnapshot(original));
            changed.Title = "Updated native title";
            var save = MetadataWriteExecutor.Execute(path, adapter, changed, new[] { "Title" });
            Assert(save.Errors.Count == 0, $"{fixture}: standard-only save failed: {string.Join("; ", save.Errors)}");
            Assert(ReadPayload() == originalPayload, $"{fixture}: standard-only save rewrote the Archivio payload");

            var reloaded = ReadSnapshot();
            Assert(reloaded.Title == changed.Title, $"{fixture}: native title did not override the unchanged payload");
            Assert(reloaded.StructuredMetadata.Work.Subtitle == "Keep this payload value", $"{fixture}: unchanged payload metadata was lost");
            Assert(reloaded.StructuredMetadata.PeopleAndStaff.Cast.Single().CharacterName == "Hero", $"{fixture}: unchanged cast details were lost");

            var clearParticipants = MetadataDocumentMapper.ToSnapshot(MetadataDocumentMapper.FromSnapshot(reloaded));
            clearParticipants.Participants = string.Empty;
            var clear = MetadataWriteExecutor.Execute(path, adapter, clearParticipants, new[] { "Participants" });
            Assert(clear.Errors.Count == 0, $"{fixture}: clearing participants failed: {string.Join("; ", clear.Errors)}");
            var cleared = ReadSnapshot();
            Assert(cleared.Participants.Length == 0, $"{fixture}: cleared native participants reappeared from the old payload");
            Assert(cleared.StructuredMetadata.PeopleAndStaff.Cast.Count == 0, $"{fixture}: cleared cast remained in the payload");
        }
    }

internal static void TestMultipleImages()
    {
        var path = CopyFixture("sample.mp4");
        var file = GetStorageFile(path);
        var store = new EmbeddedVideoMetadataStore();
        var images = new[]
        {
            new CoverArtImageData { Data = new byte[] { 1, 2, 3, 4 }, MimeType = "image/jpeg", Description = "front", Type = PictureType.FrontCover },
            new CoverArtImageData { Data = new byte[] { 5, 6, 7, 8 }, MimeType = "image/png", Description = "back", Type = PictureType.BackCover }
        };
        var result = store.SaveCoverArtAsync(file, images).GetAwaiter().GetResult();
        Assert(result.Succeeded, $"multiple image save failed: {result.Details}");
        Assert(result.ReloadedCoverArtImages.Count == 2, "multiple images were not reread");
    }

internal static void TestCoverArtExport()
    {
        var source = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3, 4 };
        var destinationPath = Path.Combine(TestFixture.Root, "exported-cover.png");
        var destination = GetStorageFile(destinationPath, createIfMissing: true);
        new CoverArtExportService().ExportAsync(destination, source).GetAwaiter().GetResult();

        Assert(IoFile.ReadAllBytes(destinationPath).SequenceEqual(source),
            "cover art export did not preserve the original image bytes");
    }

internal static void TestCorruptCustomPayload()
    {
        var path = CopyFixture("sample.mp4");
        using (var media = TagLib.File.Create(path))
        {
            var apple = media.GetTag(TagTypes.Apple, create: true) as TagLib.Mpeg4.AppleTag
                ?? throw new InvalidOperationException("Apple tag unavailable");
            apple.SetDashBox("com.archivio", "Metadata", "{broken json");
            media.Tag.Title = "Still readable";
            media.Save();
        }

        var result = new EmbeddedVideoMetadataStore().LoadAsync(GetStorageFile(path)).GetAwaiter().GetResult();
        Assert(result.Metadata.Title == "Still readable", "standard tags were lost on corrupt custom JSON");
        Assert(!string.IsNullOrWhiteSpace(result.ArchivioMetadataError), "corruption was not reported");
    }

internal static void TestTypedMetadataSaveErrors()
    {
        var path = Path.Combine(TestFixture.Root, "typed-error.mp4");
        var invalidJson = MetadataSaveErrorFactory.FromException(
            new InvalidDataException("Archivio独自メタデータが不正です。"),
            path,
            MetadataSaveErrorKind.VerificationFailed,
            originalFilePreserved: true);
        Assert(invalidJson.Kind == MetadataSaveErrorKind.InvalidArchivioJson
            && invalidJson.TargetPath == path
            && invalidJson.OriginalFilePreserved,
            "invalid Archivio JSON was not classified with recovery information");

        var external = MetadataSaveErrorFactory.FromException(
            new IOException("外部アプリケーションがファイルを変更しました。"),
            path,
            MetadataSaveErrorKind.CommitFailed,
            originalFilePreserved: true);
        Assert(external.Kind == MetadataSaveErrorKind.ExternalModification,
            "external modification was not classified as a typed save error");

        var locked = MetadataSaveErrorFactory.FromException(
            new IOException("ファイルを使用中です。"),
            path,
            MetadataSaveErrorKind.CommitFailed,
            originalFilePreserved: true);
        Assert(locked.Kind == MetadataSaveErrorKind.FileLocked,
            "file lock was not classified as a typed save error");
    }

internal static void TestTechnicalStreams()
    {
        var previous = Environment.GetEnvironmentVariable("FFPROBE_PATH");
        Environment.SetEnvironmentVariable("FFPROBE_PATH", "ffprobe");
        try
        {
            var result = new MediaProbeService().ProbeAsync(GetStorageFile(Path.Combine(TestFixture.Root, "sample.mkv"))).GetAwaiter().GetResult();
            Assert(result.UsedFfprobe, "FFprobe was not used");
            Assert(result.Technical.AudioStreams.Count == 2, $"expected two audio streams, got {result.Technical.AudioStreams.Count}");
            Assert(result.Technical.VideoStreams.Count == 1, "video stream missing");
        }
        finally { Environment.SetEnvironmentVariable("FFPROBE_PATH", previous); }
    }

internal static void TestContainerRoundTrip(string fixtureName)
    {
        var path = CopyFixture(fixtureName);
        var adapter = EmbeddedMetadataFormatAdapterSelector.Select(path);
        Assert(adapter.GetFieldSupport("work.title").CanWrite, "standard title should be writable");
        MetadataFieldCatalog.RefreshCustomFields(new[]
        {
            new CustomMetadataFieldDefinition { Id = "custom.test-note", DisplayName = "Test note", ValueType = "Text", Category = "Work", IsEditable = true }
        });

        using (var media = TagLib.File.Create(path))
        {
            media.Tag.Album = "Preserved album tag";
            media.Save();
        }

        var storageFile = GetStorageFile(path);
        var store = new EmbeddedVideoMetadataStore();
        var beforeBytes = IoFile.ReadAllBytes(path);
        var before = store.LoadAsync(storageFile).GetAwaiter().GetResult().Metadata;
        var snapshot = MetadataDocumentMapper.ToSnapshot(MetadataDocumentMapper.FromSnapshot(before));
        snapshot.Title = "Archivio roundtrip 漢字";
        snapshot.Comment = "line one\r\nline two — ✓";
        snapshot.StructuredMetadata.Work.OriginalTitle = "Original title";
        snapshot.StructuredMetadata.Work.Subtitle = "Subtitle value";
        snapshot.StructuredMetadata.Work.AlternateTitles = new() { "Alias A", "Alias B" };
        snapshot.StructuredMetadata.PeopleAndStaff.Cast = new()
        {
            new CastCredit { PersonName = "Actor One", CharacterName = "Hero", IsLead = true, Order = 0 },
            new CastCredit { PersonName = "Actor Two", CharacterName = "Friend", Order = 1 }
        };
        snapshot.StructuredMetadata.Series.Name = "Series";
        snapshot.StructuredMetadata.Series.SeasonNumber = 2;
        snapshot.StructuredMetadata.Series.EpisodeNumber = 4;
        snapshot.StructuredMetadata.Acquisition.Source = "Acquisition source";
        snapshot.StructuredMetadata.CustomFields["custom.test-note"] = new MetadataValue { Kind = MetadataValueKind.Text, Text = "custom value" };

        var save = store.SaveAsync(storageFile, snapshot, before, new[]
        {
            "Title", "Comment", "work.subtitle", "series.name", "series.season-number",
            "series.episode-number", "acquisition.source", "custom.test-note"
        }).GetAwaiter().GetResult();
        Assert(save.Succeeded, $"save failed: {save.Details}");
        var after = store.LoadAsync(storageFile).GetAwaiter().GetResult().Metadata;
        Assert(!beforeBytes.SequenceEqual(IoFile.ReadAllBytes(path)),
            "successful metadata save did not change the file bytes");
        Assert(after.Title == snapshot.Title, "standard title was not restored");
        Assert(after.Comment == snapshot.Comment, "standard comment/newlines were not restored");
        Assert(after.StructuredMetadata.Work.OriginalTitle == "Original title", "custom original title missing");
        Assert(after.StructuredMetadata.Work.Subtitle == "Subtitle value", "subtitle was not persisted");
        Assert(after.StructuredMetadata.Work.AlternateTitles.SequenceEqual(new[] { "Alias A", "Alias B" }), "alternate titles missing");
        Assert(after.StructuredMetadata.PeopleAndStaff.Cast.Count == 2
            && after.StructuredMetadata.PeopleAndStaff.Cast[0].CharacterName == "Hero"
            && after.StructuredMetadata.PeopleAndStaff.Cast[0].IsLead, "cast credits/roles missing");
        Assert(after.StructuredMetadata.Series.EpisodeNumber == 4, "series values missing");
        Assert(after.StructuredMetadata.Acquisition.Source == "Acquisition source", "acquisition source was not persisted");
        Assert(after.StructuredMetadata.CustomFields["custom.test-note"].Text == "custom value", "custom value missing");
        using var check = TagLib.File.Create(path);
        Assert(check.Tag.Album == "Preserved album tag", "unrelated standard tag was damaged");
    }
}

