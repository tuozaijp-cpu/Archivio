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

internal static class ReadOnlyFormatTests
{
internal static void TestOtherFormatReadOnly()
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
        Assert(result.ErrorKind == MetadataSaveErrorKind.UnsupportedFormat
            && result.Errors.Any(error => error.Kind == MetadataSaveErrorKind.UnsupportedFormat),
            "unsupported format was not represented as a typed result");
        Assert(beforeBytes.SequenceEqual(IoFile.ReadAllBytes(path)), "read-only format bytes changed");
        Assert(store.LoadAsync(file).GetAwaiter().GetResult().Metadata.Title == "Existing audio title", "existing tag could not be read");
    }
}

