using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sync.Sharp.Models;
using Sync.Sharp.Models.Options;

namespace Sync.Sharp.Services;

public interface ISonarrClient
{
    bool IsConfigured { get; }
    Task<IReadOnlyList<SonarrEpisode>> GetWantedMissingEpisodesAsync(CancellationToken ct);
    Task<bool> TriggerDownloadedEpisodesScanAsync(string? path = null, CancellationToken ct = default);
}

public sealed class SonarrClient(
    HttpClient httpClient,
    IOptions<DiscoveryConfigOpts> discoveryOptions,
    JsonSerializerOptions jsonOptions,
    ILogger<SonarrClient> logger) : ISonarrClient
{
    private readonly SonarrConfigOpts _config = (discoveryOptions?.Value ?? throw new ArgumentNullException(nameof(discoveryOptions))).Sonarr;
    private readonly JsonSerializerOptions _jsonOptions = jsonOptions ?? throw new ArgumentNullException(nameof(jsonOptions));

    public bool IsConfigured => _config.IsConfigured;

    private string BaseUrl => _config.BaseUrl.TrimEnd('/');

    public async Task<IReadOnlyList<SonarrEpisode>> GetWantedMissingEpisodesAsync(CancellationToken ct)
    {
        if (!IsConfigured)
        {
            logger.LogWarning("Sonarr is not configured (BaseUrl/ApiKey missing); returning empty episode list.");
            return [];
        }

        using var req = new HttpRequestMessage(
            HttpMethod.Get,
            $"{BaseUrl}/api/v3/wanted/missing?pageSize=50&includeSeries=true");
        req.Headers.Add("X-Api-Key", _config.ApiKey);

        using var resp = await httpClient.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var page = await resp.Content.ReadFromJsonAsync<SonarrWantedMissingResponse>(_jsonOptions, ct);
        return page?.Records ?? [];
    }

    public async Task<bool> TriggerDownloadedEpisodesScanAsync(string? path = null, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return false;
        }

        object payload = string.IsNullOrWhiteSpace(path)
            ? new { name = "DownloadedEpisodesScan" }
            : new { name = "DownloadedEpisodesScan", path };

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/v3/command")
        {
            Content = JsonContent.Create(payload, options: _jsonOptions)
        };
        req.Headers.Add("X-Api-Key", _config.ApiKey);

        using var resp = await httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Sonarr DownloadedEpisodesScan command failed (HTTP {Status}) for path '{Path}'.",
                resp.StatusCode,
                path ?? "<root>");
            return false;
        }

        logger.LogInformation("Triggered Sonarr DownloadedEpisodesScan for path '{Path}'.", path ?? "<root>");
        return true;
    }
}
