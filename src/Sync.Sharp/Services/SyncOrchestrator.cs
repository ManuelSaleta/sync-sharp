using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sync.Sharp.Models;
using Sync.Sharp.Models.Options;

namespace Sync.Sharp.Services;

public sealed class SyncOrchestrator(
    IOptions<StorageConfigOpts> storageOptions,
    IOptions<VpnConfigOpts> vpnOptions,
    IOptions<IndexerConfigOpts> indexerOptions,
    IOptions<FiltersConfigOpts> filtersOptions,
    IOptions<DownloaderConfigOpts> downloaderOptions,
    IOptions<RsyncConfigOpts> rsyncOptions,
    IVpnGuardService vpnGuard,
    IStateStore stateStore,
    IMediaDiscoveryProvider discoveryProvider,
    IProwlarrClient prowlarrClient,
    IQBitClient qBitClient,
    IMediaTransferService rsyncService,
    IMediaPollingService plexPollingService,
    ILogger<SyncOrchestrator> logger,
    IRadarrClient? radarrClient = null,
    ISonarrClient? sonarrClient = null,
    IOptions<DiscoveryConfigOpts>? discoveryOptions = null)
{
    private readonly StorageConfigOpts _storageConfig = storageOptions?.Value ?? throw new ArgumentNullException(nameof(storageOptions));
    private readonly VpnConfigOpts _vpnConfig = vpnOptions?.Value ?? throw new ArgumentNullException(nameof(vpnOptions));
    private readonly IndexerConfigOpts _indexerConfig = indexerOptions?.Value ?? throw new ArgumentNullException(nameof(indexerOptions));
    private readonly FiltersConfigOpts _filtersConfig = filtersOptions?.Value ?? throw new ArgumentNullException(nameof(filtersOptions));
    private readonly DownloaderConfigOpts _downloaderConfig = downloaderOptions?.Value ?? throw new ArgumentNullException(nameof(downloaderOptions));
    private readonly RsyncConfigOpts _rsyncConfig = rsyncOptions?.Value ?? throw new ArgumentNullException(nameof(rsyncOptions));
    private readonly DiscoveryConfigOpts _discoveryConfig = discoveryOptions?.Value ?? new DiscoveryConfigOpts();

    public async Task RunCycleAsync(CancellationToken ct)
    {
        await using var lockStream = TryAcquireLockFile(_storageConfig.LockFile);
        if (lockStream is null)
        {
            logger.LogWarning("Another sync-sharp cycle is already holding lock '{LockFile}'; skipping.", _storageConfig.LockFile);
            return;
        }

        await stateStore.InitializeAsync(ct);

        if (!await VerifyVpnOrPauseAsync(ct))
        {
            return;
        }

        await DiscoverAndGrabCandidatesAsync(ct);

        await qBitClient.AuditPendingQuarantinedTorrentsAsync(ct);

        await SweepCompletedTorrentsAsync(ct);
    }

    private async Task<bool> VerifyVpnOrPauseAsync(CancellationToken ct)
    {
        var vpnConnected = await vpnGuard.IsVpnConnectedAsync(ct);
        if (!vpnConnected && _vpnConfig.PauseTorrentsWhenDisconnected)
        {
            await qBitClient.PauseAllAsync(ct);
        }

        return vpnConnected;
    }

    private async Task DiscoverAndGrabCandidatesAsync(CancellationToken ct)
    {
        var candidates = await discoveryProvider.GetWantedCandidatesAsync(ct);
        var grabbedCount = 0;

        foreach (var candidate in candidates)
        {
            if (grabbedCount >= _indexerConfig.MaxGrabPerRun)
            {
                break;
            }

            if (await TryGrabCandidateAsync(candidate, ct))
            {
                grabbedCount++;
            }
        }
    }

    private async Task<bool> TryGrabCandidateAsync(DiscoveryCandidate candidate, CancellationToken ct)
    {
        var mediaCandidate = new MediaCandidate
        {
            Id = candidate.ExternalId,
            Title = candidate.SearchQuery,
            SearchQuery = candidate.SearchQuery,
            Category = candidate.Category
        };

        if (await IsMediaAlreadyPresent(mediaCandidate, ct))
        {
            await stateStore.RecordGrabbedAsync(
                mediaCandidate.Id,
                mediaCandidate.InfoHash,
                mediaCandidate.Title,
                mediaCandidate.Category,
                ct);
            return false;
        }

        var filter = candidate.Category.IsTv()
            ? _filtersConfig.Tv
            : _filtersConfig.Movies;

        var bestRelease = await prowlarrClient.SearchBestReleaseAsync(candidate.SearchQuery, filter, ct);
        if (bestRelease is null)
        {
            return false;
        }

        var infoHash = !string.IsNullOrWhiteSpace(bestRelease.InfoHash)
            ? bestRelease.InfoHash
            : bestRelease.Guid;

        if (stateStore.IsHashProcessed(infoHash))
        {
            return false;
        }

        mediaCandidate.Title = bestRelease.Title;
        mediaCandidate.InfoHash = infoHash;

        if (await IsMediaAlreadyPresent(mediaCandidate, ct))
        {
            await stateStore.RecordGrabbedAsync(
                mediaCandidate.Id,
                mediaCandidate.InfoHash,
                mediaCandidate.Title,
                mediaCandidate.Category,
                ct);
            return false;
        }

        var downloadUri = bestRelease.GetBestDownloadUri();
        if (string.IsNullOrWhiteSpace(downloadUri))
        {
            return false;
        }

        if (!await qBitClient.AddTorrentQuarantinedAsync(downloadUri, candidate.Category, ct))
        {
            return false;
        }

        await stateStore.RecordGrabbedAsync(
            candidate.ExternalId,
            infoHash,
            bestRelease.Title,
            candidate.Category,
            ct);

        await qBitClient.AuditAndQuarantineTorrentAsync(infoHash, ct);
        return true;
    }

    private async Task SweepCompletedTorrentsAsync(CancellationToken ct)
    {
        var completed = await qBitClient.GetCompletedTorrentsAsync(ct);
        foreach (var torrent in completed)
        {
            var isSynced = stateStore.IsHashSynced(torrent.Hash);
            var category = torrent.MediaCategory;

            if (!isSynced && await rsyncService.TransferAsync(torrent, ct))
            {
                await stateStore.MarkSyncedAsync(torrent.Hash, torrent.Name, category, ct);
                isSynced = true;

                await TriggerPlexScanAsync(category, ct);
                await TriggerArrImportScanAsync(torrent, category, ct);
            }

            if (isSynced)
            {
                await EvaluateAndCleanupSyncedTorrentAsync(torrent, ct);
            }
        }
    }

    private async Task EvaluateAndCleanupSyncedTorrentAsync(QBitTorrentItem torrent, CancellationToken ct)
    {
        var seedingTimeMinutes = torrent.SeedingTimeSeconds / 60.0;
        var ratioSatisfied = _downloaderConfig.RatioLimit > 0 && torrent.Ratio >= _downloaderConfig.RatioLimit;
        var seedingTimeSatisfied = _downloaderConfig.SeedingTimeLimitMinutes > 0 && seedingTimeMinutes >= _downloaderConfig.SeedingTimeLimitMinutes;
        var shouldCleanup = _rsyncConfig.RemoveLocalAfterSync || ratioSatisfied || seedingTimeSatisfied;

        if (shouldCleanup)
        {
            await qBitClient.DeleteTorrentAsync(torrent.Hash, deleteFiles: true, ct);
            await rsyncService.CleanupLocalStagingAsync(torrent, ct);
            logger.LogInformation(
                "Cleaned up synced torrent '{Name}' and local staging files (ratio: {Ratio:F2}, seeded: {Minutes:F0}m).",
                torrent.Name,
                torrent.Ratio,
                seedingTimeMinutes);
        }
    }

    public class MediaCandidate
    {
        public string? Id { get; set; } = null;
        public string? InfoHash { get; set; } = null;
        public string Title { get; set; } = string.Empty;
        public MediaCategory Category { get; set; } = MediaCategory.Movies;
        public string SearchQuery { get; set; } = string.Empty;
    }

    private async Task TriggerPlexScanAsync(MediaCategory category, CancellationToken ct)
    {
        try
        {
            await plexPollingService.ForceScanLibraryAsync(category, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to trigger Plex library scan for category {Category}.", category);
        }
    }

    private async Task TriggerArrImportScanAsync(QBitTorrentItem torrent, MediaCategory category, CancellationToken ct)
    {
        try
        {
            if (category.IsMovies() && radarrClient is { IsConfigured: true } && _discoveryConfig.Radarr.SyncOnImport)
            {
                var scanPath = !string.IsNullOrWhiteSpace(_rsyncConfig.DestMoviesDir) && !string.IsNullOrWhiteSpace(torrent.Name)
                    ? Path.Combine(_rsyncConfig.DestMoviesDir, torrent.Name)
                    : null;
                await radarrClient.TriggerDownloadedMoviesScanAsync(scanPath, ct);
            }
            else if (category.IsTv() && sonarrClient is { IsConfigured: true } && _discoveryConfig.Sonarr.SyncOnImport)
            {
                var scanPath = !string.IsNullOrWhiteSpace(_rsyncConfig.DestTvDir) && !string.IsNullOrWhiteSpace(torrent.Name)
                    ? Path.Combine(_rsyncConfig.DestTvDir, torrent.Name)
                    : null;
                await sonarrClient.TriggerDownloadedEpisodesScanAsync(scanPath, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to trigger post-transfer *Arr import scan for '{Name}'.", torrent.Name);
        }
    }

    public async Task<bool> IsMediaAlreadyPresent(MediaCandidate candidate, CancellationToken ct)
    {
        // Target release check: Verify matched release does not already exist in Plex before quarantining
        if (await plexPollingService.IsMediaAlreadyPresentAsync(candidate.Title, candidate.Category, ct: ct))
        {
            logger.LogInformation(
                "Skipping release '{ReleaseTitle}' for candidate '{Query}' - target media already exists in Plex server library.",
                candidate.Title,
                candidate.SearchQuery);

            return true;
        }

        return false;
    }

    private static FileStream? TryAcquireLockFile(string lockPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(lockPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return null;
        }
    }
}
