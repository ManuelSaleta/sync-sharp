using System.Text.Json.Serialization;

namespace Sync.Sharp.Models;

/// <summary>
/// Strongly-typed media category domain primitive representing media classification (Movies or TV).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<MediaCategory>))]
public enum MediaCategory
{
    [JsonStringEnumMemberName("movies")]
    Movies,
    [JsonStringEnumMemberName("tv")]
    Tv
}

/// <summary>
/// Parsing, normalization, and comparison routines for <see cref="MediaCategory"/>.
/// </summary>
public static class MediaCategoryParser
{
    /// <summary>
    /// Parses a raw string into a <see cref="MediaCategory"/>. Throws <see cref="ArgumentException"/> if invalid.
    /// </summary>
    public static MediaCategory Parse(string? value)
    {
        if (TryParse(value, out var category))
        {
            return category;
        }

        throw new ArgumentException($"Invalid or unsupported media category: '{value}'", nameof(value));
    }

    /// <summary>
    /// Attempts to parse a raw category string into a <see cref="MediaCategory"/>.
    /// Handles variants: "tv", "show", "shows", "movies", "movie" (case-insensitive).
    /// </summary>
    public static bool TryParse(string? value, out MediaCategory category)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            category = MediaCategory.Movies;
            return false;
        }

        var trimmed = value.Trim();

        if (string.Equals(trimmed, "tv", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "show", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "shows", StringComparison.OrdinalIgnoreCase))
        {
            category = MediaCategory.Tv;
            return true;
        }

        if (string.Equals(trimmed, "movies", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "movie", StringComparison.OrdinalIgnoreCase))
        {
            category = MediaCategory.Movies;
            return true;
        }

        category = MediaCategory.Movies;
        return false;
    }

    /// <summary>
    /// Parses a category string, returning <paramref name="defaultCategory"/> if parsing fails.
    /// </summary>
    public static MediaCategory FromStringOrDefault(string? value, MediaCategory defaultCategory = MediaCategory.Movies) =>
        TryParse(value, out var category) ? category : defaultCategory;

    /// <summary>
    /// Normalizes any raw category string into standard canonical lowercase ("movies" or "tv").
    /// </summary>
    public static string Normalize(string? value) =>
        FromStringOrDefault(value).ToNormalizedString();
}

/// <summary>
/// Extension methods for <see cref="MediaCategory"/>.
/// </summary>
public static class MediaCategoryExtensions
{
    /// <summary>
    /// Returns the canonical normalized string ("movies" or "tv").
    /// </summary>
    public static string ToNormalizedString(this MediaCategory category) =>
        category switch
        {
            MediaCategory.Tv => "tv",
            _ => "movies"
        };

    /// <summary>
    /// Returns the Plex library search target type ("show" or "movie").
    /// </summary>
    public static string ToPlexTargetType(this MediaCategory category) =>
        category == MediaCategory.Tv ? "show" : "movie";

    /// <summary>
    /// Returns the Plex default section key ("My TV Shows" or "My Movies").
    /// </summary>
    public static string ToPlexSectionKey(this MediaCategory category) =>
        category == MediaCategory.Tv ? "My TV Shows" : "My Movies";

    public static bool IsTv(this MediaCategory category) => category == MediaCategory.Tv;
    public static bool IsMovies(this MediaCategory category) => category == MediaCategory.Movies;
}
