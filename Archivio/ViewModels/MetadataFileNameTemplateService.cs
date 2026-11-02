using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using Archivio.Models;

namespace Archivio.ViewModels;

public sealed class MetadataFileNameResult
{
    public bool Succeeded { get; init; }
    public string FileName { get; init; } = string.Empty;
    public IReadOnlyList<string> InvalidIds { get; init; } = Array.Empty<string>();
}

/// <summary>Builds a filename from stable metadata IDs without depending on XAML or Page state.</summary>
public sealed class MetadataFileNameTemplateService
{
    public MetadataFileNameResult Build(VideoFileItem video, string? template)
    {
        ArgumentNullException.ThrowIfNull(video);
        if (string.IsNullOrWhiteSpace(template))
        {
            return new MetadataFileNameResult();
        }

        var invalidIds = new List<string>();
        var result = new StringBuilder();
        for (var index = 0; index < template.Length;)
        {
            if (template[index] == '\\')
            {
                var end = index + 1;
                while (end < template.Length && template[end] is not ('[' or ']' or '{' or '}' or '\\')) end++;
                MetadataFieldValueResolver.Append(video, template[(index + 1)..end], result, invalidIds);
                index = end;
                continue;
            }

            if (template[index] == '{')
            {
                var close = template.IndexOf('}', index + 1);
                if (close < 0)
                {
                    result.Append(template[index..]);
                    break;
                }

                var token = template[(index + 1)..close].Trim();
                var preserveBraces = token.StartsWith('\\');
                if (preserveBraces) token = token[1..];
                if (MetadataFieldValueResolver.TryResolve(video, token, out _))
                {
                    if (preserveBraces) result.Append('{');
                    MetadataFieldValueResolver.Append(video, token, result, invalidIds);
                    if (preserveBraces) result.Append('}');
                }
                else
                {
                    result.Append(template[index..(close + 1)]);
                    if (!string.IsNullOrWhiteSpace(token)) invalidIds.Add(token);
                }

                index = close + 1;
                continue;
            }

            result.Append(template[index++]);
        }

        var sanitized = FileNameSanitizer.Sanitize(result.ToString());
        if (string.IsNullOrWhiteSpace(sanitized) || sanitized is "." or ".." || invalidIds.Count > 0)
        {
            return new MetadataFileNameResult { InvalidIds = invalidIds };
        }

        return new MetadataFileNameResult
        {
            Succeeded = true,
            FileName = sanitized + System.IO.Path.GetExtension(video.FileName),
            InvalidIds = invalidIds
        };
    }
}

public static class MetadataFieldValueResolver
{
    private static readonly IReadOnlyDictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["出演者0"] = MetadataFieldIds.Cast + ".0",
        ["タイトル"] = MetadataFieldIds.WorkTitle,
        ["品番"] = MetadataFieldIds.CatalogNumber
    };

    public static bool TryResolve(VideoFileItem video, string? token, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(token)) return false;
        token = Aliases.TryGetValue(token.Trim(), out var alias) ? alias : token.Trim();

        if (TryParseCastToken(token, out var castIndex, out var castPart))
        {
            var cast = video.StructuredMetadata.PeopleAndStaff.Cast.OrderBy(item => item.Order).ToList();
            if (castIndex < 0 || castIndex >= cast.Count)
            {
                return MetadataFieldCatalog.FindById(MetadataFieldIds.Cast) is not null;
            }

            var credit = cast[castIndex];
            value = castPart switch
            {
                "character" or "role" => credit.CharacterName,
                "lead" => credit.IsLead ? "true" : "false",
                _ => credit.PersonName
            };
            return true;
        }

        var definition = MetadataFieldCatalog.FindById(token);
        var indexed = false;
        var index = -1;
        if (definition is null)
        {
            var separator = token.LastIndexOf('.');
            if (separator > 0 && int.TryParse(token[(separator + 1)..], out index))
            {
                token = token[..separator];
                definition = MetadataFieldCatalog.FindById(token);
                indexed = definition is not null;
            }
        }

        if (definition is null) return false;
        if (definition.IsCustom)
        {
            if (video.StructuredMetadata.CustomFields.TryGetValue(definition.CustomFieldId ?? definition.Id, out var customValue))
            {
                value = Format(customValue);
            }
            return true;
        }

        if (!definition.CanGenerateFileName) return true;
        var path = definition.DetailsBindingPath ?? definition.ListBindingPath;
        if (string.IsNullOrWhiteSpace(path)) return true;
        if (!TryReadPropertyPath(video, path, out var rawValue)) return true;
        if (indexed) rawValue = GetIndexedValue(rawValue, index);
        value = Format(rawValue);
        return true;
    }

    internal static void Append(VideoFileItem video, string id, StringBuilder output, ICollection<string> invalidIds)
    {
        if (TryResolve(video, id, out var value)) output.Append(value);
        else invalidIds.Add(id);
    }

    private static bool TryParseCastToken(string token, out int index, out string part)
    {
        index = -1;
        part = string.Empty;
        const string prefix = MetadataFieldIds.CastPrefix;
        if (!token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var parts = token[prefix.Length..].Split('.', 2);
        if (!int.TryParse(parts[0], out index)) return false;
        part = parts.Length == 2 ? parts[1].ToLowerInvariant() : "person";
        return true;
    }

    private static bool TryReadPropertyPath(object source, string path, out object? value)
    {
        value = source;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (value is null) return false;
            var property = value.GetType().GetProperty(segment, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (property is null) { value = null; return false; }
            value = property.GetValue(value);
        }
        return true;
    }

    private static object? GetIndexedValue(object? value, int index)
    {
        if (index < 0) return null;
        if (value is IList list) return index < list.Count ? list[index] : null;
        if (value is IEnumerable<string> strings) return strings.Skip(index).FirstOrDefault();
        if (value is string text)
        {
            return text.Split(new[] { ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim()).Skip(index).FirstOrDefault();
        }
        return null;
    }

    private static string Format(object? value)
    {
        if (value is null) return string.Empty;
        if (value is MetadataValue metadataValue)
        {
            return metadataValue.Kind switch
            {
                MetadataValueKind.Date => metadataValue.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
                MetadataValueKind.Integer => metadataValue.Integer?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                MetadataValueKind.Decimal => metadataValue.Decimal?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                MetadataValueKind.Rating => metadataValue.Rating?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                MetadataValueKind.Boolean => metadataValue.Boolean?.ToString().ToLowerInvariant() ?? string.Empty,
                MetadataValueKind.MultipleText => string.Join("; ", metadataValue.TextValues),
                MetadataValueKind.Image or MetadataValueKind.ImageCollection => string.Empty,
                _ => string.IsNullOrWhiteSpace(metadataValue.Text) ? string.Join("; ", metadataValue.TextValues) : metadataValue.Text
            };
        }
        if (value is DateTimeOffset date) return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (value is DateTime dateTime) return dateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (value is TimeSpan duration) return duration.ToString("c", CultureInfo.InvariantCulture);
        if (value is string textValue) return textValue;
        if (value is IEnumerable enumerable)
        {
            return string.Join("; ", enumerable.Cast<object?>().Select(Format).Where(item => !string.IsNullOrWhiteSpace(item)));
        }
        return value is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty
            : value.ToString() ?? string.Empty;
    }
}

public static class FileNameSanitizer
{
    private static readonly HashSet<char> WindowsInvalidCharacters = new("\\/:*?\"<>|".ToCharArray());

    public static string Sanitize(string value)
    {
        var invalid = new HashSet<char>(System.IO.Path.GetInvalidFileNameChars().Concat(WindowsInvalidCharacters));
        return new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray())
            .Trim().TrimEnd('.', ' ');
    }
}
