using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Archivio.ViewModels;

namespace Archivio.Models;

/// <summary>
/// The single comparison policy for editable metadata. It deliberately compares both
/// legacy display fields and the canonical document while treating null/empty values alike.
/// </summary>
public static class MetadataChangeComparer
{
    public static bool AreEquivalent(VideoMetadataSnapshot? left, VideoMetadataSnapshot? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return EqualText(left.Title, right.Title)
            && EqualText(left.Participants, right.Participants)
            && EqualText(left.CatalogNumber, right.CatalogNumber)
            && EqualText(left.Category, right.Category)
            && EqualText(left.Rating, right.Rating)
            && EqualText(left.ContentDistributor, right.ContentDistributor)
            && EqualText(left.Publisher, right.Publisher)
            && EqualText(left.Comment, right.Comment)
            && EqualDate(left.ReleaseDate, right.ReleaseDate)
            && DocumentsAreEquivalent(left.StructuredMetadata, right.StructuredMetadata);
    }

    public static string SerializeCanonical(MetadataDocument? document)
    {
        var node = JsonSerializer.SerializeToNode(document ?? new MetadataDocument());
        return Normalize(node)?.ToJsonString(new JsonSerializerOptions { WriteIndented = false }) ?? "{}";
    }

    public static VideoMetadataSnapshot CloneSnapshot(VideoMetadataSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new VideoMetadataSnapshot
        {
            Title = snapshot.Title ?? string.Empty,
            Participants = snapshot.Participants ?? string.Empty,
            CatalogNumber = snapshot.CatalogNumber ?? string.Empty,
            Category = snapshot.Category ?? string.Empty,
            Rating = snapshot.Rating ?? string.Empty,
            ContentDistributor = snapshot.ContentDistributor ?? string.Empty,
            Publisher = snapshot.Publisher ?? string.Empty,
            Duration = snapshot.Duration ?? string.Empty,
            FrameWidth = snapshot.FrameWidth ?? string.Empty,
            FrameHeight = snapshot.FrameHeight ?? string.Empty,
            FrameRate = snapshot.FrameRate ?? string.Empty,
            VideoBitrate = snapshot.VideoBitrate ?? string.Empty,
            VideoCompression = snapshot.VideoCompression ?? string.Empty,
            AudioSampleRate = snapshot.AudioSampleRate ?? string.Empty,
            AudioBitrate = snapshot.AudioBitrate ?? string.Empty,
            AudioFormat = snapshot.AudioFormat ?? string.Empty,
            Comment = snapshot.Comment ?? string.Empty,
            ReleaseDateText = snapshot.ReleaseDateText ?? string.Empty,
            ReleaseDate = snapshot.ReleaseDate,
            Technical = snapshot.Technical,
            TechnicalInfo = snapshot.TechnicalInfo,
            StructuredMetadata = snapshot.StructuredMetadata?.Clone() ?? new MetadataDocument()
        };
    }

    private static bool DocumentsAreEquivalent(MetadataDocument? left, MetadataDocument? right)
        => string.Equals(SerializeCanonical(left), SerializeCanonical(right), StringComparison.Ordinal);

    private static bool EqualText(string? left, string? right)
        => string.Equals(left?.Trim() ?? string.Empty, right?.Trim() ?? string.Empty, StringComparison.Ordinal);

    private static bool EqualDate(DateTimeOffset left, DateTimeOffset right)
    {
        var leftSet = left.Year > 1900;
        var rightSet = right.Year > 1900;
        return leftSet == rightSet && (!leftSet || left.Date == right.Date);
    }

    private static JsonNode? Normalize(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonObject obj)
        {
            var result = new JsonObject();
            foreach (var property in obj.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                var value = Normalize(property.Value);
                if (value is not null)
                {
                    result[property.Key] = value;
                }
            }

            return result;
        }

        if (node is JsonArray array)
        {
            var result = new JsonArray();
            foreach (var item in array)
            {
                var value = Normalize(item);
                if (value is not null)
                {
                    result.Add(value);
                }
            }

            return result;
        }

        if (node is JsonValue valueNode && valueNode.TryGetValue<string>(out var text))
        {
            var normalized = text?.Trim() ?? string.Empty;
            return normalized.Length == 0 ? null : JsonValue.Create(normalized);
        }

        return node.DeepClone();
    }
}
