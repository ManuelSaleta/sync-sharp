using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sync.Sharp.Models;
using Sync.Sharp.Models.Options;

namespace Sync.Sharp.Services;

public interface IProwlarrClient
{
    bool IsConfigured { get; }
    Task<ProwlarrRelease?> SearchBestReleaseAsync(string query, CategoryFilterConfigOpts filter, CancellationToken ct);
}

public sealed class ProwlarrClient(
    HttpClient httpClient,
    IOptions<IndexerConfigOpts> indexerOptions,
    JsonSerializerOptions jsonOptions,
    ILogger<ProwlarrClient> logger,
    IReleaseScorer? releaseScorer = null) : IProwlarrClient
{
    private readonly ILogger<ProwlarrClient> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly IndexerConfigOpts _config = indexerOptions?.Value ?? throw new ArgumentNullException(nameof(indexerOptions));
    private readonly JsonSerializerOptions _jsonOptions = jsonOptions ?? throw new ArgumentNullException(nameof(jsonOptions));
    private readonly IReleaseScorer _releaseScorer = releaseScorer ?? new ReleaseScorer();

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_config.ApiKey) &&
        !_config.ApiKey.StartsWith("UNCONFIGURED", StringComparison.OrdinalIgnoreCase);

    public async Task<ProwlarrRelease?> SearchBestReleaseAsync(
        string query,
        CategoryFilterConfigOpts filter,
        CancellationToken ct)
    {
        if (!IsConfigured)
        {
            _logger.LogWarning("Prowlarr API key is not configured; skipping search for '{Query}'.", query);
            return null;
        }

        var categoryQuery = string.Join("&", filter.CategoryIds.Select(id => $"categories={id}"));
        var baseUrl = _config.BaseUrl.TrimEnd('/');
        var url = $"{baseUrl}/api/v1/search?query={Uri.EscapeDataString(query)}&{categoryQuery}&type=search";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("X-Api-Key", _config.ApiKey);

        using var resp = await httpClient.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var releases = await resp.Content.ReadFromJsonAsync<List<ProwlarrRelease>>(
            _jsonOptions,
            ct) ?? [];

        var best = _releaseScorer.SelectBestRelease(releases, filter, _config.MinSeeders);
        if (best is null) return null;

        _logger.LogInformation(
            "Matched release '{Title}' ({Seeders} seeders, {SizeMb} MB, seasonPackScore: {Score}) for query '{Query}'.",
            best.Title,
            best.Seeders ?? 0,
            best.Size / (1024 * 1024),
            _releaseScorer.GetSeasonPackScore(best.Title, filter.PreferSeasonPacks),
            query);

        return best;
    }
}
