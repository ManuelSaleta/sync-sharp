using CliWrap;
using CliWrap.Buffered;
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
/// Transfers allowed media type (TV, Movies) from A to B. Using 'rsync'
/// </summary>
public sealed class MediaTransferService(
    IOptions<RsyncConfigOpts> rsyncOptions,
    IOptions<DownloaderConfigOpts> downloaderOptions,
    IMediaValidator mediaValidator,
    IStagingPathResolver stagingPathResolver,
    ILogger<MediaTransferService> logger) : IMediaTransferService
{
    private readonly ILogger<MediaTransferService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly RsyncConfigOpts _rsyncConfig = rsyncOptions?.Value ?? throw new ArgumentNullException(nameof(rsyncOptions));
    private readonly DownloaderConfigOpts _downloaderConfig = downloaderOptions?.Value ?? throw new ArgumentNullException(nameof(downloaderOptions));
    private readonly IMediaValidator _mediaValidator = mediaValidator ?? throw new ArgumentNullException(nameof(mediaValidator));
    private readonly IStagingPathResolver _stagingPathResolver = stagingPathResolver ?? throw new ArgumentNullException(nameof(stagingPathResolver));

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

        var isTv = torrent.MediaCategory.IsTv();
        var remoteBaseDir = isTv ? _rsyncConfig.DestTvDir : _rsyncConfig.DestMoviesDir;

        // When connecting to micro-nas with a restricted rrsync key (e.g. command="rrsync -wo /mnt/export/storage/plex/media"),
        // the server-side rrsync root is already at RrsyncPrefix.
        // Strip the redundant prefix if present so destination evaluates cleanly to "Movies/" or "TV Shows/".
        var rrsyncPrefix = _rsyncConfig.RrsyncPrefix;
        var relativeDir = !string.IsNullOrWhiteSpace(rrsyncPrefix) &&
                          remoteBaseDir.StartsWith(rrsyncPrefix, StringComparison.OrdinalIgnoreCase)
            ? Path.GetRelativePath(rrsyncPrefix, remoteBaseDir)
            : remoteBaseDir;

        if (string.IsNullOrWhiteSpace(relativeDir) || relativeDir == ".")
        {
            relativeDir = isTv ? "TV Shows" : "Movies";
        }

        var destination = $"{_rsyncConfig.SshUser}@{_rsyncConfig.SshHost}:{relativeDir.TrimEnd('/')}/";

        var sshCmd = $"ssh -i {_rsyncConfig.SshKey} -o IdentitiesOnly=yes -o BatchMode=yes -o StrictHostKeyChecking=accept-new -o ConnectTimeout=10";

        // Hardened Rsync Arguments matching Micro-NAS storage permissions (1000:1000, 775/664):
        //   -rtvP: Recursive, timestamps, verbose, partial (avoids -a which copies symlinks & device nodes)
        //   --no-links / --no-devices / --no-specials: Reject syncing symlinks and character/block/FIFO device nodes
        //   --safe-links: Ignore links pointing outside destination
        //   --prune-empty-dirs: Excludes directory shells that lack verified media files
        // Note: --protect-args (-s) is omitted because rrsync disables -s so it can validate paths in SSH_ORIGINAL_COMMAND
        var args = new List<string>
        {
            "-rtvP",
            "--partial",
            "--no-perms",
            "--no-links",
            "--no-devices",
            "--no-specials",
            "--safe-links",
            "--prune-empty-dirs",
            $"--chown={_rsyncConfig.Chown}",
            "--chmod=Du=rwx,Dg=rwx,Do=rx,Fu=rw,Fg=rw,Fo=r",
            "--include=*/"
        };

        foreach (var ext in MediaFormatConstants.AllowedVideoExtensions)
        {
            args.Add($"--include=*{ext}");
        }

        var srtFiles = _mediaValidator.GetValidatedSubtitleFiles(sourcePath);
        if (srtFiles.Count > 0)
        {
            args.Add("--include=*.srt");
        }

        args.Add("--exclude=*");
        args.Add("-e");
        args.Add(sshCmd);
        args.Add("--");
        args.Add(sourcePath);
        args.Add(destination);

        _logger.LogInformation(
            "Starting rsync transfer for '{Name}' -> '{Destination}'.",
            torrent.Name,
            destination);

        var result = await Cli.Wrap("rsync")
            .WithArguments(args)
            .WithValidation(CommandResultValidation.ZeroExitCode)
            .ExecuteBufferedAsync(ct);

        if (!result.IsSuccess)
        {
            _logger.LogError(
                "Rsync failed for '{Name}' (exit={ExitCode}): {Stderr}",
                torrent.Name,
                result.ExitCode,
                result.StandardError.Trim());
            return false;
        }

        _logger.LogInformation("Successfully synced '{Name}' to {Host}.", torrent.Name, _rsyncConfig.SshHost);
        return true;
    }

    public async Task CleanupLocalStagingAsync(QBitTorrentItem torrent, CancellationToken ct = default)
    {
        var rawPath = torrent.ContentPath ?? torrent.SavePath;
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return;
        }

        string sourcePath;
        try
        {
            sourcePath = ResolveLocalSourcePath(rawPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve local staging path '{RawPath}' for cleanup.", rawPath);
            return;
        }

        // Allow a brief settle window for qBittorrent to complete its background file deletion if deleteFiles was requested
        for (var i = 0; i < 5; i++)
        {
            if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
            {
                _logger.LogDebug("Local staging path '{SourcePath}' already removed.", sourcePath);
                return;
            }

            try
            {
                await Task.Delay(200, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
            {
                return;
            }

            try
            {
                if (File.Exists(sourcePath))
                {
                    File.SetAttributes(sourcePath, FileAttributes.Normal);
                    File.Delete(sourcePath);
                    _logger.LogInformation("Cleaned up local staging file '{SourcePath}'.", sourcePath);
                    return;
                }

                if (Directory.Exists(sourcePath))
                {
                    ResetFileAttributes(new DirectoryInfo(sourcePath));
                    Directory.Delete(sourcePath, recursive: true);
                    _logger.LogInformation("Cleaned up local staging directory '{SourcePath}'.", sourcePath);
                    return;
                }
            }
            catch (Exception ex) when (ex is DirectoryNotFoundException or FileNotFoundException)
            {
                // Removed concurrently (e.g. by qBittorrent)
                _logger.LogDebug("Local staging path '{SourcePath}' was removed concurrently.", sourcePath);
                return;
            }
            catch (Exception ex) when (attempt < 3)
            {
                _logger.LogDebug(ex, "Transient error cleaning up '{SourcePath}' (attempt {Attempt}/3), retrying...", sourcePath, attempt);
                try
                {
                    await Task.Delay(500, ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                // If it was removed during our attempts, do not warn
                if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
                {
                    return;
                }

                _logger.LogWarning(ex, "Failed to clean up local staging path '{RawPath}'.", rawPath);
            }
        }
    }

    private static void ResetFileAttributes(DirectoryInfo directory)
    {
        try
        {
            directory.Attributes = FileAttributes.Normal;
            foreach (var file in directory.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                try
                {
                    file.Attributes = FileAttributes.Normal;
                }
                catch
                {
                    // Ignore individual attribute reset failures
                }
            }
        }
        catch
        {
            // Directory may have been removed concurrently
        }
    }

    public string ResolveLocalSourcePath(string remotePath) =>
        _stagingPathResolver.ResolveLocalSourcePath(remotePath);
}
