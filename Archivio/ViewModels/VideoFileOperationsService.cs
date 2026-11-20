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

    internal sealed class VideoFileOperationsService : IVideoFileOperationsService
    {
        internal static readonly TimeSpan RemuxTimeout = TimeSpan.FromMinutes(10);

        public async Task RenameAsync(StorageFile file, string newName)
        {
            await file.RenameAsync(GetRenameName(file.Name, newName), NameCollisionOption.FailIfExists);
        }

        internal static string GetRenameName(string currentName, string requestedName)
        {
            var originalExtension = Path.GetExtension(currentName);
            var requestedExtension = Path.GetExtension(requestedName);
            if (string.IsNullOrWhiteSpace(requestedExtension)
                || !string.Equals(originalExtension, requestedExtension, StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFileNameWithoutExtension(requestedName) + originalExtension;
            }

            return requestedName;
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

            var result = await ProcessRunner.RunAsync(startInfo, RemuxTimeout);
            if (result.ExitCode != 0)
            {
                throw new FfmpegExecutionException(result.ExitCode, result.StandardError);
            }

            return await StorageFile.GetFileFromPathAsync(outputPath);
        }

        internal static string GetAvailableOutputPath(string directory, string baseName, string extension)
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

            startInfo.ArgumentList.Add(outputPath);
            return startInfo;
        }
    }

}
