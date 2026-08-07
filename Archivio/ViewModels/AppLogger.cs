using System;
using System.IO;

namespace Archivio.ViewModels
{
    /// <summary>診断用のローカルログ。例外と対象パスのみを記録し、動画内容やメタデータ値は記録しない。</summary>
    public static class AppLogger
    {
        private static readonly object SyncRoot = new();
        private static readonly string LogFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Archivio",
            "Archivio.log");

        public static void Error(string operation, Exception exception, string? targetPath = null)
        {
            try
            {
                var target = string.IsNullOrWhiteSpace(targetPath) ? string.Empty : $"{Environment.NewLine}対象: {targetPath}";
                var entry = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] {operation}{target}{Environment.NewLine}{exception}{Environment.NewLine}";
                lock (SyncRoot)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!);
                    File.AppendAllText(LogFile, entry);
                }
            }
            catch
            {
                // ログ失敗が本来の操作を妨げないようにする。
            }
        }
    }
}
