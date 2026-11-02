using System;
using System.Collections.Generic;

namespace Archivio.Models;

/// <summary>Stable metadata IDs shared by UI, persistence, filters and filename templates.</summary>
internal static class MetadataFieldIds
{
    internal const string WorkTitle = "work.title";
    internal const string Participants = "credits.participants";
    internal const string ReleaseDate = "dates.release";
    internal const string CatalogNumber = "identifiers.catalog-number";
    internal const string Rating = "evaluation.rating";
    internal const string Publisher = "publication.publisher";
    internal const string Label = "publication.label";
    internal const string Category = "work.category";
    internal const string Comment = "work.comment";
    internal const string Cast = "credits.cast";
    internal const string CastPrefix = "credits.cast.";

    internal static readonly IReadOnlySet<string> StandardTagIds = new HashSet<string>(StringComparer.Ordinal)
    {
        WorkTitle, Participants, Comment, Category, CatalogNumber, Publisher, Label, "artwork.cover"
    };

    internal static bool IsArchivioPayloadId(string? fieldId)
    {
        return fieldId is
            "work.title" or "work.subtitle" or "work.original-title" or "work.alternate-titles"
            or "work.description" or "work.synopsis" or "work.keywords" or "work.genres" or "work.content-type"
            or "credits.cast" or "credits.crew" or "credits.production-companies" or "credits.distributors" or "credits.publishers"
            or "series.name" or "series.season-number" or "series.episode-number" or "series.episode-title"
            or "series.episode-id" or "series.network" or "series.season-count" or "series.episode-count"
            or "series.publication-year" or "series.part-number"
            or "dates.release" or "dates.recording" or "dates.filming" or "dates.purchased"
            or "dates.archivio-registered" or "dates.archivio-updated"
            or "evaluation.content-rating" or "evaluation.personal-notes" or "evaluation.view-count"
            or "evaluation.favorite" or "evaluation.watched" or "evaluation.watched-at"
            or "evaluation.playback-position" or "evaluation.resume-position" or "evaluation.user-tags"
            or "identifiers.barcode" or "identifiers.imdb" or "identifiers.tmdb" or "identifiers.tvdb"
            or "identifiers.archivio-id" or "identifiers.source-file-id"
            or "acquisition.price" or "acquisition.currency" or "acquisition.seller" or "acquisition.url"
            or "acquisition.source" or "acquisition.original-media"
            or "rights.copyright-holder" or "rights.license" or "rights.source"
            or "location.filming" or "location.country"
            || fieldId?.StartsWith("custom.", StringComparison.Ordinal) == true;
    }
}
