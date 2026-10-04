using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Archivio.Models;

namespace Archivio.ViewModels
{
    /// <summary>
    /// 動画一覧の絞り込み（フィルター）および並び替え（ソート）ロジックを管理するクラス。
    /// </summary>
    public sealed class VideoListFilterManager
    {
        private static readonly IReadOnlyDictionary<string, PropertyInfo> ListValueProperties = CreatePropertyMap(
            field => field.ListBindingPath);
        private static readonly IReadOnlyDictionary<string, PropertyInfo> SortValueProperties = CreatePropertyMap(
            field => field.SortBindingPath ?? field.ListBindingPath);
        private static string[] GetGlobalSearchFieldIds() => MetadataFieldCatalog.All
            .Where(field => field.IsGlobalSearchable)
            .Select(field => field.Id)
            .ToArray();
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
                filtered = filtered.Where(video => GetGlobalSearchFieldIds().Any(fieldId =>
                    GetPropertyValueString(video, fieldId).ToLowerInvariant().Contains(search)));
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
                Func<VideoFileItem, object> keySelector = SortValueProperties.TryGetValue(SortColumn, out var sortProperty)
                    ? video => sortProperty.GetValue(video)
                        ?? (MetadataFieldCatalog.FindById(SortColumn)?.ValueType == MetadataFieldValueType.Duration
                            ? TimeSpan.Zero
                            : string.Empty)
                    : MetadataFieldCatalog.FindById(SortColumn) is { IsCustom: true }
                        ? video => GetPropertyValueString(video, SortColumn)
                    : video => video.FileName;

                filtered = IsSortAscending
                    ? filtered.OrderBy(keySelector)
                    : filtered.OrderByDescending(keySelector);
            }

            return filtered;
        }

        private static Dictionary<string, PropertyInfo> CreatePropertyMap(Func<MetadataFieldDefinition, string?> getPath)
        {
            var properties = new Dictionary<string, PropertyInfo>(StringComparer.Ordinal);
            foreach (var field in MetadataFieldCatalog.All)
            {
                var path = getPath(field);
                var property = string.IsNullOrWhiteSpace(path)
                    ? null
                    : typeof(VideoFileItem).GetProperty(path);
                if (property is not null)
                {
                    properties[field.Id] = property;
                }
            }

            return properties;
        }

        private static string GetPropertyValueString(VideoFileItem item, string fieldId)
        {
            if (ListValueProperties.TryGetValue(fieldId, out var property))
            {
                return property.GetValue(item)?.ToString() ?? string.Empty;
            }

            if (MetadataFieldCatalog.FindById(fieldId) is { IsCustom: true }
                && item.StructuredMetadata.CustomFields.TryGetValue(fieldId, out var value))
            {
                return value.Text
                    ?? value.Date?.ToString("d", System.Globalization.CultureInfo.CurrentUICulture)
                    ?? value.Integer?.ToString(System.Globalization.CultureInfo.CurrentCulture)
                    ?? value.Decimal?.ToString(System.Globalization.CultureInfo.CurrentCulture)
                    ?? value.Rating?.ToString(System.Globalization.CultureInfo.CurrentCulture)
                    ?? value.Boolean?.ToString()
                    ?? string.Join("; ", value.TextValues);
            }

            return string.Empty;
        }
    }
}
