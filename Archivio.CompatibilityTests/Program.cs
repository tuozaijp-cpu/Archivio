using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Archivio.Models;
using Archivio.ViewModels;
using TagLib;
using Windows.Storage;
using IoFile = System.IO.File;

namespace Archivio.CompatibilityTests;

internal static class Program
{
    private static int _passed;
    private static int _failed;
    private static string _root = string.Empty;

    [STAThread]
    private static int Main()
    {
        _root = Path.Combine(Path.GetTempPath(), $"Archivio-compat-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        try
        {
            Run("create isolated media fixtures", CreateFixtures);
            Run("MP4 standard/custom metadata roundtrip", TestMp4RoundTrip);
            Run("MKV standard/custom metadata roundtrip", TestMkvRoundTrip);
            Run("other format reads but refuses writes", TestOtherFormatReadOnly);
            Run("multiple cover images roundtrip", TestMultipleImages);
            Run("broken custom payload leaves standard tags readable", TestCorruptCustomPayload);
            Run("technical probe retains multiple audio streams", TestTechnicalStreams);
            Run("legacy settings and snapshot migration", TestLegacyMigration);
            Run("unknown field IDs are ignored by the catalog", TestUnknownFieldIds);
            Run("external modifications are detected", TestExternalModificationDetection);
            Run("failed save leaves original bytes unchanged", TestFailedSavePreservesOriginal);
        }
        finally
        {
            try { Directory.Delete(_root, recursive: true); } catch { }
        }

        Console.WriteLine($"\nCompatibility checks: {_passed} passed, {_failed} failed.");
        return _failed == 0 ? 0 : 1;
    }

    private static void CreateFixtures()
    {
        var mp4 = Path.Combine(_root, "sample.mp4");
        var mkv = Path.Combine(_root, "sample.mkv");
        var mp3 = Path.Combine(_root, "sample.mp3");
        RunFfmpeg("-f", "lavfi", "-i", "color=c=blue:s=96x64:r=12:d=1", "-f", "lavfi", "-i", "sine=frequency=440:duration=1", "-c:v", "mpeg4", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", mp4);
        RunFfmpeg("-f", "lavfi", "-i", "color=c=green:s=96x64:r=12:d=1", "-f", "lavfi", "-i", "sine=frequency=440:duration=1", "-f", "lavfi", "-i", "sine=frequency=660:duration=1", "-map", "0:v", "-map", "1:a", "-map", "2:a", "-c:v", "mpeg4", "-pix_fmt", "yuv420p", "-c:a", "libvorbis", "-shortest", mkv);
        RunFfmpeg("-f", "lavfi", "-i", "sine=frequency=440:duration=1", "-c:a", "libmp3lame", mp3);
        Assert(IoFile.Exists(mp4) && IoFile.Exists(mkv) && IoFile.Exists(mp3), "fixture files were not created");
    }

    private static void TestMp4RoundTrip() => TestContainerRoundTrip("sample.mp4");
    private static void TestMkvRoundTrip() => TestContainerRoundTrip("sample.mkv");

    private static void TestContainerRoundTrip(string fixtureName)
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
        var before = store.LoadAsync(storageFile).GetAwaiter().GetResult().Metadata;
        var snapshot = before;
        snapshot.Title = "Archivio roundtrip 漢字";
        snapshot.Comment = "line one\r\nline two — ✓";
        snapshot.StructuredMetadata.Work.OriginalTitle = "Original title";
        snapshot.StructuredMetadata.Work.AlternateTitles = new() { "Alias A", "Alias B" };
        snapshot.StructuredMetadata.PeopleAndStaff.Cast = new()
        {
            new CastCredit { PersonName = "Actor One", CharacterName = "Hero", IsLead = true, Order = 0 },
            new CastCredit { PersonName = "Actor Two", CharacterName = "Friend", Order = 1 }
        };
        snapshot.StructuredMetadata.Series.Name = "Series";
        snapshot.StructuredMetadata.Series.SeasonNumber = 2;
        snapshot.StructuredMetadata.Series.EpisodeNumber = 4;
        snapshot.StructuredMetadata.CustomFields["custom.test-note"] = new MetadataValue { Kind = MetadataValueKind.Text, Text = "custom value" };

        var save = store.SaveAsync(storageFile, snapshot, before, new[] { "Title", "Comment", "custom.test-note" }).GetAwaiter().GetResult();
        Assert(save.Succeeded, $"save failed: {save.Details}");
        var after = store.LoadAsync(storageFile).GetAwaiter().GetResult().Metadata;
        Assert(after.Title == snapshot.Title, "standard title was not restored");
        Assert(after.Comment == snapshot.Comment, "standard comment/newlines were not restored");
        Assert(after.StructuredMetadata.Work.OriginalTitle == "Original title", "custom original title missing");
        Assert(after.StructuredMetadata.Work.AlternateTitles.SequenceEqual(new[] { "Alias A", "Alias B" }), "alternate titles missing");
        Assert(after.StructuredMetadata.PeopleAndStaff.Cast.Count == 2
            && after.StructuredMetadata.PeopleAndStaff.Cast[0].CharacterName == "Hero"
            && after.StructuredMetadata.PeopleAndStaff.Cast[0].IsLead, "cast credits/roles missing");
        Assert(after.StructuredMetadata.Series.EpisodeNumber == 4, "series values missing");
        Assert(after.StructuredMetadata.CustomFields["custom.test-note"].Text == "custom value", "custom value missing");
        using var check = TagLib.File.Create(path);
        Assert(check.Tag.Album == "Preserved album tag", "unrelated standard tag was damaged");
    }

    private static void TestOtherFormatReadOnly()
    {
        var path = CopyFixture("sample.mp3");
        var adapter = EmbeddedMetadataFormatAdapterSelector.Select(path);
        Assert(adapter.GetFieldSupport("work.title").CanRead, "other format should allow standard tag read");
        Assert(!adapter.GetFieldSupport("work.title").CanWrite, "other format must refuse writes");
        using (var media = TagLib.File.Create(path))
        {
            media.Tag.Title = "Existing audio title";
            media.Save();
        }

        var beforeBytes = IoFile.ReadAllBytes(path);
        var store = new EmbeddedVideoMetadataStore();
        var file = GetStorageFile(path);
        var before = store.LoadAsync(file).GetAwaiter().GetResult().Metadata;
        var changed = before;
        changed.Title = "Should not be written";
        var result = store.SaveAsync(file, changed, before, new[] { "Title" }).GetAwaiter().GetResult();
        Assert(result.Succeeded && result.HasWarnings && result.UnsupportedProperties.Contains("work.title"), "unsupported result was not reported");
        Assert(beforeBytes.SequenceEqual(IoFile.ReadAllBytes(path)), "read-only format bytes changed");
        Assert(store.LoadAsync(file).GetAwaiter().GetResult().Metadata.Title == "Existing audio title", "existing tag could not be read");
    }

    private static void TestMultipleImages()
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

    private static void TestCorruptCustomPayload()
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

    private static void TestTechnicalStreams()
    {
        var previous = Environment.GetEnvironmentVariable("FFPROBE_PATH");
        Environment.SetEnvironmentVariable("FFPROBE_PATH", "ffprobe");
        try
        {
            var result = new MediaProbeService().ProbeAsync(GetStorageFile(Path.Combine(_root, "sample.mkv"))).GetAwaiter().GetResult();
            Assert(result.UsedFfprobe, "FFprobe was not used");
            Assert(result.Technical.AudioStreams.Count == 2, $"expected two audio streams, got {result.Technical.AudioStreams.Count}");
            Assert(result.Technical.VideoStreams.Count == 1, "video stream missing");
        }
        finally { Environment.SetEnvironmentVariable("FFPROBE_PATH", previous); }
    }

    private static void TestLegacyMigration()
    {
        const string legacySettings = """{"ColumnOrder":["Title","ReleaseDateText"],"ColumnWidths":{"Title":210},"VisibleListFieldIds":["work.title","deleted.legacy-id"],"VisibleDetailFieldIds":["work.title"],"DetailFieldOrder":["work.title","dates.release"]}""";
        var settings = JsonSerializer.Deserialize<AppSettings>(legacySettings) ?? throw new InvalidOperationException("legacy settings did not deserialize");
        Assert(settings.ColumnOrder.SequenceEqual(new[] { "Title", "ReleaseDateText" }), "legacy column order changed");
        Assert(settings.ColumnWidths.TryGetValue("Title", out var width) && width == 210, "legacy column width was lost");
        Assert(settings.VisibleListFieldIds?.Contains("deleted.legacy-id") == true, "unknown user ID was not retained for safe normalization");

        const string oldSnapshot = """{"Title":"Legacy title","Participants":"Actor A; Actor B","Comment":"old comment","ReleaseDate":"2021-03-04T00:00:00+00:00"}""";
        var snapshot = JsonSerializer.Deserialize<VideoMetadataSnapshot>(oldSnapshot) ?? throw new InvalidOperationException("legacy snapshot did not deserialize");
        var document = MetadataDocumentMapper.FromSnapshot(snapshot);
        Assert(document.Work.Title == "Legacy title" && document.PeopleAndStaff.Cast.Count == 2, "old snapshot did not map to new model");
        Assert(document.Dates.ReleaseDate?.Year == 2021, "legacy date did not migrate");
    }

    private static void TestUnknownFieldIds()
    {
        Assert(MetadataFieldCatalog.FindById("field.from.future-version") is null, "unknown ID should not resolve");
        var other = EmbeddedMetadataFormatAdapterSelector.Select("unknown-container.xyz");
        Assert(other.GetFieldCapability("field.from.future-version") == MetadataFieldCapability.Unsupported, "unknown ID was not safely unsupported");
        var oldSettings = JsonSerializer.Deserialize<AppSettings>("""{"ListFieldOrder":["work.title","field.from.future-version"],"DetailFieldOrder":["work.title"]}""")!;
        Assert(oldSettings.ListFieldOrder.Count == 2, "settings could not deserialize with an unknown ID");
    }

    private static void TestExternalModificationDetection()
    {
        var path = CopyFixture("sample.mp4");
        var storeType = typeof(EmbeddedVideoMetadataStore);
        var versionType = storeType.GetNestedType("FileVersion", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("file version type not found");
        var readVersion = storeType.GetMethod("ReadFileVersion", BindingFlags.NonPublic | BindingFlags.Static)!;
        var ensureUnchanged = storeType.GetMethod("EnsureFileUnchanged", BindingFlags.NonPublic | BindingFlags.Static)!;
        var expected = readVersion.Invoke(null, new object[] { path })!;
        IoFile.AppendAllText(path, "external update");
        try
        {
            ensureUnchanged.Invoke(null, new[] { (object)path, expected });
            throw new InvalidOperationException("external change was not detected");
        }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException)
        {
            Assert(ex.InnerException.Message.Contains("外部", StringComparison.Ordinal), "wrong external-change error");
        }
        Assert(versionType.IsInstanceOfType(expected), "version snapshot had unexpected type");
    }

    private static void TestFailedSavePreservesOriginal()
    {
        var path = CopyFixture("sample.mp4");
        var beforeBytes = IoFile.ReadAllBytes(path);
        var file = GetStorageFile(path);
        var store = new EmbeddedVideoMetadataStore();
        var before = store.LoadAsync(file).GetAwaiter().GetResult().Metadata;
        before.StructuredMetadata.CustomFields["custom.oversize"] = new MetadataValue { Kind = MetadataValueKind.Text, Text = new string('x', 70 * 1024) };
        var result = store.SaveAsync(file, before, before, new[] { "custom.oversize" }).GetAwaiter().GetResult();
        Assert(!result.Succeeded, "oversize payload unexpectedly saved");
        Assert(beforeBytes.SequenceEqual(IoFile.ReadAllBytes(path)), "failed transaction changed the source file");
    }

    private static string CopyFixture(string name)
    {
        var source = Path.Combine(_root, name);
        var copy = Path.Combine(_root, $"copy-{Guid.NewGuid():N}{Path.GetExtension(name)}");
        IoFile.Copy(source, copy);
        return copy;
    }

    private static StorageFile GetStorageFile(string path) => StorageFile.GetFileFromPathAsync(path).AsTask().GetAwaiter().GetResult();

    private static void RunFfmpeg(params string[] arguments)
    {
        var start = new ProcessStartInfo("ffmpeg") { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true };
        start.ArgumentList.Add("-y");
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("ffmpeg did not start");
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException($"ffmpeg failed: {stderr}");
    }

    private static void Run(string name, Action check)
    {
        try
        {
            check();
            _passed++;
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception ex)
        {
            _failed++;
            Console.WriteLine($"FAIL {name}: {ex.GetBaseException().Message}");
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
