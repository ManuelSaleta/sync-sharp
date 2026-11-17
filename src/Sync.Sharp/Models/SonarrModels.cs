using System.Text.Json.Serialization;

namespace Sync.Sharp.Models;

/// <summary>
/// Represents a series entry returned by Sonarr v3 API (<c>/api/v3/series</c> or embedded in wanted/missing).
/// </summary>
public sealed record SonarrSeries(
    int Id,
    string Title,
    int Year = 0,
    [property: JsonPropertyName("tvdbId")] int TvdbId = 0,
    [property: JsonPropertyName("imdbId")] string? ImdbId = null,
    string? Path = null,
    bool Monitored = false
);

/// <summary>
/// Represents an episode record returned by Sonarr v3 API (<c>/api/v3/wanted/missing</c>).
/// </summary>
public sealed record SonarrEpisode(
    int Id,
    [property: JsonPropertyName("seriesId")] int SeriesId,
    [property: JsonPropertyName("seasonNumber")] int SeasonNumber,
    [property: JsonPropertyName("episodeNumber")] int EpisodeNumber,
    string? Title = null,
    [property: JsonPropertyName("hasFile")] bool HasFile = false,
    bool Monitored = false,
    [property: JsonPropertyName("airDateUtc")] DateTimeOffset? AirDateUtc = null,
    SonarrSeries? Series = null
);

public sealed record SonarrWantedMissingResponse(
    int Page,
    [property: JsonPropertyName("pageSize")] int PageSize,
    [property: JsonPropertyName("totalRecords")] int TotalRecords,
    IReadOnlyList<SonarrEpisode> Records
);

public sealed record SonarrCommandRequest(
    string Name,
    string? Path = null
);
