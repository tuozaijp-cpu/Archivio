using System.Diagnostics;
using System.ComponentModel;

namespace Archivio.ViewModels;

internal sealed record ProcessRunResult(byte[] StandardOutput, string StandardError, int ExitCode);

internal sealed class FfmpegProcessStartException : Exception
{
    public FfmpegProcessStartException()
        : base("FFmpeg process could not be started.")
    {
    }

    public FfmpegProcessStartException(Exception innerException)
        : base("FFmpeg process could not be started.", innerException)
    {
    }
}

internal sealed class FfmpegExecutionException : Exception
{
    public int ExitCode { get; }
    public string ErrorText { get; }

    public FfmpegExecutionException(int exitCode, string errorText)
        : base($"FFmpeg exited with code {exitCode}.")
    {
        ExitCode = exitCode;
        ErrorText = errorText;
    }
}

internal sealed class FfmpegProcessTimeoutException : TimeoutException
{
    public TimeSpan Timeout { get; }

    public FfmpegProcessTimeoutException(TimeSpan timeout)
        : base($"FFmpeg did not finish within {timeout}.")
    {
        Timeout = timeout;
    }
}

internal static class ProcessRunner
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    internal static async Task<ProcessRunResult> RunAsync(
        ProcessStartInfo startInfo,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        using var process = Start(startInfo);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        var outputTask = ReadOutputAsync(process);
        var errorTask = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            await StopAsync(process, outputTask, errorTask);

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new FfmpegProcessTimeoutException(timeout);
        }

        var output = await outputTask;
        var error = await errorTask;
        return new ProcessRunResult(output, error, process.ExitCode);
    }

    private static Process Start(ProcessStartInfo startInfo)
    {
        try
        {
            return Process.Start(startInfo)
                ?? throw new FfmpegProcessStartException(new InvalidOperationException("Process.Start returned null."));
        }
        catch (FfmpegProcessStartException)
        {
            throw;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            throw new FfmpegProcessStartException(ex);
        }
    }

    private static async Task<byte[]> ReadOutputAsync(Process process)
    {
        using var output = new MemoryStream();
        await process.StandardOutput.BaseStream.CopyToAsync(output);
        return output.ToArray();
    }

    private static async Task StopAsync(
        Process process,
        Task<byte[]> outputTask,
        Task<string> errorTask)
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

        var exitTask = process.WaitForExitAsync();
        await Task.WhenAny(exitTask, Task.Delay(StopTimeout));
        if (exitTask.IsCompleted)
        {
            await exitTask;
        }

        var drainTask = Task.WhenAll(outputTask, errorTask);
        await Task.WhenAny(drainTask, Task.Delay(StopTimeout));
        if (drainTask.IsCompleted)
        {
            await drainTask;
        }
    }
}
