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

internal static class MetadataUnitTests
{
internal static void TestMetadataFieldFormatCapabilities()
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

internal static void TestMetadataFileNameTemplateService()
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

internal static void TestFileSaveOperationState()
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

internal static void TestVideoFileStateBoundaries()
    {
        var item = new VideoFileItem("state-test.mp4");
        var titleNotification = false;
        item.PropertyChanged += (_, args) => titleNotification |= args.PropertyName == nameof(VideoFileItem.Title);
        Assert(item.FileInfoState.FullPath == "state-test.mp4" && item.FileInfoState.FileName == "state-test.mp4",
            "file information state was not initialized without StorageFile");

        item.FileSizeBytes = 123;
        item.Title = "State title";
        item.CoverArtImages = new List<CoverArtImageData>
        {
            new() { Data = [1, 2, 3], MimeType = "image/jpeg" }
        };
        item.IsCoverArtDirty = true;
        item.MarkCustomMetadataFieldChanged("custom.stable-id");

        Assert(item.FileInfoState.FileSizeBytes == 123, "file information state did not track the compatibility property");
        Assert(item.MetadataState.Title == "State title" && titleNotification,
            "metadata state and compatibility notification were not synchronized");
        Assert(item.CoverArtState.Images.Count == 1 && item.CoverArtState.IsDirty,
            "cover art state did not track binary image state");
        Assert(item.ChangeTrackingState.DirtyCustomFieldIds.Contains("custom.stable-id"),
            "change tracking state did not retain the stable field ID");
        Assert(item.CoverArtState.Images.All(image => image.Data is not null),
            "cover art state contains invalid image data");
    }

internal static void TestSelectionGenerationGuard()
    {
        var guard = new SelectionGenerationGuard();
        var first = guard.Begin();
        var second = guard.Begin();
        Assert(!guard.IsCurrent(first) && guard.IsCurrent(second),
            "a previous selection generation was still considered current");
    }

internal static void TestOperationCoordinatorLifecycle()
    {
        using var coordinator = new OperationCoordinator();
        var busyChanges = new List<bool>();
        coordinator.BusyChanged += busy => busyChanges.Add(busy);

        var first = coordinator.Start();
        using var firstBusy = coordinator.EnterBusy();
        using var secondBusy = coordinator.EnterBusy();
        var second = coordinator.Start();

        Assert(first.Token.IsCancellationRequested, "starting a newer operation did not cancel the previous operation");
        Assert(!first.IsCurrent && second.IsCurrent, "operation generation did not reject stale work");
        Assert(busyChanges.Count == 1 && busyChanges[0], "busy scope was not reference counted on entry");

        firstBusy.Dispose();
        Assert(busyChanges.Count == 1, "busy state ended while a nested operation was still active");
        secondBusy.Dispose();
        Assert(busyChanges.Count == 2 && !busyChanges[1], "busy state did not end after the final scope");

        coordinator.Cancel();
        Assert(second.Token.IsCancellationRequested, "explicit cancellation did not cancel the active operation");

        using var closingCoordinator = new OperationCoordinator();
        var closingOperation = closingCoordinator.Start();
        closingCoordinator.Dispose();
        Assert(closingOperation.Token.IsCancellationRequested && !closingOperation.IsCurrent,
            "disposing the coordinator did not cancel and invalidate screen-scoped work");
    }

internal static void TestCustomMetadataEditorValueConversion()
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
}

