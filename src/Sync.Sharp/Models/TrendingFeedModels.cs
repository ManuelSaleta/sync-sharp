using System.Text.Json.Serialization;

namespace Sync.Sharp.Models;

/// <summary>
/// Common contract for items returned from trending media feeds.
/// </summary>
public interface ITrendingItem
{
    string? Title { get; }
    string? Name { get; }
    string? ImdbId { get; }
    string? PosterUrl { get; }
    string EffectiveTitle { get; }
    MediaCategory Category { get; }
}

public sealed record TrendingMovieItem(
    string? Title = null,
    string? Name = null,
    [property: JsonPropertyName("imdb_id")] string? ImdbId = null,
    [property: JsonPropertyName("poster_url")] string? PosterUrl = null
) : ITrendingItem
{
    [JsonIgnore]
    public string EffectiveTitle => !string.IsNullOrWhiteSpace(Title) ? Title : (Name ?? string.Empty);

    [JsonIgnore]
    public MediaCategory Category => MediaCategory.Movies;
}

public sealed record TrendingTvItem(
    string? Title = null,
    string? Name = null,
    [property: JsonPropertyName("show_name")] string? ShowName = null,
    [property: JsonPropertyName("imdb_id")] string? ImdbId = null,
    [property: JsonPropertyName("poster_url")] string? PosterUrl = null
) : ITrendingItem
{
    [JsonIgnore]
    public string EffectiveTitle => !string.IsNullOrWhiteSpace(Title)
        ? Title
        : (!string.IsNullOrWhiteSpace(Name) ? Name : (ShowName ?? string.Empty));

    [JsonIgnore]
    public MediaCategory Category => MediaCategory.Tv;
}
