using System.Text.Json.Serialization;

namespace Sync.Sharp.Models;

/// <summary>
/// Represents a movie entry returned by Radarr v3 API (<c>/api/v3/movie</c>).
/// Explicit <see cref="JsonPropertyNameAttribute"/> annotations ensure camelCase API keys
/// deserialize cleanly under Sync.Sharp's global snake_case naming policy.
/// </summary>
public sealed record RadarrMovie(
    int Id,
    string Title,
    [property: JsonPropertyName("originalTitle")] string? OriginalTitle = null,
    int Year = 0,
    [property: JsonPropertyName("tmdbId")] int TmdbId = 0,
    [property: JsonPropertyName("imdbId")] string? ImdbId = null,
    [property: JsonPropertyName("hasFile")] bool HasFile = false,
    bool Monitored = false,
    [property: JsonPropertyName("isAvailable")] bool IsAvailable = false,
    string? Path = null,
    [property: JsonPropertyName("folderName")] string? FolderName = null,
    string? Status = null
);

public sealed record RadarrCommandRequest(
    string Name,
    string? Path = null
);
