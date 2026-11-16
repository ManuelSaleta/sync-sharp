using System.Text.Json.Serialization;

namespace Sync.Sharp.Models;

public sealed record ProwlarrCategory(
    int Id,
    string? Name
);

/// <summary>
/// Represents a release item returned by Prowlarr's search API.
/// Prowlarr (*Arr stack) API contracts strictly return camelCase JSON keys.
/// Explicit JsonPropertyName annotations are required for multi-word properties
/// so they deserialize reliably when Sync.Sharp uses a global snake_case naming policy.
/// </summary>
public sealed record ProwlarrRelease(
    string Guid,
    string Title,
    long Size,
    // Prowlarr API contract returns camelCase: "indexerId"
    [property: JsonPropertyName("indexerId")] int IndexerId,
    string? Indexer,
    // Prowlarr API contract returns camelCase: "infoHash"
    [property: JsonPropertyName("infoHash")] string? InfoHash,
    // Prowlarr API contract returns camelCase: "magnetUrl"
    [property: JsonPropertyName("magnetUrl")] string? MagnetUrl,
    // Prowlarr API contract returns camelCase: "downloadUrl"
    [property: JsonPropertyName("downloadUrl")] string? DownloadUrl,
    int? Seeders,
    int? Leechers,
    // Prowlarr API contract returns camelCase: "publishDate"
    [property: JsonPropertyName("publishDate")] DateTimeOffset PublishDate,
    IReadOnlyList<ProwlarrCategory>? Categories
)
{
    public string? GetBestDownloadUri() =>
        !string.IsNullOrWhiteSpace(MagnetUrl) ? MagnetUrl : DownloadUrl;
}
