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

internal static class TestProcessRunner
{
internal static void RunProcessRunnerLifecycle()
    {
        var success = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        success.ArgumentList.Add("/c");
        success.ArgumentList.Add("echo runner-output & echo runner-error 1>&2");
        var result = ProcessRunner.RunAsync(success, TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        Assert(Encoding.UTF8.GetString(result.StandardOutput).Contains("runner-output", StringComparison.OrdinalIgnoreCase),
            "process runner did not capture stdout");
        Assert(result.StandardError.Contains("runner-error", StringComparison.OrdinalIgnoreCase),
            "process runner did not capture stderr");

        var timeout = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        timeout.ArgumentList.Add("/c");
        timeout.ArgumentList.Add("ping 127.0.0.1 -n 30 > nul");
        var timedOut = false;
        try
        {
            ProcessRunner.RunAsync(timeout, TimeSpan.FromMilliseconds(100)).GetAwaiter().GetResult();
        }
        catch (FfmpegProcessTimeoutException)
        {
            timedOut = true;
        }

        Assert(timedOut, "process runner did not distinguish timeout");
    }

    internal static void RunFfmpeg(params string[] arguments)
    {
        var start = new ProcessStartInfo("ffmpeg") { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = false, CreateNoWindow = true };
        start.ArgumentList.Add("-y");
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("ffmpeg did not start");
        var stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(30_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new InvalidOperationException("ffmpeg fixture generation timed out after 30 seconds");
        }
        if (process.ExitCode != 0) throw new InvalidOperationException($"ffmpeg failed: {stderr}");
    }

    internal static bool DetectFfmpeg()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("ffmpeg", "-version")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });
            if (process is null || !process.WaitForExit(5_000))
            {
                try { process?.Kill(entireProcessTree: true); } catch { }
                return false;
            }

            return process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}

