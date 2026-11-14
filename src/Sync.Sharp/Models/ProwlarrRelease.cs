namespace Sync.Sharp.Models;

public sealed record ProwlarrCategory(
    int Id,
    string? Name
);

public sealed record ProwlarrRelease(
    string Guid,
    string Title,
    long Size,
    int IndexerId,
    string? Indexer,
    string? InfoHash,
    string? MagnetUrl,
    string? DownloadUrl,
    int? Seeders,
    int? Leechers,
    DateTimeOffset PublishDate,
    IReadOnlyList<ProwlarrCategory>? Categories
)
{
    public string? GetBestDownloadUri() =>
        !string.IsNullOrWhiteSpace(MagnetUrl) ? MagnetUrl : DownloadUrl;
}
