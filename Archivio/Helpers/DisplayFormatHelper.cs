using System;
using System.Globalization;
using Archivio.ViewModels;

namespace Archivio.Helpers
{
    public static class DisplayFormatHelper
    {
        public static string FormatReleaseDateDisplayText(DateTimeOffset releaseDate)
        {
            return releaseDate.Year > 1900 ? releaseDate.ToString("d", System.Globalization.CultureInfo.CurrentUICulture) : string.Empty;
        }

        public static string FormatFileSize(ulong bytes)
        {
            const double kb = 1024d;
            const double mb = kb * 1024d;
            const double gb = mb * 1024d;

            if (bytes >= gb)
            {
                return $"{bytes / gb:0.0} GB";
            }

            if (bytes >= mb)
            {
                return $"{bytes / mb:0.0} MB";
            }

            if (bytes >= kb)
            {
                return $"{bytes / kb:0.0} KB";
            }

            return $"{bytes} B";
        }

        public static TimeSpan? TryParseDuration(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var formats = new[] { @"h\:mm\:ss", @"hh\:mm\:ss", @"m\:ss", @"mm\:ss" };
            if (TimeSpan.TryParseExact(text.Trim(), formats, CultureInfo.InvariantCulture, out var duration))
            {
                return duration;
            }

            return TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out duration) ? duration : null;
        }

        public static string EscapeCsvField(string? field)
        {
            if (field is null)
            {
                return string.Empty;
            }

            if (field.Contains(',') || field.Contains('"') || field.Contains('\r') || field.Contains('\n'))
            {
                return $"\"{field.Replace("\"", "\"\"")}\"";
            }

            return field;
        }

        public static string FormatOperationMessage(VideoMetadataOperationResult result)
        {
            return string.IsNullOrWhiteSpace(result.Details)
                ? result.Message
                : $"{result.Message}{Environment.NewLine}{result.Details}";
        }
    }
}
