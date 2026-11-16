using CliWrap;
using CliWrap.Buffered;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sync.Sharp.Models;

namespace Sync.Sharp.Services;

/// <summary>
/// Executes hardened rsync over SSH transfers for validated media payloads.
/// </summary>
public interface IRsyncTransferClient
{
    Task<bool> TransferAsync(QBitTorrentItem torrent, string sourcePath, IReadOnlyList<string> validatedSubtitles, CancellationToken ct);
}

public sealed class RsyncTransferClient(
    IOptions<RsyncConfigOpts> rsyncOptions,
    ILogger<RsyncTransferClient> logger) : IRsyncTransferClient
{
    private readonly RsyncConfigOpts _rsyncConfig = rsyncOptions?.Value ?? throw new ArgumentNullException(nameof(rsyncOptions));
    private readonly ILogger<RsyncTransferClient> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<bool> TransferAsync(
        QBitTorrentItem torrent,
        string sourcePath,
        IReadOnlyList<string> validatedSubtitles,
        CancellationToken ct)
    {
        var isTv = torrent.MediaCategory.IsTv();
        var remoteBaseDir = isTv ? _rsyncConfig.DestTvDir : _rsyncConfig.DestMoviesDir;

        var rrsyncPrefix = (_rsyncConfig.RrsyncPrefix ?? string.Empty).TrimEnd('/', '\\');
        var relativeDir = !string.IsNullOrWhiteSpace(rrsyncPrefix) &&
                          StagingPathResolver.IsPathEqualOrUnderPrefix(remoteBaseDir, rrsyncPrefix)
            ? Path.GetRelativePath(rrsyncPrefix, remoteBaseDir)
            : remoteBaseDir;

        if (string.IsNullOrWhiteSpace(relativeDir) || relativeDir == ".")
        {
            relativeDir = isTv ? "TV Shows" : "Movies";
        }

        var destination = $"{_rsyncConfig.SshUser}@{_rsyncConfig.SshHost}:{relativeDir.TrimEnd('/')}/";
        var sshCmd = $"ssh -i {_rsyncConfig.SshKey} -o IdentitiesOnly=yes -o BatchMode=yes -o StrictHostKeyChecking=accept-new -o ConnectTimeout=10";

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

        if (validatedSubtitles.Count > 0)
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
            .WithValidation(CommandResultValidation.None)
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
}
