using Microsoft.Extensions.Logging;
using Sync.Sharp.Models;

namespace Sync.Sharp.Services;

/// <summary>
/// Discovery provider that polls Sonarr v3 API for monitored missing television episodes.
/// </summary>
public sealed class SonarrDiscoveryProvider(
    ISonarrClient sonarrClient,
    IStateStore stateStore,
    ILogger<SonarrDiscoveryProvider> logger) : IMediaDiscoveryProvider
{
    public string ProviderName => "Sonarr";

    public async Task<IReadOnlyList<DiscoveryCandidate>> GetWantedCandidatesAsync(CancellationToken ct)
    {
        if (!sonarrClient.IsConfigured)
        {
            logger.LogWarning("Sonarr discovery provider is active, but Sonarr BaseUrl/ApiKey are not configured.");
            return [];
        }

        var episodes = await sonarrClient.GetWantedMissingEpisodesAsync(ct);
        var candidates = new List<DiscoveryCandidate>();

        foreach (var ep in episodes)
        {
            if (!ep.Monitored || ep.HasFile || ep.Series is null || string.IsNullOrWhiteSpace(ep.Series.Title))
            {
                continue;
            }

            var episodeCode = $"S{ep.SeasonNumber:D2}E{ep.EpisodeNumber:D2}";
            var externalId = $"sonarr:{ep.SeriesId}:{episodeCode.ToLowerInvariant()}";
            if (stateStore.IsExternalIdProcessed(externalId))
            {
                continue;
            }

            var searchQuery = $"{ep.Series.Title.Trim()} {episodeCode}";
            candidates.Add(new DiscoveryCandidate(externalId, searchQuery, MediaCategory.Tv));
        }

        logger.LogInformation("Sonarr discovery found {Count} wanted missing episode(s).", candidates.Count);
        return candidates;
    }
}
