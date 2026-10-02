using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;

namespace Archivio.ViewModels
{
    internal interface IVideoFileOperationsService
    {
        Task RenameAsync(StorageFile file, string newName);
        Task DeleteAsync(StorageFile file);
        Task<StorageFile> ReMuxAsync(StorageFile file);
    }

    internal sealed class FfmpegProcessStartException : Exception
    {
        public FfmpegProcessStartException() : base("FFmpeg process could not be started.")
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

    internal sealed class VideoFileOperationsService : IVideoFileOperationsService
    {
        public async Task RenameAsync(StorageFile file, string newName)
        {
            var originalExtension = Path.GetExtension(file.Name);
            var requestedExtension = Path.GetExtension(newName);
            if (string.IsNullOrWhiteSpace(requestedExtension)
                || !string.Equals(originalExtension, requestedExtension, StringComparison.OrdinalIgnoreCase))
            {
                newName = Path.GetFileNameWithoutExtension(newName) + originalExtension;
            }

            await file.RenameAsync(newName, NameCollisionOption.FailIfExists);
        }

        public async Task DeleteAsync(StorageFile file)
        {
            await file.DeleteAsync();
        }

        public async Task<StorageFile> ReMuxAsync(StorageFile file)
        {
            var inputPath = file.Path;
            var directory = Path.GetDirectoryName(inputPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidOperationException("Unable to determine the source folder.");
            }

            var baseName = Path.GetFileNameWithoutExtension(inputPath);
            var extension = Path.GetExtension(inputPath);
            var outputPath = GetAvailableOutputPath(directory, baseName, extension);
            var startInfo = CreateStartInfo(inputPath, outputPath, extension);

            using var process = Process.Start(startInfo) ?? throw new FfmpegProcessStartException();
            var errorTask = process.StandardError.ReadToEndAsync();

            try
            {
                await process.WaitForExitAsync();
            }
            catch
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                }

                await process.WaitForExitAsync();
                throw;
            }

            var errorText = await errorTask;
            if (process.ExitCode != 0)
            {
                throw new FfmpegExecutionException(process.ExitCode, errorText);
            }

            return await StorageFile.GetFileFromPathAsync(outputPath);
        }

        private static string GetAvailableOutputPath(string directory, string baseName, string extension)
        {
            var outputPath = Path.Combine(directory, baseName + "_ReMUX" + extension);
            for (var suffix = 2; File.Exists(outputPath) || Directory.Exists(outputPath); suffix++)
            {
                outputPath = Path.Combine(directory, $"{baseName}_ReMUX_{suffix}{extension}");
            }

            return outputPath;
        }

        private static ProcessStartInfo CreateStartInfo(string inputPath, string outputPath, string extension)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-n");
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add(inputPath);
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add("copy");

            if (extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".m4v", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".mov", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".3gp", StringComparison.OrdinalIgnoreCase))
            {
                startInfo.ArgumentList.Add("-movflags");
                startInfo.ArgumentList.Add("+faststart");
            }

            startInfo.ArgumentList.Add(outputPath);
            return startInfo;
        }
    }

}
