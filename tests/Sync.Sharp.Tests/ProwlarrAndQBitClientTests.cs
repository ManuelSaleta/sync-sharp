using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sync.Sharp.Models.Options;
using Sync.Sharp.Services;

namespace Sync.Sharp.Tests;

public class ProwlarrAndQBitClientTests
{
    [Fact]
    public async Task ProwlarrClient_SearchBestReleaseAsync_ShouldPrioritizeSeasonPacksAndHigherSeeders()
    {
        const string payload = """
        [
          {
            "guid": "guid-solo",
            "title": "Severance.S01E01.1080p.WEB-DL",
            "size": 2147483648,
            "indexerId": 1,
            "infoHash": "hash1",
            "magnetUrl": "magnet:?xt=urn:btih:hash1",
            "seeders": 500,
            "leechers": 10,
            "publishDate": "2026-01-01T00:00:00Z"
          },
          {
            "guid": "guid-season",
            "title": "Severance.Season.1.Complete.1080p.WEB-DL",
            "size": 10737418240,
            "indexerId": 1,
            "infoHash": "hash2",
            "magnetUrl": "magnet:?xt=urn:btih:hash2",
            "seeders": 120,
            "leechers": 5,
            "publishDate": "2026-01-01T00:00:00Z"
          }
        ]
        """;

        var handler = new MockHttpMessageHandler(_ => MockHttpMessageHandler.JsonResponse(payload));
        var httpClient = new HttpClient(handler);
        var indexerOpts = Options.Create(new IndexerConfigOpts
        {
            BaseUrl = "http://127.0.0.1:9696",
            ApiKey = "valid-key",
            MinSeeders = 50
        });
        var jsonOpts = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var client = new ProwlarrClient(httpClient, indexerOpts, jsonOpts, NullLogger<ProwlarrClient>.Instance);

        var filter = new CategoryFilterConfigOpts
        {
            CategoryIds = [5000],
            MinSizeMb = 500,
            MaxSizeMb = 25000,
            AllowedPatterns = ["1080p"],
            BlockedPatterns = ["720p", "CAM"],
            PreferSeasonPacks = true
        };

        var best = await client.SearchBestReleaseAsync("Severance", filter, CancellationToken.None);

        best.Should().NotBeNull();
        best!.InfoHash.Should().Be("hash2");
    }

    [Fact]
    public async Task QBitClient_AuditAndQuarantineTorrentAsync_ShouldRejectDangerousExecutablePayloads()
    {
        var deletedTorrent = false;
        var handler = new MockHttpMessageHandler(req =>
        {
            var path = req.RequestUri?.AbsolutePath ?? "";
            if (path.EndsWith("/api/v2/auth/login"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Ok.") };
            }
            if (path.EndsWith("/api/v2/torrents/files"))
            {
                return MockHttpMessageHandler.JsonResponse("""
                [
                  { "index": 0, "name": "Movie.2026.1080p.mkv", "size": 2000000000, "progress": 0, "priority": 1 },
                  { "index": 1, "name": "CodecPack.exe", "size": 1048576, "progress": 0, "priority": 1 }
                ]
                """);
            }
            if (path.EndsWith("/api/v2/torrents/delete"))
            {
                deletedTorrent = true;
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var httpClient = new HttpClient(handler);
        var downloaderOpts = Options.Create(new DownloaderConfigOpts
        {
            BaseUrl = "http://127.0.0.1:8080",
            Username = "admin",
            Password = "secret",
            StagingDir = "/downloads"
        });
        var jsonOpts = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var client = new QBitClient(httpClient, downloaderOpts, jsonOpts, NullLogger<QBitClient>.Instance);

        var passed = await client.AuditAndQuarantineTorrentAsync("badhash123", CancellationToken.None);

        passed.Should().BeFalse();
        deletedTorrent.Should().BeTrue();
    }
}
