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

internal static class FileCommitTests
{
internal static void TestVideoFileOperationNamingRules()
    {
        Assert(VideoFileOperationsService.GetRenameName("video.mp4", "renamed") == "renamed.mp4",
            "rename rule did not append the source extension");
        Assert(VideoFileOperationsService.GetRenameName("video.mp4", "renamed.mkv") == "renamed.mp4",
            "rename rule did not preserve the source extension when the requested extension differed");
        Assert(VideoFileOperationsService.GetRenameName("video.mp4", "renamed.MP4") == "renamed.MP4",
            "rename rule changed a matching extension");

        var firstOutput = Path.Combine(TestFixture.Root, "naming-test_ReMUX.mp4");
        var secondOutput = Path.Combine(TestFixture.Root, "naming-test_ReMUX_2.mp4");
        IoFile.WriteAllText(firstOutput, "occupied");
        IoFile.WriteAllText(secondOutput, "occupied");
        var output = VideoFileOperationsService.GetAvailableOutputPath(TestFixture.Root, "naming-test", ".mp4");
        Assert(output == Path.Combine(TestFixture.Root, "naming-test_ReMUX_3.mp4"),
            "remux output naming did not skip existing files");
    }

internal static void TestVideoCsvRowFormatting()
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

internal static void TestMetadataFileCommitModes()
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

internal static void TestExternalModificationDetection()
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

internal static void TestFailedSavePreservesOriginal()
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
}

