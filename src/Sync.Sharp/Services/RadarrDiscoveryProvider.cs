using Microsoft.Extensions.Logging;
using Sync.Sharp.Models;

namespace Sync.Sharp.Services;

/// <summary>
/// Discovery provider that polls Radarr v3 API for monitored, available movies missing a file.
/// </summary>
public sealed class RadarrDiscoveryProvider(
    IRadarrClient radarrClient,
    IStateStore stateStore,
    ILogger<RadarrDiscoveryProvider> logger) : IMediaDiscoveryProvider
{
    public string ProviderName => "Radarr";

    public async Task<IReadOnlyList<DiscoveryCandidate>> GetWantedCandidatesAsync(CancellationToken ct)
    {
        if (!radarrClient.IsConfigured)
        {
            logger.LogWarning("Radarr discovery provider is active, but Radarr BaseUrl/ApiKey are not configured.");
            return [];
        }

        var movies = await radarrClient.GetMonitoredMoviesAsync(ct);
        var candidates = new List<DiscoveryCandidate>();

        foreach (var movie in movies)
        {
            if (!movie.Monitored || movie.HasFile || !movie.IsAvailable)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(movie.Title))
            {
                continue;
            }

            var externalId = movie.TmdbId > 0
                ? $"radarr:tmdb:{movie.TmdbId}"
                : (!string.IsNullOrWhiteSpace(movie.ImdbId)
                    ? $"radarr:imdb:{movie.ImdbId}"
                    : $"radarr:id:{movie.Id}");

            if (stateStore.IsExternalIdProcessed(externalId) ||
                (!string.IsNullOrWhiteSpace(movie.ImdbId) && stateStore.IsExternalIdProcessed(movie.ImdbId)))
            {
                continue;
            }

            var searchQuery = movie.Year > 0
                ? $"{movie.Title.Trim()} {movie.Year}"
                : movie.Title.Trim();

            candidates.Add(new DiscoveryCandidate(externalId, searchQuery, MediaCategory.Movies));
        }

        logger.LogInformation("Radarr discovery found {Count} wanted missing movie(s).", candidates.Count);
        return candidates;
    }
}
