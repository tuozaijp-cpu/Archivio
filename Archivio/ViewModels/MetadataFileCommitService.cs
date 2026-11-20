using System;
using System.IO;

namespace Archivio.ViewModels;

/// <summary>メディアファイルの作業コピーとコミットを担当する。</summary>
internal sealed class FileCommitService : IDisposable
{
    private readonly string _targetPath;
    private readonly bool _directWrite;
    private readonly FileVersion _originalVersion;
    private bool _committed;

    private FileCommitService(string targetPath, bool directWrite, FileVersion originalVersion, string workingPath)
    {
        _targetPath = targetPath;
        _directWrite = directWrite;
        _originalVersion = originalVersion;
        WorkingPath = workingPath;
    }

    public string WorkingPath { get; }
    public bool IsDirectWrite => _directWrite;

    public static FileCommitService Open(string targetPath, bool directWrite)
    {
        var version = ReadVersion(targetPath);
        if (directWrite)
        {
            return new FileCommitService(targetPath, true, version, targetPath);
        }

        var directory = Path.GetDirectoryName(targetPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("対象ファイルの保存先を特定できません。");
        }

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileNameWithoutExtension(targetPath)}.archivio-{Guid.NewGuid():N}{Path.GetExtension(targetPath)}");
        File.Copy(targetPath, temporaryPath, overwrite: false);
        return new FileCommitService(targetPath, false, version, temporaryPath);
    }

    public void Commit()
    {
        if (_directWrite)
        {
            _committed = true;
            return;
        }

        var current = ReadVersion(_targetPath);
        if (current != _originalVersion)
        {
            throw new InvalidOperationException("保存中に外部アプリケーションがファイルを変更したため、上書きしませんでした。再読み込みしてください。");
        }

        File.Replace(WorkingPath, _targetPath, null, ignoreMetadataErrors: true);
        _committed = true;
    }

    public void Dispose()
    {
        if (_directWrite || _committed)
        {
            if (!_directWrite && File.Exists(WorkingPath))
            {
                TryDelete(WorkingPath);
            }
            return;
        }

        TryDelete(WorkingPath);
    }

    private static FileVersion ReadVersion(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new FileNotFoundException("対象ファイルが存在しません。", path);
        }

        return new FileVersion(info.Length, info.LastWriteTimeUtc);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("保存用一時ファイルの削除に失敗しました", ex, path);
        }
    }

    private readonly record struct FileVersion(long Length, DateTime LastWriteTimeUtc);
}
