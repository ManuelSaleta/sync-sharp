using Sync.Sharp.Models;

namespace Sync.Sharp.Services;

/// <summary>
/// Represents a media item candidate emitted by a discovery provider for acquisition.
/// </summary>
public sealed record DiscoveryCandidate(string ExternalId, string SearchQuery, MediaCategory Category);

/// <summary>
/// Pluggable media discovery provider abstraction (Radarr, Sonarr, Jarvis webhook, or Trending feeds).
/// </summary>
public interface IMediaDiscoveryProvider
{
    string ProviderName { get; }
    Task<IReadOnlyList<DiscoveryCandidate>> GetWantedCandidatesAsync(CancellationToken ct);
}
