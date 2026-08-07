using System;
using System.Collections.Generic;
using System.Linq;
using Archivio.Models;

namespace Archivio.ViewModels
{
    /// <summary>
    /// 動画一覧の絞り込み（フィルター）および並び替え（ソート）ロジックを管理するクラス。
    /// </summary>
    public sealed class VideoListFilterManager
    {
        private readonly Dictionary<string, HashSet<string>> _columnCheckedValues = new(StringComparer.OrdinalIgnoreCase);

        public string FilterText { get; set; } = string.Empty;
        public string SortColumn { get; set; } = string.Empty;
        public bool IsSortAscending { get; set; } = true;

        public void Sort(string column, bool ascending)
        {
            SortColumn = column;
            IsSortAscending = ascending;
        }

        public HashSet<string>? GetColumnFilterValues(string propertyName)
        {
            return _columnCheckedValues.TryGetValue(propertyName, out var set) ? set : null;
        }

        public void SetColumnFilterValues(string propertyName, List<string> allowedValues)
        {
            _columnCheckedValues[propertyName] = new HashSet<string>(allowedValues, StringComparer.OrdinalIgnoreCase);
        }

        public void ClearColumnFilter(string propertyName)
        {
            _columnCheckedValues.Remove(propertyName);
        }

        public void ClearAllFilters()
        {
            FilterText = string.Empty;
            SortColumn = string.Empty;
            _columnCheckedValues.Clear();
        }

        public List<string> GetUniqueValuesForProperty(IEnumerable<VideoFileItem> source, string propertyName)
        {
            return source.Select(v => GetPropertyValueString(v, propertyName))
                         .Where(s => !string.IsNullOrWhiteSpace(s))
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(s => s)
                         .ToList();
        }

        public IEnumerable<VideoFileItem> Apply(IEnumerable<VideoFileItem> source)
        {
            var filtered = source;

            // 1. グローバルフィルター（キーワード検索）
            if (!string.IsNullOrWhiteSpace(FilterText))
            {
                var search = FilterText.Trim().ToLowerInvariant();
                filtered = filtered.Where(v =>
                    v.FileName.ToLowerInvariant().Contains(search) ||
                    v.Title.ToLowerInvariant().Contains(search) ||
                    v.Participants.ToLowerInvariant().Contains(search) ||
                    v.CatalogNumber.ToLowerInvariant().Contains(search) ||
                    v.Publisher.ToLowerInvariant().Contains(search) ||
                    v.ContentDistributor.ToLowerInvariant().Contains(search) ||
                    v.Category.ToLowerInvariant().Contains(search) ||
                    v.Comment.ToLowerInvariant().Contains(search)
                );
            }

            // 2. カラム別フィルター（チェックボックス選択）
            foreach (var colFilter in _columnCheckedValues)
            {
                var propertyName = colFilter.Key;
                var allowedValues = colFilter.Value;
                if (allowedValues != null)
                {
                    filtered = filtered.Where(v =>
                    {
                        var propVal = GetPropertyValueString(v, propertyName);
                        return allowedValues.Contains(propVal);
                    });
                }
            }

            // 3. ソート処理
            if (!string.IsNullOrWhiteSpace(SortColumn))
            {
                Func<VideoFileItem, object> keySelector = SortColumn switch
                {
                    "StatusText" => v => v.StatusText,
                    "FileName" => v => v.FileName,
                    "FileSizeText" => v => v.FileSizeBytes, // ファイルサイズ（数値）でソート
                    "Title" => v => v.Title,
                    "Participants" => v => v.Participants,
                    "ReleaseDateText" => v => v.ReleaseDate,
                    "CatalogNumber" => v => v.CatalogNumber,
                    "Rating" => v => v.RatingStarsIndex,
                    "Publisher" => v => v.Publisher,
                    "ContentDistributor" => v => v.ContentDistributor,
                    "Category" => v => v.Category,
                    "Comment" => v => v.Comment,
                    "Duration" => v => v.DurationValue ?? TimeSpan.Zero, // 再生時間（数値/TimeSpan）でソート
                    _ => v => v.FileName
                };

                filtered = IsSortAscending
                    ? filtered.OrderBy(keySelector)
                    : filtered.OrderByDescending(keySelector);
            }

            return filtered;
        }

        private static string GetPropertyValueString(VideoFileItem item, string propertyName)
        {
            return propertyName switch
            {
                "StatusText" => item.StatusText,
                "FileName" => item.FileName,
                "FileSizeText" => item.FileSizeText,
                "Title" => item.Title,
                "Participants" => item.Participants,
                "ReleaseDateText" => item.ReleaseDateText,
                "CatalogNumber" => item.CatalogNumber,
                "Rating" => item.RatingStarsText,
                "Publisher" => item.Publisher,
                "ContentDistributor" => item.ContentDistributor,
                "Category" => item.Category,
                "Comment" => item.Comment,
                "Duration" => item.Duration,
                "FrameWidth" => item.FrameWidth,
                "FrameHeight" => item.FrameHeight,
                "FrameRate" => item.FrameRate,
                "VideoBitrate" => item.VideoBitrate,
                "VideoCompression" => item.VideoCompression,
                "AudioSampleRate" => item.AudioSampleRate,
                "AudioBitrate" => item.AudioBitrate,
                "AudioFormat" => item.AudioFormat,
                _ => string.Empty
            };
        }
    }
}
