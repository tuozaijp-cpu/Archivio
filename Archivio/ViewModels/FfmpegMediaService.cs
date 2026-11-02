using System.Diagnostics;
using System.Globalization;

namespace Archivio.ViewModels;

internal sealed class FfmpegMediaService
{
    public async Task<string?> FindFfmpegAsync(
        string? configuredFfprobe,
        CancellationToken cancellationToken = default)
    {
        var pathEnvironment = Environment.GetEnvironmentVariable("PATH");
        var pathCandidate = FindFfmpegInConfiguredPaths(configuredFfprobe, pathEnvironment);
        if (pathCandidate is not null)
        {
            return pathCandidate;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "where.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("ffmpeg.exe");

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return null;
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException) when (process.HasExited)
            {
            }

            await process.WaitForExitAsync();
            await outputTask;
            throw;
        }

        var output = await outputTask;
        if (process.ExitCode != 0)
        {
            return null;
        }

        return output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => path.Trim().Trim('"'))
            .FirstOrDefault(File.Exists);
    }

    internal static string? FindFfmpegInConfiguredPaths(string? configuredFfprobe, string? pathEnvironment)
    {
        if (!string.IsNullOrWhiteSpace(configuredFfprobe))
        {
            var configuredPath = configuredFfprobe.Trim().Trim('"');
            if (Path.GetFileName(configuredPath).Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase)
                && File.Exists(configuredPath))
            {
                return configuredPath;
            }

            var directory = Path.GetDirectoryName(configuredPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                var sibling = Path.Combine(directory, "ffmpeg.exe");
                if (File.Exists(sibling))
                {
                    return sibling;
                }
            }
        }

        foreach (var directory in (pathEnvironment ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim('"'), "ffmpeg.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
                // Ignore malformed PATH entries.
            }
        }

        return null;
    }

    public Task<byte[]> CaptureFrameAsync(
        string ffmpegPath,
        string videoPath,
        TimeSpan position,
        CancellationToken cancellationToken = default)
    {
        return ExecuteForImageAsync(
            BuildCaptureFrameStartInfo(ffmpegPath, videoPath, position),
            cancellationToken);
    }

    public Task<byte[]> CreateThumbnailSheetAsync(
        string ffmpegPath,
        string videoPath,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        return ExecuteForImageAsync(
            BuildThumbnailSheetStartInfo(ffmpegPath, videoPath, duration),
            cancellationToken);
    }

    internal static ProcessStartInfo BuildCaptureFrameStartInfo(
        string ffmpegPath,
        string videoPath,
        TimeSpan position)
    {
        var startInfo = CreateImageOutputStartInfo(ffmpegPath);
        startInfo.ArgumentList.Add("-ss");
        startInfo.ArgumentList.Add(Math.Max(0, position.TotalSeconds).ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(videoPath);
        startInfo.ArgumentList.Add("-frames:v");
        startInfo.ArgumentList.Add("1");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("image2pipe");
        startInfo.ArgumentList.Add("-c:v");
        startInfo.ArgumentList.Add("png");
        startInfo.ArgumentList.Add("pipe:1");
        return startInfo;
    }

    internal static ProcessStartInfo BuildThumbnailSheetStartInfo(
        string ffmpegPath,
        string videoPath,
        TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "A positive video duration is required.");
        }

        var startInfo = CreateImageOutputStartInfo(ffmpegPath);
        var seconds = duration.TotalSeconds;
        var filterParts = new List<string>(25);
        for (var index = 0; index < 25; index++)
        {
            var timestamp = seconds * (index + 0.5) / 25.0;
            startInfo.ArgumentList.Add("-ss");
            startInfo.ArgumentList.Add(timestamp.ToString(CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add(videoPath);
            filterParts.Add($"[{index}:v:0]trim=end_frame=1,setpts=PTS-STARTPTS,scale=320:-2[s{index}]");
        }

        var tileInputs = string.Concat(Enumerable.Range(0, 25).Select(index => $"[s{index}]"));
        var filter = string.Join(";", filterParts)
            + $";{tileInputs}concat=n=25:v=1:a=0[frames];[frames]tile=5x5:padding=4:margin=4:color=black[v]";
        startInfo.ArgumentList.Add("-filter_complex");
        startInfo.ArgumentList.Add(filter);
        startInfo.ArgumentList.Add("-map");
        startInfo.ArgumentList.Add("[v]");
        startInfo.ArgumentList.Add("-frames:v");
        startInfo.ArgumentList.Add("1");
        startInfo.ArgumentList.Add("-an");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("image2pipe");
        startInfo.ArgumentList.Add("-vcodec");
        startInfo.ArgumentList.Add("png");
        startInfo.ArgumentList.Add("pipe:1");
        return startInfo;
    }

    private static ProcessStartInfo CreateImageOutputStartInfo(string ffmpegPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-loglevel");
        startInfo.ArgumentList.Add("error");
        return startInfo;
    }

    private static async Task<byte[]> ExecuteForImageAsync(
        ProcessStartInfo startInfo,
        CancellationToken cancellationToken)
    {
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("FFmpegを起動できませんでした。");
        using var imageStream = new MemoryStream();
        var outputTask = process.StandardOutput.BaseStream.CopyToAsync(imageStream, cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException) when (process.HasExited)
            {
            }

            await process.WaitForExitAsync();
            await Task.WhenAll(outputTask, errorTask);
            throw;
        }

        await Task.WhenAll(outputTask, errorTask);
        var errorText = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new FfmpegMediaProcessingException(process.ExitCode, errorText);
        }

        if (imageStream.Length == 0)
        {
            throw new FfmpegMediaProcessingException(process.ExitCode, errorText);
        }

        return imageStream.ToArray();
    }
}

internal sealed class FfmpegMediaProcessingException : InvalidOperationException
{
    public int ExitCode { get; }
    public string ErrorText { get; }

    public FfmpegMediaProcessingException(int exitCode, string errorText)
        : base(string.IsNullOrWhiteSpace(errorText)
            ? $"FFmpeg exited with code {exitCode} without producing an image."
            : errorText.Trim())
    {
        ExitCode = exitCode;
        ErrorText = errorText;
    }
}
