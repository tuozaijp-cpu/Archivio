using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using IoFile = System.IO.File;

namespace Archivio.CompatibilityTests;

internal static class TestFixture
{
    internal static string Root { get; private set; } = string.Empty;
    internal static bool FfmpegAvailable { get; private set; }
    internal static void Initialize(bool ffmpegAvailable)
    {
        FfmpegAvailable = ffmpegAvailable;
        Root = Path.Combine(Path.GetTempPath(), $"Archivio-compat-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Root);
    }
    internal static string CopyFixture(string name)
    {
        var source = Path.Combine(Root, name);
        var copy = Path.Combine(Root, $"copy-{Guid.NewGuid():N}{Path.GetExtension(name)}");
        IoFile.Copy(source, copy);
        return copy;
    }
    internal static StorageFile GetStorageFile(string path, bool createIfMissing = false)
    {
        if (createIfMissing && !IoFile.Exists(path)) IoFile.WriteAllBytes(path, []);
        return Task.Run(async () => await StorageFile.GetFileFromPathAsync(path)).GetAwaiter().GetResult();
    }
    internal static bool Cleanup()
    {
        for (var attempt = 0; attempt < 3 && Directory.Exists(Root); attempt++)
        {
            try { Directory.Delete(Root, recursive: true); return true; }
            catch when (attempt < 2) { Thread.Sleep(100); }
        }
        return !Directory.Exists(Root);
    }
}

