using CliWrap;
using CliWrap.Buffered;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sync.Sharp.Models;

namespace Sync.Sharp.Services;

/// <summary>
/// Validates staged media and subtitle files before transfer.
/// </summary>
public interface IMediaValidator
{
    Task<bool> ValidateMediaWithFfprobeAsync(string contentPath, CancellationToken ct);
    IReadOnlyList<string> GetValidatedSubtitleFiles(string path);
}

public sealed class MediaValidator(
    IOptions<RsyncConfigOpts> rsyncOptions,
    ILogger<MediaValidator> logger) : IMediaValidator
{
    private readonly RsyncConfigOpts _rsyncConfig = rsyncOptions?.Value ?? throw new ArgumentNullException(nameof(rsyncOptions));
    private readonly ILogger<MediaValidator> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<bool> ValidateMediaWithFfprobeAsync(string contentPath, CancellationToken ct)
    {
        if (!_rsyncConfig.VerifyWithFfprobe)
        {
            return true;
        }

        var videoFiles = GetMediaFiles(contentPath);
        if (videoFiles.Count == 0)
        {
            _logger.LogWarning("No media files found in '{ContentPath}'.", contentPath);
            return false;
        }

        foreach (var file in videoFiles)
        {
            var fi = new FileInfo(file);
            if ((fi.Attributes & FileAttributes.ReparsePoint) != 0 || fi.LinkTarget != null)
            {
                _logger.LogError("Security Violation: File '{File}' is a symbolic link. ffprobe rejected.", file);
                return false;
            }

            var cmdResult = await Cli.Wrap("ffprobe")
                .WithArguments([
                    "-v", "error",
                    "-hide_banner",
                    "-protocol_whitelist", "file,crypto",
                    "-show_entries", "format=format_name:stream=codec_type,codec_name",
                    "-of", "default=noprint_wrappers=1:nokey=1",
                    "--",
                    file
                ])
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync(ct);

            var cmdOutput = cmdResult.StandardOutput.Trim();

            if (cmdResult.ExitCode != 0)
            {
                var errorMsg = !string.IsNullOrWhiteSpace(cmdResult.StandardError)
                    ? cmdResult.StandardError.Trim()
                    : cmdOutput;

                _logger.LogError(
                    "ffprobe validation rejected '{File}' (exit={ExitCode}, error='{Error}').",
                    file,
                    cmdResult.ExitCode,
                    errorMsg);
                return false;
            }

            var foundAllowedFormat = MediaFormatConstants.AllowedFfprobeFormats.Any(allowed => cmdOutput.Contains(allowed, StringComparison.OrdinalIgnoreCase));
            var hasVideoStream = cmdOutput.Contains("video", StringComparison.OrdinalIgnoreCase);

            if (!foundAllowedFormat || !hasVideoStream)
            {
                _logger.LogError(
                    "ffprobe validation rejected '{File}': output '{Format}' lacks allowed container format or video stream.",
                    file,
                    cmdOutput);
                return false;
            }
        }

        return true;
    }

    public IReadOnlyList<string> GetValidatedSubtitleFiles(string path)
    {
        var result = new List<string>();
        IEnumerable<string> candidates;

        if (File.Exists(path))
        {
            if (string.Equals(Path.GetExtension(path), ".srt", StringComparison.OrdinalIgnoreCase))
            {
                candidates = [path];
            }
            else
            {
                var srtSibling = Path.ChangeExtension(path, ".srt");
                candidates = File.Exists(srtSibling) ? [srtSibling] : [];
            }
        }
        else if (Directory.Exists(path))
        {
            candidates = Directory.EnumerateFiles(path, "*.srt", SearchOption.AllDirectories);
        }
        else
        {
            return [];
        }

        foreach (var file in candidates)
        {
            if (IsValidSubtitleFile(file))
            {
                result.Add(file);
            }
            else
            {
                _logger.LogWarning("Skipping unverified or non-compliant subtitle file '{File}'.", file);
            }
        }

        return result;
    }

    private static List<string> GetMediaFiles(string path)
    {
        if (File.Exists(path))
        {
            var ext = Path.GetExtension(path);
            var fi = new FileInfo(path);
            if ((fi.Attributes & FileAttributes.ReparsePoint) != 0 || fi.LinkTarget != null)
            {
                return [];
            }

            return MediaFormatConstants.AllowedVideoExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase)
                ? [path]
                : [];
        }

        if (Directory.Exists(path))
        {
            return [.. Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories)
                .Where(f =>
                {
                    var name = Path.GetFileName(f);
                    var ext = Path.GetExtension(f);
                    var fi = new FileInfo(f);

                    if ((fi.Attributes & FileAttributes.ReparsePoint) != 0 || fi.LinkTarget != null)
                    {
                        return false;
                    }

                    return !name.Contains("sample", StringComparison.OrdinalIgnoreCase) &&
                           MediaFormatConstants.AllowedVideoExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
                })];
        }

        return [];
    }

    private static bool IsValidSubtitleFile(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists || (fi.Attributes & FileAttributes.ReparsePoint) != 0 || fi.LinkTarget != null)
            {
                return false;
            }

            // Subtitle size ceiling: maximum 2MB
            if (fi.Length > 2 * 1024 * 1024 || fi.Length == 0)
            {
                return false;
            }

            // Verify plain UTF-8 / ASCII text (no binary null bytes or script injections)
            using var stream = File.OpenRead(path);
            var buffer = new byte[Math.Min(4096, (int)fi.Length)];
            var read = stream.Read(buffer, 0, buffer.Length);

            for (var i = 0; i < read; i++)
            {
                if (buffer[i] == 0)
                {
                    return false;
                }
            }

            var preview = System.Text.Encoding.UTF8.GetString(buffer, 0, read);
            if (preview.Contains("<script", StringComparison.OrdinalIgnoreCase) ||
                preview.Contains("javascript:", StringComparison.OrdinalIgnoreCase) ||
                preview.StartsWith("MZ", StringComparison.Ordinal) ||
                preview.StartsWith("\x7fELF", StringComparison.Ordinal))
            {
                return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
