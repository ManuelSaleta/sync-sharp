using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sync.Sharp.Models;
using Sync.Sharp.Models.Options;

namespace Sync.Sharp.Services;

public interface IRadarrClient
{
    bool IsConfigured { get; }
    Task<IReadOnlyList<RadarrMovie>> GetMonitoredMoviesAsync(CancellationToken ct);
    Task<bool> TriggerDownloadedMoviesScanAsync(string? path = null, CancellationToken ct = default);
}

public sealed class RadarrClient(
    HttpClient httpClient,
    IOptions<DiscoveryConfigOpts> discoveryOptions,
    JsonSerializerOptions jsonOptions,
    ILogger<RadarrClient> logger) : IRadarrClient
{
    private readonly RadarrConfigOpts _config = (discoveryOptions?.Value ?? throw new ArgumentNullException(nameof(discoveryOptions))).Radarr;
    private readonly JsonSerializerOptions _jsonOptions = jsonOptions ?? throw new ArgumentNullException(nameof(jsonOptions));

    public bool IsConfigured => _config.IsConfigured;

    private string BaseUrl => _config.BaseUrl.TrimEnd('/');

    public async Task<IReadOnlyList<RadarrMovie>> GetMonitoredMoviesAsync(CancellationToken ct)
    {
        if (!IsConfigured)
        {
            logger.LogWarning("Radarr is not configured (BaseUrl/ApiKey missing); returning empty movie list.");
            return [];
        }

        using var req = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/api/v3/movie?monitored=true");
        req.Headers.Add("X-Api-Key", _config.ApiKey);

        using var resp = await httpClient.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var movies = await resp.Content.ReadFromJsonAsync<List<RadarrMovie>>(_jsonOptions, ct);
        return movies ?? [];
    }

    public async Task<bool> TriggerDownloadedMoviesScanAsync(string? path = null, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return false;
        }

        object payload = string.IsNullOrWhiteSpace(path)
            ? new { name = "DownloadedMoviesScan" }
            : new { name = "DownloadedMoviesScan", path };

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/v3/command")
        {
            Content = JsonContent.Create(payload, options: _jsonOptions)
        };
        req.Headers.Add("X-Api-Key", _config.ApiKey);

        using var resp = await httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Radarr DownloadedMoviesScan command failed (HTTP {Status}) for path '{Path}'.",
                resp.StatusCode,
                path ?? "<root>");
            return false;
        }

        logger.LogInformation("Triggered Radarr DownloadedMoviesScan for path '{Path}'.", path ?? "<root>");
        return true;
    }
}
