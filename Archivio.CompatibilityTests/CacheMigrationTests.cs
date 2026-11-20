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

internal static class CacheMigrationTests
{
internal static void TestThumbnailCacheCapacityAndLru()
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

internal static void TestThumbnailCacheMutation()
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

internal static void TestMetadataLoadCoordinator()
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

internal static void TestLegacyMigration()
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

internal static void TestUnknownFieldIds()
    {
        Assert(MetadataFieldCatalog.FindById("field.from.future-version") is null, "unknown ID should not resolve");
        var other = EmbeddedMetadataFormatAdapterSelector.Select("unknown-container.xyz");
        Assert(other.GetFieldCapability("field.from.future-version") == MetadataFieldCapability.Unsupported, "unknown ID was not safely unsupported");
        var oldSettings = JsonSerializer.Deserialize<AppSettings>("""{"ListFieldOrder":["work.title","field.from.future-version"],"DetailFieldOrder":["work.title"]}""")!;
        SettingsManager.NormalizeMetadataLayoutSettings(oldSettings);
        Assert(oldSettings.ListFieldOrder.SequenceEqual(new[] { "work.title" }), "unknown ID was not safely ignored");
    }

internal static void TestDisplaySettings()
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
}

