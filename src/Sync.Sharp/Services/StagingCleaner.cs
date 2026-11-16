using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sync.Sharp.Models;

namespace Sync.Sharp.Services;

/// <summary>
/// Cleans up local staging files and directories after torrent sync/removal.
/// </summary>
public interface IStagingCleaner
{
    Task CleanupLocalStagingAsync(QBitTorrentItem torrent, CancellationToken ct = default);
}

public sealed class StagingCleaner(
    IOptions<DownloaderConfigOpts> downloaderOptions,
    IStagingPathResolver stagingPathResolver,
    ILogger<StagingCleaner> logger) : IStagingCleaner
{
    private readonly DownloaderConfigOpts _downloaderConfig = downloaderOptions?.Value ?? throw new ArgumentNullException(nameof(downloaderOptions));
    private readonly IStagingPathResolver _stagingPathResolver = stagingPathResolver ?? throw new ArgumentNullException(nameof(stagingPathResolver));
    private readonly ILogger<StagingCleaner> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task CleanupLocalStagingAsync(QBitTorrentItem torrent, CancellationToken ct = default)
    {
        var rawPath = !string.IsNullOrWhiteSpace(torrent.ContentPath)
            ? torrent.ContentPath
            : (!string.IsNullOrWhiteSpace(torrent.Name)
                ? Path.Combine(
                    !string.IsNullOrWhiteSpace(torrent.SavePath) ? torrent.SavePath : _downloaderConfig.StagingDir,
                    torrent.Name)
                : null);

        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return;
        }

        string sourcePath;
        try
        {
            sourcePath = _stagingPathResolver.ResolveLocalSourcePath(rawPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve local staging path '{RawPath}' for cleanup.", rawPath);
            return;
        }

        var localStaging = Path.GetFullPath(_downloaderConfig.StagingDir)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedSource = Path.GetFullPath(sourcePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(normalizedSource, localStaging, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Refusing to delete staging root directory '{StagingDir}' for torrent '{Name}'.", localStaging, torrent.Name);
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
}
