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

internal static class FfmpegIntegrationTests
{
internal static void TestFfmpegMediaService()
    {
        var executableDirectory = Path.Combine(TestFixture.Root, "ffmpeg-tools");
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

internal static void TestFfmpegMediaProcessing()
    {
        var service = new FfmpegMediaService();
        var ffmpegPath = service.FindFfmpegAsync(null).GetAwaiter().GetResult();
        Assert(!string.IsNullOrWhiteSpace(ffmpegPath), "FFmpeg executable could not be resolved");

        var videoPath = Path.Combine(TestFixture.Root, "sample.mp4");
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
}

