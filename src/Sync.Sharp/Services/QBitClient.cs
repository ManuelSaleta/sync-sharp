using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sync.Sharp.Models;
using Sync.Sharp.Models.Options;

namespace Sync.Sharp.Services;

public interface IQBitClient
{
    bool IsConfigured { get; }
    Task<bool> EnsureAuthenticatedAsync(CancellationToken ct);
    Task<bool> AddTorrentAsync(string downloadUri, MediaCategory category, CancellationToken ct);
    Task<bool> AddTorrentQuarantinedAsync(string downloadUri, MediaCategory category, CancellationToken ct);
    Task<bool> AuditAndQuarantineTorrentAsync(string hash, CancellationToken ct);
    Task AuditPendingQuarantinedTorrentsAsync(CancellationToken ct);
    Task<IReadOnlyList<QBitTorrentItem>> GetCompletedTorrentsAsync(CancellationToken ct);
    Task<bool> DeleteTorrentAsync(string hash, bool deleteFiles, CancellationToken ct);
    Task PauseAllAsync(CancellationToken ct);
}

public sealed class QBitClient(
    HttpClient httpClient,
    IOptions<DownloaderConfigOpts> downloaderOptions,
    JsonSerializerOptions jsonOptions,
    ILogger<QBitClient> logger,
    ITorrentSecurityAuditor? securityAuditor = null) : IQBitClient
{
    private readonly DownloaderConfigOpts _config = downloaderOptions?.Value ?? throw new ArgumentNullException(nameof(downloaderOptions));
    private readonly JsonSerializerOptions _jsonOptions = jsonOptions ?? throw new ArgumentNullException(nameof(jsonOptions));
    private readonly ITorrentSecurityAuditor _securityAuditor = securityAuditor ?? new TorrentSecurityAuditor(NullLogger<TorrentSecurityAuditor>.Instance);
    private bool _authenticated;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_config.Password) &&
        !_config.Password.StartsWith("UNCONFIGURED", StringComparison.OrdinalIgnoreCase);

    private string BaseUrl => _config.BaseUrl.TrimEnd('/');

    public async Task<bool> EnsureAuthenticatedAsync(CancellationToken ct)
    {
        if (!IsConfigured)
        {
            logger.LogWarning("qBittorrent password is unconfigured; skipping qBittorrent API calls.");
            return false;
        }

        if (_authenticated)
        {
            return true;
        }

        using var form = new FormUrlEncodedContent([
            new KeyValuePair<string, string>("username", _config.Username),
            new KeyValuePair<string, string>("password", _config.Password)
        ]);

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/v2/auth/login")
        {
            Content = form
        };
        req.Headers.Referrer = new Uri(BaseUrl);

        using var resp = await httpClient.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (resp.IsSuccessStatusCode &&
            (resp.StatusCode == HttpStatusCode.NoContent ||
             body.Contains("Ok", StringComparison.OrdinalIgnoreCase) ||
             string.IsNullOrWhiteSpace(body)))
        {
            _authenticated = true;
            return true;
        }

        logger.LogError("qBittorrent authentication failed with status {Status}: {Body}", resp.StatusCode, body);
        return false;
    }

    private async Task<HttpResponseMessage?> SendWithReauthAsync(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken ct)
    {
        if (!await EnsureAuthenticatedAsync(ct))
        {
            return null;
        }

        using var req = requestFactory();
        var resp = await httpClient.SendAsync(req, ct);

        if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            var statusCode = resp.StatusCode;
            resp.Dispose();
            _authenticated = false;
            logger.LogDebug("qBittorrent session expired (HTTP {Status}); re-authenticating...", statusCode);

            if (!await EnsureAuthenticatedAsync(ct))
            {
                return null;
            }

            using var retryReq = requestFactory();
            resp = await httpClient.SendAsync(retryReq, ct);
        }

        return resp;
    }

    private async Task<T?> GetJsonWithReauthAsync<T>(string url, CancellationToken ct)
    {
        using var resp = await SendWithReauthAsync(() => new HttpRequestMessage(HttpMethod.Get, url), ct);
        if (resp is null)
        {
            return default;
        }

        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<T>(_jsonOptions, ct);
    }

    public async Task<bool> AddTorrentAsync(string downloadUri, MediaCategory category, CancellationToken ct)
    {
        // Enforce Quarantine Pattern: Always add torrents in paused state
        return await AddTorrentQuarantinedAsync(downloadUri, category, ct);
    }

    public async Task<bool> AddTorrentQuarantinedAsync(string downloadUri, MediaCategory category, CancellationToken ct)
    {
        var normalizedCategory = category.ToNormalizedString();
        var clientBaseDir = !string.IsNullOrWhiteSpace(_config.RemotePathPrefix)
            ? _config.RemotePathPrefix
            : _config.StagingDir;
        var savePath = Path.Combine(clientBaseDir, normalizedCategory);

        using var resp = await SendWithReauthAsync(() => new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/v2/torrents/add")
        {
            Content = new FormUrlEncodedContent([
                new KeyValuePair<string, string>("urls", downloadUri),
                new KeyValuePair<string, string>("category", normalizedCategory),
                new KeyValuePair<string, string>("savepath", savePath),
                new KeyValuePair<string, string>("paused", "true"),
                new KeyValuePair<string, string>("ratioLimit", _config.RatioLimit.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("seedingTimeLimit", _config.SeedingTimeLimitMinutes.ToString(CultureInfo.InvariantCulture))
            ])
        }, ct);

        if (resp is null)
        {
            return false;
        }

        if (!resp.IsSuccessStatusCode)
        {
            logger.LogError("Failed to add quarantined torrent to qBittorrent (HTTP {Status}).", resp.StatusCode);
            return false;
        }

        logger.LogInformation("Added torrent to qBittorrent in QUARANTINED (paused) state for pre-download security audit.");
        return true;
    }

    public async Task<IReadOnlyList<QBitTorrentFileItem>> GetTorrentFilesAsync(string hash, CancellationToken ct)
    {
        try
        {
            var files = await GetJsonWithReauthAsync<List<QBitTorrentFileItem>>(
                $"{BaseUrl}/api/v2/torrents/files?hash={Uri.EscapeDataString(hash)}",
                ct);

            return files ?? [];
        }
        catch (HttpRequestException ex)
        {
            logger.LogDebug(ex, "Could not fetch file list for torrent '{Hash}'.", hash);
            return [];
        }
    }

    public async Task<bool> SetFilePrioritiesAsync(string hash, IEnumerable<int> fileIndices, int priority, CancellationToken ct)
    {
        var idList = string.Join("|", fileIndices);
        if (string.IsNullOrWhiteSpace(idList))
        {
            return true;
        }

        using var resp = await SendWithReauthAsync(() => new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/v2/torrents/filePrio")
        {
            Content = new FormUrlEncodedContent([
                new KeyValuePair<string, string>("hash", hash),
                new KeyValuePair<string, string>("id", idList),
                new KeyValuePair<string, string>("priority", priority.ToString(CultureInfo.InvariantCulture))
            ])
        }, ct);

        return resp is { IsSuccessStatusCode: true };
    }

    public async Task<bool> ResumeTorrentAsync(string hash, CancellationToken ct)
    {
        // qBittorrent v5.0+ uses /api/v2/torrents/start; v4.x uses /api/v2/torrents/resume
        using var startResp = await SendWithReauthAsync(() => new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/v2/torrents/start")
        {
            Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("hashes", hash)])
        }, ct);

        if (startResp is null)
        {
            return false;
        }

        if (startResp.StatusCode == HttpStatusCode.NotFound)
        {
            using var resumeResp = await SendWithReauthAsync(() => new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/v2/torrents/resume")
            {
                Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("hashes", hash)])
            }, ct);
            return resumeResp is { IsSuccessStatusCode: true };
        }

        return startResp.IsSuccessStatusCode;
    }

    public async Task<bool> AuditAndQuarantineTorrentAsync(string hash, CancellationToken ct)
    {
        var files = await GetTorrentFilesAsync(hash, ct);
        if (files.Count == 0)
        {
            logger.LogDebug("Torrent '{Hash}' metadata not yet resolved by swarm; keeping in quarantine.", hash);
            return false;
        }

        var audit = _securityAuditor.EvaluateFiles(hash, files);
        if (!audit.IsSafe)
        {
            if (audit.ShouldDeleteTorrent)
            {
                await DeleteTorrentAsync(hash, deleteFiles: true, ct);
            }
            return false;
        }

        // Exclude all non-media files by setting priority to 0 (Do Not Download)
        if (audit.NonMediaIndices.Count > 0)
        {
            logger.LogInformation(
                "Zero-Trust Policy: Deselecting {Count} non-media files (priority=0) for torrent '{Hash}'.",
                audit.NonMediaIndices.Count,
                hash);
            if (!await SetFilePrioritiesAsync(hash, audit.NonMediaIndices, priority: 0, ct))
            {
                logger.LogError("Failed to deselect non-media files for torrent '{Hash}'; keeping in quarantine.", hash);
                return false;
            }
        }

        // Torrent passed security audit: safe to resume
        if (!await ResumeTorrentAsync(hash, ct))
        {
            logger.LogError("Failed to resume audited torrent '{Hash}'.", hash);
            return false;
        }

        logger.LogInformation("Torrent '{Hash}' PASSED pre-download security audit and was unpaused for media acquisition.", hash);
        return true;
    }

    public async Task AuditPendingQuarantinedTorrentsAsync(CancellationToken ct)
    {
        try
        {
            // Inspect torrents currently in paused/stopped state
            var pausedTorrents = await GetJsonWithReauthAsync<List<QBitTorrentItem>>(
                $"{BaseUrl}/api/v2/torrents/info?filter=paused",
                ct) ?? [];

            foreach (var torrent in pausedTorrents)
            {
                // If torrent is not 100% complete, it is an in-flight quarantined download
                if (!torrent.IsCompleted)
                {
                    await AuditAndQuarantineTorrentAsync(torrent.Hash, ct);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to inspect paused torrents for security audit.");
        }
    }

    public async Task<IReadOnlyList<QBitTorrentItem>> GetCompletedTorrentsAsync(CancellationToken ct)
    {
        var items = await GetJsonWithReauthAsync<List<QBitTorrentItem>>(
            $"{BaseUrl}/api/v2/torrents/info?filter=completed",
            ct);

        return items ?? [];
    }

    public async Task<bool> DeleteTorrentAsync(string hash, bool deleteFiles, CancellationToken ct)
    {
        using var resp = await SendWithReauthAsync(() => new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/v2/torrents/delete")
        {
            Content = new FormUrlEncodedContent([
                new KeyValuePair<string, string>("hashes", hash),
                new KeyValuePair<string, string>("deleteFiles", deleteFiles ? "true" : "false")
            ])
        }, ct);

        if (resp is null)
        {
            return false;
        }

        if (!resp.IsSuccessStatusCode)
        {
            logger.LogError("Failed to delete torrent '{Hash}' from qBittorrent (HTTP {Status}).", hash, resp.StatusCode);
            return false;
        }

        return true;
    }

    public async Task PauseAllAsync(CancellationToken ct)
    {
        // qBittorrent v5.0+ uses /api/v2/torrents/stop; v4.x uses /api/v2/torrents/pause
        using var stopResp = await SendWithReauthAsync(() => new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/v2/torrents/stop")
        {
            Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("hashes", "all")])
        }, ct);

        if (stopResp is null)
        {
            return;
        }

        if (stopResp.StatusCode == HttpStatusCode.NotFound)
        {
            using var pauseResp = await SendWithReauthAsync(() => new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/v2/torrents/pause")
            {
                Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("hashes", "all")])
            }, ct);

            if (pauseResp is { IsSuccessStatusCode: true })
            {
                logger.LogWarning("Paused all active qBittorrent transfers due to VPN disconnection.");
            }
            return;
        }

        if (stopResp.IsSuccessStatusCode)
        {
            logger.LogWarning("Stopped all active qBittorrent transfers due to VPN disconnection.");
        }
    }
}
