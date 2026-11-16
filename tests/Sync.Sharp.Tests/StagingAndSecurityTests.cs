using System.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sync.Sharp.Models;
using Sync.Sharp.Models.Options;
using Sync.Sharp.Services;

namespace Sync.Sharp.Tests;

public class StagingAndSecurityTests : IDisposable
{
    private readonly string _tempRoot;

    public StagingAndSecurityTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"sync-sharp-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("subdir/..")]
    [InlineData("../evil.mkv")]
    [InlineData("/etc/passwd")]
    public void TorrentSecurityAuditor_RejectsPathTraversalAndRootedPaths(string maliciousName)
    {
        var auditor = new TorrentSecurityAuditor(NullLogger<TorrentSecurityAuditor>.Instance);
        var files = new[]
        {
            new QBitTorrentFileItem(0, maliciousName, 1024, 0, 1)
        };

        var result = auditor.EvaluateFiles("hash1", files);

        result.IsSafe.Should().BeFalse();
        result.ShouldDeleteTorrent.Should().BeTrue();
    }

    [Fact]
    public void TorrentSecurityAuditor_DeselectsNonMediaAndOversizeSubtitles_WhileAllowingValidMedia()
    {
        var auditor = new TorrentSecurityAuditor(NullLogger<TorrentSecurityAuditor>.Instance);
        var files = new[]
        {
            new QBitTorrentFileItem(0, "Movie.2026.1080p.mkv", 2_000_000_000, 0, 1),
            new QBitTorrentFileItem(1, "Movie.2026.srt", 50_000, 0, 1),
            new QBitTorrentFileItem(2, "Oversize.srt", 3 * 1024 * 1024, 0, 1),
            new QBitTorrentFileItem(3, "readme.nfo", 512, 0, 1)
        };

        var result = auditor.EvaluateFiles("hash2", files);

        result.IsSafe.Should().BeTrue();
        result.ShouldDeleteTorrent.Should().BeFalse();
        result.NonMediaIndices.Should().BeEquivalentTo([2, 3]);
    }

    [Fact]
    public void StagingPathResolver_ResolvesUnderRemotePrefix_AndRejectsSiblingPrefixCollision()
    {
        var stagingDir = Path.Combine(_tempRoot, "staging");
        Directory.CreateDirectory(stagingDir);

        var opts = Options.Create(new DownloaderConfigOpts
        {
            StagingDir = stagingDir,
            RemotePathPrefix = "/downloads"
        });
        var resolver = new StagingPathResolver(opts);

        var resolved = resolver.ResolveLocalSourcePath("/downloads/movies/MyMovie.mkv");
        resolved.Should().Be(Path.Combine(stagingDir, "movies", "MyMovie.mkv"));

        var act = () => resolver.ResolveLocalSourcePath("/downloads2/movies/MyMovie.mkv");
        act.Should().Throw<SecurityException>();
    }

    [Fact]
    public void StagingPathResolver_IsPathEqualOrUnderPrefix_RespectsDirectoryBoundaries()
    {
        StagingPathResolver.IsPathEqualOrUnderPrefix("/mnt/export/storage/plex/media/Movies", "/mnt/export/storage/plex/media")
            .Should().BeTrue();
        StagingPathResolver.IsPathEqualOrUnderPrefix("/mnt/export/storage/plex/media", "/mnt/export/storage/plex/media")
            .Should().BeTrue();
        StagingPathResolver.IsPathEqualOrUnderPrefix("/mnt/export/storage/plex/media2/Movies", "/mnt/export/storage/plex/media")
            .Should().BeFalse();
    }

    [Fact]
    public async Task StagingCleaner_DoesNotDeleteSavePathOrStagingRoot_WhenContentPathAndNameAreEmpty()
    {
        var stagingDir = Path.Combine(_tempRoot, "staging");
        var moviesDir = Path.Combine(stagingDir, "movies");
        Directory.CreateDirectory(moviesDir);
        var sentinelFile = Path.Combine(moviesDir, "keep-me.mkv");
        await File.WriteAllTextAsync(sentinelFile, "keep");

        var opts = Options.Create(new DownloaderConfigOpts
        {
            StagingDir = stagingDir
        });
        var resolver = new StagingPathResolver(opts);
        var cleaner = new StagingCleaner(opts, resolver, NullLogger<StagingCleaner>.Instance);

        var torrent = new QBitTorrentItem(
            Hash: "abc123",
            Name: "",
            State: "uploading",
            Progress: 1.0,
            Size: 1000,
            Ratio: 1.0,
            SeedingTime: 100,
            Category: "movies",
            ContentPath: null,
            SavePath: moviesDir);

        await cleaner.CleanupLocalStagingAsync(torrent, CancellationToken.None);

        Directory.Exists(moviesDir).Should().BeTrue();
        File.Exists(sentinelFile).Should().BeTrue();
    }

    [Fact]
    public async Task StagingCleaner_DeletesTorrentFolder_WhenContentPathIsNullAndNameIsProvided()
    {
        var stagingDir = Path.Combine(_tempRoot, "staging");
        var moviesDir = Path.Combine(stagingDir, "movies");
        var torrentDir = Path.Combine(moviesDir, "Sample.Movie.2026");
        var siblingDir = Path.Combine(moviesDir, "Other.Movie.2026");
        Directory.CreateDirectory(torrentDir);
        Directory.CreateDirectory(siblingDir);
        await File.WriteAllTextAsync(Path.Combine(torrentDir, "movie.mkv"), "data");
        await File.WriteAllTextAsync(Path.Combine(siblingDir, "other.mkv"), "keep");

        var opts = Options.Create(new DownloaderConfigOpts
        {
            StagingDir = stagingDir
        });
        var resolver = new StagingPathResolver(opts);
        var cleaner = new StagingCleaner(opts, resolver, NullLogger<StagingCleaner>.Instance);

        var torrent = new QBitTorrentItem(
            Hash: "abc123",
            Name: "Sample.Movie.2026",
            State: "uploading",
            Progress: 1.0,
            Size: 1000,
            Ratio: 1.0,
            SeedingTime: 100,
            Category: "movies",
            ContentPath: null,
            SavePath: moviesDir);

        await cleaner.CleanupLocalStagingAsync(torrent, CancellationToken.None);

        Directory.Exists(torrentDir).Should().BeFalse();
        Directory.Exists(siblingDir).Should().BeTrue();
    }
}
