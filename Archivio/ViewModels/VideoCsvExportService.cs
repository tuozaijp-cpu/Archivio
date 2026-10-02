using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Archivio.Helpers;
using Windows.Storage;

namespace Archivio.ViewModels
{
    internal sealed record VideoCsvRow(
        string FileName,
        string FileSize,
        string Title,
        string Participants,
        string ReleaseDate,
        string CatalogNumber,
        string Rating,
        string Publisher,
        string Label,
        string Category,
        string Comment,
        string Duration,
        string FrameWidth,
        string FrameHeight,
        string FrameRate,
        string VideoBitrate,
        string VideoCompression,
        string AudioSampleRate,
        string AudioBitrate,
        string AudioFormat)
    {
        public IEnumerable<string?> ToFields()
        {
            return new string?[]
            {
                FileName, FileSize, Title, Participants, ReleaseDate, CatalogNumber,
                Rating, Publisher, Label, Category, Comment, Duration, FrameWidth,
                FrameHeight, FrameRate, VideoBitrate, VideoCompression, AudioSampleRate,
                AudioBitrate, AudioFormat
            };
        }
    }

    internal interface IVideoCsvExportService
    {
        Task ExportAsync(StorageFile file, IReadOnlyList<VideoCsvRow> rows);
    }

    internal sealed class VideoCsvExportService : IVideoCsvExportService
    {
        public async Task ExportAsync(StorageFile file, IReadOnlyList<VideoCsvRow> rows)
        {
            await using var stream = await file.OpenStreamForWriteAsync();
            stream.SetLength(0);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(true));

            await writer.WriteLineAsync(FormatRow(GetHeaders()));
            foreach (var row in rows)
            {
                await writer.WriteLineAsync(FormatRow(row.ToFields()));
            }
        }

        private static string[] GetHeaders()
        {
            return new[]
            {
                LanguageManager.GetString("Col_FileName"), LanguageManager.GetString("Col_FileSizeText"),
                LanguageManager.GetString("Col_Title"), LanguageManager.GetString("Col_Participants"),
                LanguageManager.GetString("Col_ReleaseDateText"), LanguageManager.GetString("Col_CatalogNumber"),
                LanguageManager.GetString("Col_Rating"), LanguageManager.GetString("Col_Publisher"),
                LanguageManager.GetString("Col_ContentDistributor"), LanguageManager.GetString("Col_Category"),
                LanguageManager.GetString("Col_Comment"), LanguageManager.GetString("Col_Duration"),
                LanguageManager.GetString("Col_FrameWidth"), LanguageManager.GetString("Col_FrameHeight"),
                LanguageManager.GetString("Col_FrameRate"), LanguageManager.GetString("Col_VideoBitrate"),
                LanguageManager.GetString("Col_VideoCompression"), LanguageManager.GetString("Col_AudioSampleRate"),
                LanguageManager.GetString("Col_AudioBitrate"), LanguageManager.GetString("Col_AudioFormat")
            };
        }

        private static string FormatRow(IEnumerable<string?> fields)
        {
            return string.Join(",", fields.Select(DisplayFormatHelper.EscapeCsvField));
        }
    }

}
