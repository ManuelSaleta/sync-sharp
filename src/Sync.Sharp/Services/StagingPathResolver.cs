using System.Security;
using Microsoft.Extensions.Options;

namespace Sync.Sharp.Services;

/// <summary>
/// Resolves remote downloader paths to validated local staging paths within the configured staging root.
/// </summary>
public interface IStagingPathResolver
{
    string ResolveLocalSourcePath(string remotePath);
}

public sealed class StagingPathResolver(IOptions<DownloaderConfigOpts> downloaderOptions) : IStagingPathResolver
{
    private readonly DownloaderConfigOpts _downloaderConfig = downloaderOptions?.Value ?? throw new ArgumentNullException(nameof(downloaderOptions));

    public string ResolveLocalSourcePath(string remotePath)
    {
        if (string.IsNullOrWhiteSpace(remotePath))
        {
            throw new ArgumentException("Path cannot be empty.", nameof(remotePath));
        }

        var localStaging = Path.GetFullPath(_downloaderConfig.StagingDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var remotePrefix = (_downloaderConfig.RemotePathPrefix ?? string.Empty).TrimEnd('/', '\\');

        string candidatePath;
        if (!string.IsNullOrWhiteSpace(remotePrefix) && IsPathEqualOrUnderPrefix(remotePath, remotePrefix))
        {
            var relative = Path.GetRelativePath(remotePrefix, remotePath);
            candidatePath = Path.GetFullPath(Path.Combine(localStaging, relative));
        }
        else if (Path.IsPathRooted(remotePath))
        {
            candidatePath = Path.GetFullPath(remotePath);
        }
        else
        {
            candidatePath = Path.GetFullPath(Path.Combine(localStaging, remotePath));
        }

        // STRICT STAGING BOUNDARY ENFORCEMENT:
        // candidatePath MUST reside strictly inside localStaging
        var stagingPrefixWithSep = localStaging + Path.DirectorySeparatorChar;
        if (!candidatePath.StartsWith(stagingPrefixWithSep, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(candidatePath, localStaging, StringComparison.OrdinalIgnoreCase))
        {
            throw new SecurityException(
                $"CRITICAL SECURITY VIOLATION: Path '{remotePath}' resolved to '{candidatePath}' outside allowed staging root '{localStaging}'.");
        }

        // Guard against symlinks
        if (File.Exists(candidatePath))
        {
            var fi = new FileInfo(candidatePath);
            if ((fi.Attributes & FileAttributes.ReparsePoint) != 0 || fi.LinkTarget != null)
            {
                throw new SecurityException(
                    $"CRITICAL SECURITY VIOLATION: File '{candidatePath}' is a symbolic link or reparse point.");
            }
        }
        else if (Directory.Exists(candidatePath))
        {
            var di = new DirectoryInfo(candidatePath);
            if ((di.Attributes & FileAttributes.ReparsePoint) != 0 || di.LinkTarget != null)
            {
                throw new SecurityException(
                    $"CRITICAL SECURITY VIOLATION: Directory '{candidatePath}' is a symbolic link or reparse point.");
            }
        }

        return candidatePath;
    }

    public static bool IsPathEqualOrUnderPrefix(string path, string prefix)
    {
        if (string.Equals(path, prefix, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return path.Length > prefix.Length &&
               path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
               (path[prefix.Length] == '/' || path[prefix.Length] == '\\');
    }
}
