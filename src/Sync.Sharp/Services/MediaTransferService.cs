using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sync.Sharp.Models;

namespace Sync.Sharp.Services;

/// <summary>
/// Transfers verified media from local staging to remote storage.
/// </summary>
public interface IMediaTransferService
{
    Task<bool> ValidateMediaWithFfprobeAsync(string contentPath, CancellationToken ct);
    Task<bool> TransferAsync(QBitTorrentItem torrent, CancellationToken ct);
    Task CleanupLocalStagingAsync(QBitTorrentItem torrent, CancellationToken ct = default);
}

/// <summary>
/// Coordinates media validation, rsync transfer, and local staging cleanup.
/// </summary>
public sealed class MediaTransferService(
    IOptions<RsyncConfigOpts> rsyncOptions,
    IOptions<DownloaderConfigOpts> downloaderOptions,
    IMediaValidator mediaValidator,
    IStagingPathResolver stagingPathResolver,
    IRsyncTransferClient rsyncTransferClient,
    IStagingCleaner stagingCleaner,
    ILogger<MediaTransferService> logger) : IMediaTransferService
{
    private readonly ILogger<MediaTransferService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly RsyncConfigOpts _rsyncConfig = rsyncOptions?.Value ?? throw new ArgumentNullException(nameof(rsyncOptions));
    private readonly DownloaderConfigOpts _downloaderConfig = downloaderOptions?.Value ?? throw new ArgumentNullException(nameof(downloaderOptions));
    private readonly IMediaValidator _mediaValidator = mediaValidator ?? throw new ArgumentNullException(nameof(mediaValidator));
    private readonly IStagingPathResolver _stagingPathResolver = stagingPathResolver ?? throw new ArgumentNullException(nameof(stagingPathResolver));
    private readonly IRsyncTransferClient _rsyncTransferClient = rsyncTransferClient ?? throw new ArgumentNullException(nameof(rsyncTransferClient));
    private readonly IStagingCleaner _stagingCleaner = stagingCleaner ?? throw new ArgumentNullException(nameof(stagingCleaner));

    public Task<bool> ValidateMediaWithFfprobeAsync(string contentPath, CancellationToken ct) =>
        _mediaValidator.ValidateMediaWithFfprobeAsync(contentPath, ct);

    public async Task<bool> TransferAsync(QBitTorrentItem torrent, CancellationToken ct)
    {
        if (!_rsyncConfig.Enabled)
        {
            _logger.LogInformation("Rsync is disabled in configuration; skipping transfer for '{Name}'.", torrent.Name);
            return false;
        }

        var rawSourcePath = !string.IsNullOrWhiteSpace(torrent.ContentPath)
            ? torrent.ContentPath
            : Path.Combine(torrent.SavePath ?? _downloaderConfig.StagingDir, torrent.Name);

        var sourcePath = ResolveLocalSourcePath(rawSourcePath);

        if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
        {
            _logger.LogWarning("Completed torrent path '{SourcePath}' does not exist locally (resolved: '{ResolvedPath}').", rawSourcePath, sourcePath);
            return false;
        }

        if (!await ValidateMediaWithFfprobeAsync(sourcePath, ct))
        {
            return false;
        }

        var srtFiles = _mediaValidator.GetValidatedSubtitleFiles(sourcePath);
        return await _rsyncTransferClient.TransferAsync(torrent, sourcePath, srtFiles, ct);
    }

    public Task CleanupLocalStagingAsync(QBitTorrentItem torrent, CancellationToken ct = default) =>
        _stagingCleaner.CleanupLocalStagingAsync(torrent, ct);

    public string ResolveLocalSourcePath(string remotePath) =>
        _stagingPathResolver.ResolveLocalSourcePath(remotePath);
}
