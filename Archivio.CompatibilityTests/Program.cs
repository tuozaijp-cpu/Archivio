using System.Diagnostics;
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

    private static int Main()
    {
        _root = Path.Combine(Path.GetTempPath(), $"Archivio-compat-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        try
        {
            Run("metadata filename template service", TestMetadataFileNameTemplateService);
            Run("custom metadata editor value conversion", TestCustomMetadataEditorValueConversion);
            Run("thumbnail cache capacity and LRU behavior", TestThumbnailCacheCapacityAndLru);
            Run("thumbnail cache overwrite remove clear and retain", TestThumbnailCacheMutation);
            Run("metadata loading coordinator retries and cancels", TestMetadataLoadCoordinator);
            Run("file save operation state is reference counted", TestFileSaveOperationState);
            Run("FFmpeg playback commands and executable discovery", TestFfmpegMediaService);
            Run("metadata field format capabilities", TestMetadataFieldFormatCapabilities);
            Run("create isolated media fixtures", CreateFixtures);
            Run("file operation naming rules", TestVideoFileOperationNamingRules);
            Run("CSV row formatting", TestVideoCsvRowFormatting);
            Run("FFmpeg frame capture and thumbnail generation", TestFfmpegMediaProcessing);
            Run("metadata save verification preserves technical information", TestVerificationPreservesTechnicalInformation);
            Run("MP4/MKV saves work in direct and backup modes", TestMetadataSaveModes);
            Run("MP4 standard/custom metadata roundtrip", TestMp4RoundTrip);
            Run("MKV standard/custom metadata roundtrip", TestMkvRoundTrip);
            Run("standard-only saves keep existing payload untouched", TestStandardOnlySaveKeepsPayload);
            Run("other format reads but refuses writes", TestOtherFormatReadOnly);
            Run("multiple cover images roundtrip", TestMultipleImages);
            Run("broken custom payload leaves standard tags readable", TestCorruptCustomPayload);
            Run("technical probe retains multiple audio streams", TestTechnicalStreams);
            Run("legacy settings and snapshot migration", TestLegacyMigration);
            Run("display visibility and order settings", TestDisplaySettings);
            Run("unknown field IDs are ignored by the catalog", TestUnknownFieldIds);
            Run("metadata file commit modes", TestMetadataFileCommitModes);
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

    private static void TestMetadataFieldFormatCapabilities()
    {
        Assert(!MetadataFieldCatalog.ListFields.Any(field => field.Id == "file.status"), "status column is still available");
        var mp4 = EmbeddedMetadataFormatAdapterSelector.Select("sample.mp4");
        var mkv = EmbeddedMetadataFormatAdapterSelector.Select("sample.mkv");
        var other = EmbeddedMetadataFormatAdapterSelector.Select("sample.mp3");

        Assert(mp4.GetFieldCapability("work.title") == MetadataFieldCapability.Native, "MP4 standard capability changed");
        Assert(mkv.GetFieldCapability("work.title") == MetadataFieldCapability.Native, "MKV standard capability changed");
        Assert(mp4.GetFieldCapability("work.subtitle") == MetadataFieldCapability.ArchivioCustom, "MP4 Archivio capability missing");
        Assert(mkv.GetFieldCapability("work.subtitle") == MetadataFieldCapability.ArchivioCustom, "MKV Archivio capability missing");
        Assert(mp4.GetFieldCapability("technical.duration") == MetadataFieldCapability.ReadOnly, "technical field became writable");
        Assert(other.GetFieldSupport("work.title").CanRead && !other.GetFieldSupport("work.title").CanWrite, "other format write policy changed");
        Assert(other.GetFieldSupport("work.subtitle").Capability == MetadataFieldCapability.Unsupported, "other format accepted Archivio field");
        Assert(other.GetFieldCapability("unknown.future-id") == MetadataFieldCapability.Unsupported, "unknown ID was not rejected");
    }

    private static void TestVideoFileOperationNamingRules()
    {
        Assert(VideoFileOperationsService.GetRenameName("video.mp4", "renamed") == "renamed.mp4",
            "rename rule did not append the source extension");
        Assert(VideoFileOperationsService.GetRenameName("video.mp4", "renamed.mkv") == "renamed.mp4",
            "rename rule did not preserve the source extension when the requested extension differed");
        Assert(VideoFileOperationsService.GetRenameName("video.mp4", "renamed.MP4") == "renamed.MP4",
            "rename rule changed a matching extension");

        var firstOutput = Path.Combine(_root, "naming-test_ReMUX.mp4");
        var secondOutput = Path.Combine(_root, "naming-test_ReMUX_2.mp4");
        IoFile.WriteAllText(firstOutput, "occupied");
        IoFile.WriteAllText(secondOutput, "occupied");
        var output = VideoFileOperationsService.GetAvailableOutputPath(_root, "naming-test", ".mp4");
        Assert(output == Path.Combine(_root, "naming-test_ReMUX_3.mp4"),
            "remux output naming did not skip existing files");
    }

    private static void TestVideoCsvRowFormatting()
    {
        var row = new VideoCsvRow(
            "clip,one.mp4", "1 KB", "Title, \"Quoted\"\r\nLine", "Actor", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "");
        var fields = row.ToFields().ToArray();
        var csv = VideoCsvExportService.FormatRow(fields);
        Assert(fields.Length == 20 && fields[0] == row.FileName && fields[2] == row.Title,
            "CSV row field mapping changed");
        Assert(csv.StartsWith("\"clip,one.mp4\",1 KB,", StringComparison.Ordinal),
            "CSV row formatting did not quote a comma-containing field");
        Assert(csv.Contains("\"Title, \"\"Quoted\"\"\r\nLine\"", StringComparison.Ordinal),
            "CSV export did not escape quotes and line breaks in a field");
    }

    private static void TestVerificationPreservesTechnicalInformation()
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

    private static void TestMetadataFileNameTemplateService()
    {
        var item = new VideoFileItem("video.mp4");
        item.Title = "Title:/ one";
        item.CatalogNumber = "CAT-01";
        item.StructuredMetadata.Work.Subtitle = "Subtitle";
        item.StructuredMetadata.Work.AlternateTitles = ["Alternate one", "Alternate two"];
        item.StructuredMetadata.PeopleAndStaff.Cast = new()
        {
            new CastCredit { PersonName = "Actor", CharacterName = "Role", IsLead = true, Order = 0 },
            new CastCredit { PersonName = "Second", Order = 1 }
        };
        item.ReleaseDate = new DateTimeOffset(2024, 2, 3, 0, 0, 0, TimeSpan.Zero);
        MetadataFieldCatalog.RefreshCustomFields(new[]
        {
            new CustomMetadataFieldDefinition { Id = "custom.filename-test", DisplayName = "Filename test", ValueType = "Text", Category = "Work", IsEditable = true }
        });
        item.StructuredMetadata.CustomFields["custom.filename-test"] = new MetadataValue { Text = "custom value" };

        var service = new MetadataFileNameTemplateService();
        Assert(service.Build(item, @"\work.title").FileName == "Title__ one.mp4", "basic filename value was not sanitized");
        Assert(service.Build(item, @"[\credits.cast.0][\credits.cast.1]").FileName == "[Actor][Second].mp4", "multiple cast values failed");
        Assert(service.Build(item, @"{work.subtitle}-[{\credits.cast.0.character}]").FileName == "Subtitle-[{Role}].mp4", "brace syntax or role failed");
        Assert(service.Build(item, @"[\work.alternate-titles.1]").FileName == "[Alternate two].mp4", "indexed metadata value failed");
        Assert(service.Build(item, @"[\custom.filename-test]-\dates.release").FileName == "[custom value]-2024-02-03.mp4", "custom/date value failed");
        Assert(service.Build(item, @"\credits.cast.0.lead").FileName == "true.mp4", "cast role flag failed");
        Assert(service.Build(item, @"\出演者0").FileName == "Actor.mp4", "Japanese cast alias failed");
        var unknownToken = service.Build(item, @"\unknown.id");
        Assert(!unknownToken.Succeeded && unknownToken.InvalidIds.Contains("unknown.id"), "unknown ID was not rejected and reported");
        var unknownBraceToken = service.Build(item, "{missing.field}");
        Assert(!unknownBraceToken.Succeeded && unknownBraceToken.InvalidIds.Contains("missing.field"), "unknown brace token was not rejected and reported");
        Assert(service.Build(item, @"[\タイトル]-[\品番]").FileName == "[Title__ one]-[CAT-01].mp4", "compatibility aliases failed");
        Assert(service.Build(item, @"literal{").FileName == "literal{.mp4", "unclosed brace was not preserved as literal text");
    }

    private static void TestFileSaveOperationState()
    {
        var item = new VideoFileItem("video.mp4");
        var stateNotifications = 0;
        item.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(VideoFileItem.IsFileSaveInProgress))
            {
                stateNotifications++;
            }
        };
        Assert(!item.IsFileSaveInProgress, "file save state should start inactive");

        item.BeginFileSaveOperation();
        item.BeginFileSaveOperation();
        Assert(item.IsFileSaveInProgress, "file save state was not activated");
        Assert(stateNotifications == 1, "starting overlapping saves emitted an unexpected state notification");

        item.EndFileSaveOperation();
        Assert(item.IsFileSaveInProgress, "file save state ended while another save was active");
        Assert(stateNotifications == 1, "ending one of overlapping saves emitted an unexpected state notification");

        item.EndFileSaveOperation();
        Assert(!item.IsFileSaveInProgress, "file save state did not return to inactive");
        Assert(stateNotifications == 2, "ending the final save did not notify state change");
    }

    private static void TestCustomMetadataEditorValueConversion()
    {
        var multipleText = new MetadataValue();
        multipleText.SetEditorValue(MetadataFieldValueType.MultipleText, "First; Second\r\nThird");
        Assert(multipleText.Kind == MetadataValueKind.MultipleText, "multiple text value kind was not set");
        Assert(multipleText.TextValues.SequenceEqual(new[] { "First", "Second", "Third" }), "multiple text values were not parsed");
        Assert(multipleText.ToEditorText() == "First; Second\r\nThird", "multiple text editor value did not roundtrip");

        var decimalValue = new MetadataValue();
        decimalValue.SetEditorValue(MetadataFieldValueType.Decimal, "12.5");
        Assert(decimalValue.Kind == MetadataValueKind.Decimal && decimalValue.Decimal == 12.5m, "decimal value was not parsed invariantly");
        Assert(decimalValue.ToEditorText() == "12.5", "decimal editor value was not formatted invariantly");

        var dateValue = new MetadataValue();
        dateValue.SetEditorValue(MetadataFieldValueType.Date, "2024-02-03");
        Assert(dateValue.Kind == MetadataValueKind.Date && dateValue.Date?.Date == new DateTimeOffset(2024, 2, 3, 0, 0, 0, TimeSpan.Zero).Date, "date value was not parsed");
        Assert(dateValue.ToEditorText() == "2024-02-03", "date editor value was not formatted");
    }

    private static void TestThumbnailCacheCapacityAndLru()
    {
        var entryLimitCache = new ThumbnailImageCache(maxEntries: 2, maxImageDataBytes: 100);
        entryLimitCache.Set("one.mp4", CreateThumbnailCacheEntry(2));
        entryLimitCache.Set("two.mp4", CreateThumbnailCacheEntry(2));
        Assert(entryLimitCache.TryGet("ONE.mp4", out _), "cache lookup did not use case-insensitive paths");
        entryLimitCache.Set("three.mp4", CreateThumbnailCacheEntry(2));
        Assert(!entryLimitCache.TryGet("two.mp4", out _), "least recently used entry was not evicted");
        Assert(entryLimitCache.TryGet("one.mp4", out _) && entryLimitCache.TryGet("three.mp4", out _), "recent entries were evicted");
        Assert(entryLimitCache.Count == 2, "entry count limit was exceeded");

        var byteLimitCache = new ThumbnailImageCache(maxEntries: 10, maxImageDataBytes: 6);
        byteLimitCache.Set("first.mp4", CreateThumbnailCacheEntry(4));
        byteLimitCache.Set("second.mp4", CreateThumbnailCacheEntry(3));
        Assert(!byteLimitCache.TryGet("first.mp4", out _), "byte limit did not evict the least recent entry");
        Assert(byteLimitCache.TryGet("second.mp4", out _), "byte limit evicted the newest entry");
        Assert(byteLimitCache.ImageDataBytes == 3, "cached image byte total is incorrect after byte eviction");

        byteLimitCache.Set("oversized.mp4", CreateThumbnailCacheEntry(7));
        Assert(!byteLimitCache.TryGet("oversized.mp4", out _), "oversized image data was retained");
        Assert(byteLimitCache.ImageDataBytes == 3, "oversized image changed cached byte accounting");

        var oversizedReplacementCache = new ThumbnailImageCache(maxEntries: 2, maxImageDataBytes: 4);
        oversizedReplacementCache.Set("same.mp4", CreateThumbnailCacheEntry(2));
        oversizedReplacementCache.Set("SAME.MP4", CreateThumbnailCacheEntry(5));
        Assert(!oversizedReplacementCache.TryGet("same.mp4", out _), "oversized replacement retained the previous entry");
        Assert(oversizedReplacementCache.Count == 0 && oversizedReplacementCache.ImageDataBytes == 0,
            "oversized replacement did not remove the previous entry from cache accounting");
    }

    private static void TestThumbnailCacheMutation()
    {
        var cache = new ThumbnailImageCache(maxEntries: 5, maxImageDataBytes: 20);
        cache.Set("one.mp4", CreateThumbnailCacheEntry(5));
        cache.Set("two.mp4", CreateThumbnailCacheEntry(4));

        cache.Set("ONE.MP4", CreateThumbnailCacheEntry(2, fileSize: 20));
        Assert(cache.Count == 2, "overwriting a case-insensitive path duplicated the entry");
        Assert(cache.ImageDataBytes == 6, "overwriting an entry did not update byte accounting");
        Assert(cache.TryGet("one.mp4", out var replaced) && replaced.FileSize == 20 && replaced.ImageData.Length == 2,
            "overwriting an entry did not replace its value");

        cache.Remove("missing.mp4");
        cache.Remove("two.mp4");
        cache.Remove("two.mp4");
        Assert(cache.Count == 1 && cache.ImageDataBytes == 2, "remove did not update cache state idempotently");

        cache.Set("three.mp4", CreateThumbnailCacheEntry(3));
        cache.Retain(new[] { "ONE.MP4" });
        Assert(cache.Count == 1 && cache.TryGet("one.mp4", out _), "retain removed a case-insensitive matching path or kept an obsolete entry");

        cache.Clear();
        Assert(cache.Count == 0 && cache.ImageDataBytes == 0, "clear did not reset entries and byte accounting");
        Assert(!cache.TryGet("one.mp4", out _), "clear retained a cached item");

        var zeroCapacityCache = new ThumbnailImageCache(maxEntries: 1, maxImageDataBytes: 0);
        zeroCapacityCache.Set("empty.mp4", new ThumbnailCacheEntry());
        Assert(zeroCapacityCache.TryGet("empty.mp4", out _), "zero-byte cache entry should fit a zero-byte budget");
    }

    private static ThumbnailCacheEntry CreateThumbnailCacheEntry(int dataLength, ulong fileSize = 1)
    {
        return new ThumbnailCacheEntry
        {
            FileSize = fileSize,
            LastWriteTime = DateTimeOffset.UnixEpoch,
            ImageData = new byte[dataLength]
        };
    }

    private static void TestMetadataLoadCoordinator()
    {
        using var readSemaphore = new SemaphoreSlim(1, 1);
        var coordinator = new MetadataLoadCoordinator(readSemaphore, maxAttempts: 3, retryDelay: TimeSpan.Zero);
        var attempts = 0;
        var loaded = coordinator.ExecuteAsync(ct => coordinator.LoadWithRetryAsync(_ =>
        {
            attempts++;
            return Task.FromResult(new VideoMetadataLoadResult
            {
                WindowsPropertiesLoaded = attempts == 2
            });
        }, ct)).GetAwaiter().GetResult();

        Assert(attempts == 2 && loaded.WindowsPropertiesLoaded, "metadata coordinator did not retry until properties loaded");

        attempts = 0;
        coordinator.ExecuteAsync(ct => coordinator.LoadWithRetryAsync(_ =>
        {
            attempts++;
            return Task.FromResult(new VideoMetadataLoadResult());
        }, ct)).GetAwaiter().GetResult();
        Assert(attempts == 3, "metadata coordinator did not stop after the configured attempt count");

        using var cancellation = new CancellationTokenSource();
        var cancelledAttempts = 0;
        var cancellationTask = coordinator.ExecuteAsync(ct => coordinator.LoadWithRetryAsync(_ =>
        {
            cancelledAttempts++;
            cancellation.Cancel();
            return Task.FromResult(new VideoMetadataLoadResult());
        }, ct), cancellation.Token);

        var wasCancelled = false;
        try
        {
            cancellationTask.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            wasCancelled = true;
        }
        Assert(wasCancelled, "metadata coordinator ignored cancellation between attempts");
        Assert(cancelledAttempts == 1, "metadata coordinator attempted another load after cancellation");

        using var boundedSemaphore = new SemaphoreSlim(2, 2);
        var boundedCoordinator = new MetadataLoadCoordinator(boundedSemaphore, maxAttempts: 1, retryDelay: TimeSpan.Zero);
        var activeLoads = 0;
        var maximumConcurrency = 0;
        var concurrentLoads = Enumerable.Range(0, 8).Select(_ => boundedCoordinator.ExecuteAsync(async _ =>
        {
            var active = Interlocked.Increment(ref activeLoads);
            int observed;
            while (active > (observed = Volatile.Read(ref maximumConcurrency))
                && Interlocked.CompareExchange(ref maximumConcurrency, active, observed) != observed)
            {
            }

            await Task.Delay(20);
            Interlocked.Decrement(ref activeLoads);
            return true;
        }));
        Task.WhenAll(concurrentLoads).GetAwaiter().GetResult();
        Assert(maximumConcurrency == 2, "metadata coordinator did not enforce the configured shared concurrency limit");
    }

    private static void TestFfmpegMediaService()
    {
        var executableDirectory = Path.Combine(_root, "ffmpeg-tools");
        Directory.CreateDirectory(executableDirectory);
        var ffmpegPath = Path.Combine(executableDirectory, "ffmpeg.exe");
        IoFile.WriteAllBytes(ffmpegPath, []);
        var ffprobePath = Path.Combine(executableDirectory, "ffprobe.exe");
        IoFile.WriteAllBytes(ffprobePath, []);

        Assert(
            FfmpegMediaService.FindFfmpegInConfiguredPaths(ffmpegPath, null) == ffmpegPath,
            "configured ffmpeg executable was not found");
        Assert(
            FfmpegMediaService.FindFfmpegInConfiguredPaths(ffprobePath, null) == ffmpegPath,
            "ffmpeg sibling to configured ffprobe was not found");
        Assert(
            FfmpegMediaService.FindFfmpegInConfiguredPaths(null, executableDirectory) == ffmpegPath,
            "ffmpeg executable in PATH was not found");

        var captureInfo = FfmpegMediaService.BuildCaptureFrameStartInfo(
            ffmpegPath,
            "sample video.mp4",
            TimeSpan.FromSeconds(12.5));
        var captureArguments = captureInfo.ArgumentList.ToArray();
        Assert(captureInfo.FileName == ffmpegPath, "capture command used the wrong executable");
        Assert(captureArguments.Contains("12.5"), "capture command lost the current playback position");
        Assert(captureArguments.Contains("sample video.mp4"), "capture command did not preserve the input path");
        Assert(captureArguments[^1] == "pipe:1", "capture command did not write the image to stdout");
        Assert(
            FfmpegMediaService.BuildCaptureFrameStartInfo(ffmpegPath, "video.mp4", TimeSpan.FromSeconds(-1))
                .ArgumentList.Contains("0"),
            "negative capture position was not clamped");

        var sheetInfo = FfmpegMediaService.BuildThumbnailSheetStartInfo(
            ffmpegPath,
            "sample video.mp4",
            TimeSpan.FromSeconds(30));
        var sheetArguments = sheetInfo.ArgumentList.ToArray();
        Assert(sheetArguments.Count(argument => argument == "-ss") == 25, "thumbnail sheet did not seek to 25 samples");
        Assert(sheetArguments.Contains("0.6") && sheetArguments.Contains("29.4"), "thumbnail sample timestamps changed");
        Assert(
            sheetArguments.Any(argument => argument.Contains("tile=5x5", StringComparison.Ordinal)),
            "thumbnail sheet grid filter is missing");
        Assert(sheetArguments[^1] == "pipe:1", "thumbnail sheet command did not write the image to stdout");

        var invalidDurationRejected = false;
        try
        {
            FfmpegMediaService.BuildThumbnailSheetStartInfo(ffmpegPath, "video.mp4", TimeSpan.Zero);
        }
        catch (ArgumentOutOfRangeException)
        {
            invalidDurationRejected = true;
        }

        Assert(invalidDurationRejected, "thumbnail sheet accepted an invalid duration");
    }

    private static void TestFfmpegMediaProcessing()
    {
        var service = new FfmpegMediaService();
        var ffmpegPath = service.FindFfmpegAsync(null).GetAwaiter().GetResult();
        Assert(!string.IsNullOrWhiteSpace(ffmpegPath), "FFmpeg executable could not be resolved");

        var videoPath = Path.Combine(_root, "sample.mp4");
        var capturedFrame = service.CaptureFrameAsync(
            ffmpegPath!,
            videoPath,
            TimeSpan.FromMilliseconds(500)).GetAwaiter().GetResult();
        Assert(IsPng(capturedFrame), "frame capture did not return PNG data");

        var thumbnailSheet = service.CreateThumbnailSheetAsync(
            ffmpegPath!,
            videoPath,
            TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();
        Assert(IsPng(thumbnailSheet), "thumbnail generation did not return PNG data");
    }

    private static bool IsPng(byte[] data) =>
        data.Length >= 8
        && data.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

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

    private static void TestMetadataSaveModes()
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

    private static void TestStandardOnlySaveKeepsPayload()
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
        var changed = MetadataDocumentMapper.ToSnapshot(MetadataDocumentMapper.FromSnapshot(before));
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
        SettingsManager.NormalizeMetadataLayoutSettings(settings);
        Assert(settings.ColumnOrder.SequenceEqual(new[] { "Title", "ReleaseDateText" }), "legacy column order changed");
        Assert(settings.ColumnWidths.TryGetValue("Title", out var width) && width == 210, "legacy column width was lost");
        Assert(settings.ListFieldOrder.SequenceEqual(new[] { "work.title", "dates.release" }), "legacy column order was not migrated to stable IDs");
        Assert(settings.VisibleListFieldIds?.SequenceEqual(new[] { "work.title" }) == true, "unknown user ID was not safely ignored");

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
        SettingsManager.NormalizeMetadataLayoutSettings(oldSettings);
        Assert(oldSettings.ListFieldOrder.SequenceEqual(new[] { "work.title" }), "unknown ID was not safely ignored");
    }

    private static void TestDisplaySettings()
    {
        MetadataFieldCatalog.RefreshCustomFields(null);
        var savedColumnSettings = JsonSerializer.Deserialize<AppSettings>(
            JsonSerializer.Serialize(new AppSettings { DetailFieldColumnCount = 2 }))
            ?? throw new InvalidOperationException("detail column settings did not deserialize");
        SettingsManager.NormalizeMetadataLayoutSettings(savedColumnSettings);
        Assert(savedColumnSettings.DetailFieldColumnCount == 2, "two-column detail layout was not preserved by settings serialization");
        Assert(!MetadataFieldCatalog.All.Any(field => field.IsCustom), "empty catalog initialization retained custom fields");

        var customField = new CustomMetadataFieldDefinition
        {
            Id = "custom.layout-test",
            DisplayName = "Layout test",
            ValueType = "Text",
            Category = "Work",
            IsEditable = true,
            IsInListByDefault = true,
            IsInDetailsByDefault = true
        };
        var settings = new AppSettings
        {
            CustomMetadataFields = new() { customField },
            VisibleListFieldIds = new() { "work.title", customField.Id, "unknown.future" },
            ListFieldOrder = new() { "dates.release", customField.Id, "work.title", customField.Id },
            ListFieldWidths = new() { [customField.Id] = 235 },
            VisibleDetailFieldIds = new() { "artwork.cover", customField.Id, "unknown.future" },
            DetailFieldOrder = new() { "artwork.cover", customField.Id, "unknown.future" }
        };
        SettingsManager.NormalizeMetadataLayoutSettings(settings);
        Assert(settings.VisibleListFieldIds!.SequenceEqual(new[] { "work.title", customField.Id }), "visible list IDs were not normalized");
        Assert(settings.ListFieldOrder.SequenceEqual(new[] { "dates.release", customField.Id, "work.title" }), "list order was not preserved");
        Assert(settings.ListFieldWidths[customField.Id] == 235, "custom column width was not preserved");
        Assert(settings.VisibleDetailFieldIds!.SequenceEqual(new[] { "artwork.cover", customField.Id }), "visible detail IDs were not normalized");
        Assert(settings.DetailFieldOrder.SequenceEqual(new[] { "artwork.cover", customField.Id }), "detail order was not preserved");

        var resolvedCustomField = MetadataFieldCatalog.FindById(customField.Id);
        Assert(resolvedCustomField is { IsCustom: true, CustomDisplayName: "Layout test" }, "custom field definition was not loaded before layout normalization");
        Assert(resolvedCustomField?.LocalizationKey == customField.DisplayName, "custom field display label changed");
        Assert(MetadataFieldCatalog.FindById("work.title")?.LocalizationKey == "Col_Title", "standard field localization key changed");
    }

    private static void TestMetadataFileCommitModes()
    {
        var backupPath = CopyFixture("sample.mp4");
        var originalBytes = IoFile.ReadAllBytes(backupPath);
        string temporaryPath;
        using (var commit = FileCommitService.Open(backupPath, directWrite: false))
        {
            temporaryPath = commit.WorkingPath;
            Assert(!commit.IsDirectWrite && temporaryPath != backupPath, "backup mode did not create a working copy");
            Assert(originalBytes.SequenceEqual(IoFile.ReadAllBytes(temporaryPath)), "working copy differs from original");
            IoFile.AppendAllText(temporaryPath, "metadata update");
            Assert(originalBytes.SequenceEqual(IoFile.ReadAllBytes(backupPath)), "backup mode modified the original before commit");
            commit.Commit();
        }
        Assert(!IoFile.Exists(temporaryPath), "committed working copy was not moved into place");
        Assert(!originalBytes.SequenceEqual(IoFile.ReadAllBytes(backupPath)), "backup mode did not commit the working copy");

        var directPath = CopyFixture("sample.mp4");
        using (var commit = FileCommitService.Open(directPath, directWrite: true))
        {
            Assert(commit.IsDirectWrite && commit.WorkingPath == directPath, "direct mode did not target the original file");
            IoFile.AppendAllText(commit.WorkingPath, "direct metadata update");
            commit.Commit();
        }
        Assert(
            IoFile.ReadAllBytes(directPath).AsSpan().EndsWith("direct metadata update"u8),
            "direct mode changes were not retained");
    }

    private static void TestExternalModificationDetection()
    {
        var path = CopyFixture("sample.mp4");
        var commit = FileCommitService.Open(path, directWrite: false);
        var temporaryPath = commit.WorkingPath;
        var externalUpdate = IoFile.ReadAllBytes(path).Concat("external update"u8.ToArray()).ToArray();
        IoFile.AppendAllText(path, "external update");
        try
        {
            commit.Commit();
            throw new InvalidOperationException("external change was not detected");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("外部", StringComparison.Ordinal))
        {
            Assert(ex.Message.Contains("外部", StringComparison.Ordinal), "wrong external-change error");
        }
        finally
        {
            commit.Dispose();
        }
        Assert(externalUpdate.SequenceEqual(IoFile.ReadAllBytes(path)), "external changes were overwritten");
        Assert(!IoFile.Exists(temporaryPath), "uncommitted working copy was not removed");
    }

    private static void TestFailedSavePreservesOriginal()
    {
        MetadataFieldCatalog.RefreshCustomFields(new[]
        {
            new CustomMetadataFieldDefinition { Id = "custom.oversize", DisplayName = "Oversize", ValueType = "Text", Category = "Work", IsEditable = true }
        });

        foreach (var directWrite in new[] { false, true })
        {
            var path = CopyFixture("sample.mp4");
            var beforeBytes = IoFile.ReadAllBytes(path);
            var before = new VideoMetadataSnapshot();
            before.StructuredMetadata.CustomFields["custom.oversize"] =
                new MetadataValue { Kind = MetadataValueKind.Text, Text = new string('x', 70 * 1024) };
            var result = MetadataSaveCoordinator.ExecuteAsync(
                path,
                before,
                before,
                new[] { "custom.oversize" },
                directWrite).GetAwaiter().GetResult();
            Assert(!result.Succeeded, $"direct={directWrite}: oversize payload unexpectedly saved");
            Assert(beforeBytes.SequenceEqual(IoFile.ReadAllBytes(path)), $"direct={directWrite}: failed transaction changed the source file");
        }
    }

    private static string CopyFixture(string name)
    {
        var source = Path.Combine(_root, name);
        var copy = Path.Combine(_root, $"copy-{Guid.NewGuid():N}{Path.GetExtension(name)}");
        IoFile.Copy(source, copy);
        return copy;
    }

    // WinRTの非同期処理を完了させてから同期テストへ戻します。
    private static StorageFile GetStorageFile(string path) =>
        Task.Run(async () => await StorageFile.GetFileFromPathAsync(path)).GetAwaiter().GetResult();

    private static void RunFfmpeg(params string[] arguments)
    {
        var start = new ProcessStartInfo("ffmpeg") { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = false, CreateNoWindow = true };
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
