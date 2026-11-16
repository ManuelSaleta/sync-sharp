using Microsoft.Extensions.Logging;
using Sync.Sharp.Models;

namespace Sync.Sharp.Services;

/// <summary>
/// Result of a zero-trust pre-download security audit on a torrent's file manifest.
/// </summary>
public sealed record TorrentAuditResult(
    bool IsSafe,
    bool ShouldDeleteTorrent,
    IReadOnlyList<int> NonMediaIndices);

/// <summary>
/// Evaluates torrent file manifests against zero-trust security rules (path traversal,
/// dangerous executables/archives, and non-media file exclusion).
/// </summary>
public interface ITorrentSecurityAuditor
{
    TorrentAuditResult EvaluateFiles(string hash, IReadOnlyList<QBitTorrentFileItem> files);
}

public sealed class TorrentSecurityAuditor(ILogger<TorrentSecurityAuditor> logger) : ITorrentSecurityAuditor
{
    public TorrentAuditResult EvaluateFiles(string hash, IReadOnlyList<QBitTorrentFileItem> files)
    {
        var nonMediaIndices = new List<int>();
        var hasValidMedia = false;

        foreach (var file in files)
        {
            var normalizedName = file.Name.Replace('\\', '/');
            var ext = Path.GetExtension(normalizedName);

            // Security Check 1: Reject path traversal tokens
            if (normalizedName.Contains("../", StringComparison.Ordinal) ||
                normalizedName.StartsWith('/') ||
                normalizedName.Contains("/../", StringComparison.Ordinal))
            {
                logger.LogError(
                    "CRITICAL SECURITY VIOLATION: Torrent '{Hash}' contains path traversal token in '{Name}'. Aborting and purging from swarm.",
                    hash,
                    file.Name);
                return new TorrentAuditResult(IsSafe: false, ShouldDeleteTorrent: true, NonMediaIndices: []);
            }

            // Security Check 2: Reject blacklisted dangerous file types
            if (MediaFormatConstants.BlacklistedDangerousExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            {
                logger.LogError(
                    "CRITICAL SECURITY VIOLATION: Torrent '{Hash}' contains blacklisted dangerous file '{Name}'. Aborting and purging from swarm.",
                    hash,
                    file.Name);
                return new TorrentAuditResult(IsSafe: false, ShouldDeleteTorrent: true, NonMediaIndices: []);
            }

            // Security Check 3: Check if valid media file
            if (MediaFormatConstants.AllowedVideoExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            {
                hasValidMedia = true;
            }
            else if (MediaFormatConstants.AllowedSubtitleExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            {
                // Subtitles permitted if within reasonable size ceiling (<= 2MB)
                if (file.Size > 2 * 1024 * 1024)
                {
                    logger.LogWarning(
                        "Deselecting oversize subtitle file '{Name}' ({Size} bytes) in torrent '{Hash}'.",
                        file.Name,
                        file.Size,
                        hash);
                    nonMediaIndices.Add(file.Index);
                }
            }
            else
            {
                // DEFAULT DENY: Any non-media file type (nfo, txt, url, samples, unknown) is excluded
                nonMediaIndices.Add(file.Index);
            }
        }

        // Security Check 4: Must contain at least one valid video file
        if (!hasValidMedia)
        {
            logger.LogWarning(
                "SECURITY REJECTION: Torrent '{Hash}' contains no recognized video files. Deleting from client.",
                hash);
            return new TorrentAuditResult(IsSafe: false, ShouldDeleteTorrent: true, NonMediaIndices: []);
        }

        return new TorrentAuditResult(IsSafe: true, ShouldDeleteTorrent: false, NonMediaIndices: nonMediaIndices);
    }
}
