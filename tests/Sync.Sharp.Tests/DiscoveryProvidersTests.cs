using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sync.Sharp.Extensions;
using Sync.Sharp.Models;
using Sync.Sharp.Models.Options;
using Sync.Sharp.Services;

namespace Sync.Sharp.Tests;

public class DiscoveryProvidersTests
{
    private static JsonSerializerOptions CreateAppJsonOptions() =>
        new(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
        };

    [Fact]
    public async Task RadarrClient_GetMonitoredMoviesAndTriggerScan_UsesExpectedApiContract()
    {
        string? seenApiKey = null;
        string? seenCommandBody = null;
        var handler = new MockHttpMessageHandler(req =>
        {
            seenApiKey = req.Headers.GetValues("X-Api-Key").FirstOrDefault();
            if (req.Method == HttpMethod.Get && req.RequestUri!.PathAndQuery == "/api/v3/movie?monitored=true")
            {
                return MockHttpMessageHandler.JsonResponse("""
                [
                  {
                    "id": 1,
                    "title": "Inception",
                    "originalTitle": "Inception",
                    "year": 2010,
                    "tmdbId": 27205,
                    "imdbId": "tt1375666",
                    "hasFile": false,
                    "monitored": true,
                    "isAvailable": true
                  },
                  {
                    "id": 2,
                    "title": "AlreadyDownloaded",
                    "year": 2024,
                    "tmdbId": 99999,
                    "hasFile": true,
                    "monitored": true,
                    "isAvailable": true
                  }
                ]
                """);
            }

            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath == "/api/v3/command")
            {
                seenCommandBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.Created);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var opts = Options.Create(new DiscoveryConfigOpts
        {
            Provider = "Radarr",
            Radarr = new RadarrConfigOpts
            {
                BaseUrl = "http://radarr.local:7878",
                ApiKey = "radarr-secret"
            }
        });

        var client = new RadarrClient(
            new HttpClient(handler),
            opts,
            CreateAppJsonOptions(),
            NullLogger<RadarrClient>.Instance);

        var movies = await client.GetMonitoredMoviesAsync(CancellationToken.None);
        movies.Should().HaveCount(2);
        movies[0].Title.Should().Be("Inception");
        movies[0].TmdbId.Should().Be(27205);
        movies[0].ImdbId.Should().Be("tt1375666");
        movies[0].HasFile.Should().BeFalse();
        movies[0].IsAvailable.Should().BeTrue();
        movies[1].HasFile.Should().BeTrue();
        seenApiKey.Should().Be("radarr-secret");

        var triggered = await client.TriggerDownloadedMoviesScanAsync("/storage/media/movies/Inception (2010)", CancellationToken.None);
        triggered.Should().BeTrue();
        seenCommandBody.Should().Contain("\"name\":\"DownloadedMoviesScan\"");
        seenCommandBody.Should().Contain("/storage/media/movies/Inception (2010)");
    }

    [Fact]
    public async Task RadarrDiscoveryProvider_FiltersToMissingAvailableMonitored_AndSkipsProcessed()
    {
        var statePath = Path.Combine(Path.GetTempPath(), $"radarr-state-{Guid.NewGuid():N}.json");
        try
        {
            var jsonOpts = CreateAppJsonOptions();
            var stateStore = new JsonStateStore(
                Options.Create(new StorageConfigOpts { StateFilePath = statePath }),
                jsonOpts,
                NullLogger<JsonStateStore>.Instance);
            await stateStore.InitializeAsync(CancellationToken.None);
            await stateStore.RecordGrabbedAsync("radarr:tmdb:100", "hash100", "Already Grabbed", MediaCategory.Movies, CancellationToken.None);

            var handler = new MockHttpMessageHandler(_ => MockHttpMessageHandler.JsonResponse("""
            [
              { "id": 1, "title": "Already Grabbed", "year": 2025, "tmdbId": 100, "hasFile": false, "monitored": true, "isAvailable": true },
              { "id": 2, "title": "Has File", "year": 2025, "tmdbId": 200, "hasFile": true, "monitored": true, "isAvailable": true },
              { "id": 3, "title": "Not Available", "year": 2027, "tmdbId": 300, "hasFile": false, "monitored": true, "isAvailable": false },
              { "id": 4, "title": "Dune: Part Two", "year": 2024, "tmdbId": 693134, "hasFile": false, "monitored": true, "isAvailable": true }
            ]
            """));

            var opts = Options.Create(new DiscoveryConfigOpts
            {
                Provider = "Radarr",
                Radarr = new RadarrConfigOpts { BaseUrl = "http://radarr.local:7878", ApiKey = "key" }
            });
            var radarrClient = new RadarrClient(new HttpClient(handler), opts, jsonOpts, NullLogger<RadarrClient>.Instance);
            var provider = new RadarrDiscoveryProvider(radarrClient, stateStore, NullLogger<RadarrDiscoveryProvider>.Instance);

            var candidates = await provider.GetWantedCandidatesAsync(CancellationToken.None);

            candidates.Should().ContainSingle();
            candidates[0].ExternalId.Should().Be("radarr:tmdb:693134");
            candidates[0].SearchQuery.Should().Be("Dune: Part Two 2024");
            candidates[0].Category.Should().Be(MediaCategory.Movies);
        }
        finally
        {
            if (File.Exists(statePath)) File.Delete(statePath);
        }
    }

    [Fact]
    public async Task SonarrDiscoveryProvider_EmitsTvCandidatesFromWantedMissing()
    {
        var statePath = Path.Combine(Path.GetTempPath(), $"sonarr-state-{Guid.NewGuid():N}.json");
        try
        {
            var jsonOpts = CreateAppJsonOptions();
            var stateStore = new JsonStateStore(
                Options.Create(new StorageConfigOpts { StateFilePath = statePath }),
                jsonOpts,
                NullLogger<JsonStateStore>.Instance);
            await stateStore.InitializeAsync(CancellationToken.None);

            var handler = new MockHttpMessageHandler(_ => MockHttpMessageHandler.JsonResponse("""
            {
              "page": 1,
              "pageSize": 50,
              "totalRecords": 1,
              "records": [
                {
                  "id": 10,
                  "seriesId": 42,
                  "seasonNumber": 2,
                  "episodeNumber": 3,
                  "title": "Episode 3",
                  "hasFile": false,
                  "monitored": true,
                  "series": { "id": 42, "title": "Severance", "year": 2022, "tvdbId": 371980, "monitored": true }
                }
              ]
            }
            """));

            var opts = Options.Create(new DiscoveryConfigOpts
            {
                Provider = "Sonarr",
                Sonarr = new SonarrConfigOpts { BaseUrl = "http://sonarr.local:8989", ApiKey = "sonarr-key" }
            });
            var sonarrClient = new SonarrClient(new HttpClient(handler), opts, jsonOpts, NullLogger<SonarrClient>.Instance);
            var provider = new SonarrDiscoveryProvider(sonarrClient, stateStore, NullLogger<SonarrDiscoveryProvider>.Instance);

            var candidates = await provider.GetWantedCandidatesAsync(CancellationToken.None);

            candidates.Should().ContainSingle();
            candidates[0].ExternalId.Should().Be("sonarr:42:s02e03");
            candidates[0].SearchQuery.Should().Be("Severance S02E03");
            candidates[0].Category.Should().Be(MediaCategory.Tv);
        }
        finally
        {
            if (File.Exists(statePath)) File.Delete(statePath);
        }
    }

    [Fact]
    public async Task JarvisWebhookDiscoveryProvider_AndHmacValidator_VerifyAndEnqueueBatch()
    {
        const string secret = "super-secret-32-byte-hmac-key-123";
        var json = """{"batch_id":"b1","requested_by":"U123","items":[{"Title":"Severance","Year":2022,"Category":"tv","imdbId":"tt11280740"},{"index":1,"title":"Inception","year":2010,"media_type":"Movie"}]}""";
        var bodyBytes = Encoding.UTF8.GetBytes(json);

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signature = $"sha256={Convert.ToHexStringLower(hmac.ComputeHash(bodyBytes))}";

        HmacSignatureValidator.VerifySignature(secret, bodyBytes, signature).Should().BeTrue();
        HmacSignatureValidator.VerifySignature("wrong-secret", bodyBytes, signature).Should().BeFalse();
        HmacSignatureValidator.VerifySignature(secret, bodyBytes, "sha256=deadbeef").Should().BeFalse();

        var statePath = Path.Combine(Path.GetTempPath(), $"jarvis-state-{Guid.NewGuid():N}.json");
        try
        {
            var jsonOpts = CreateAppJsonOptions();
            var stateStore = new JsonStateStore(
                Options.Create(new StorageConfigOpts { StateFilePath = statePath }),
                jsonOpts,
                NullLogger<JsonStateStore>.Instance);
            await stateStore.InitializeAsync(CancellationToken.None);

            var provider = new JarvisWebhookDiscoveryProvider(stateStore, NullLogger<JarvisWebhookDiscoveryProvider>.Instance);
            var payload = JsonSerializer.Deserialize<JarvisTriggerPayload>(bodyBytes, jsonOpts)!;

            var enqueued = provider.EnqueueBatch(payload);
            enqueued.Should().Be(2);

            var candidates = await provider.GetWantedCandidatesAsync(CancellationToken.None);
            candidates.Should().HaveCount(2);
            candidates[0].ExternalId.Should().Be("jarvis:imdb:tt11280740");
            candidates[0].SearchQuery.Should().Be("Severance 2022");
            candidates[0].Category.Should().Be(MediaCategory.Tv);
            candidates[1].SearchQuery.Should().Be("Inception 2010");
            candidates[1].Category.Should().Be(MediaCategory.Movies);

            // Queue is drained after read
            var secondRead = await provider.GetWantedCandidatesAsync(CancellationToken.None);
            secondRead.Should().BeEmpty();
        }
        finally
        {
            if (File.Exists(statePath)) File.Delete(statePath);
        }
    }

    [Theory]
    [InlineData("Radarr", typeof(RadarrDiscoveryProvider))]
    [InlineData("Sonarr", typeof(SonarrDiscoveryProvider))]
    [InlineData("Jarvis", typeof(JarvisWebhookDiscoveryProvider))]
    public void AddSyncSharpServices_ResolvesConfiguredDiscoveryProvider(string providerName, Type expectedType)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:StateFilePath"] = "/tmp/test-state.json",
                ["Vpn:RequiredInterface"] = "wg0",
                ["Indexer:BaseUrl"] = "http://localhost:9696",
                ["Indexer:ApiKey"] = "key",
                ["Downloader:BaseUrl"] = "http://localhost:8080",
                ["Downloader:StagingDir"] = "/tmp/staging",
                ["Rsync:SshHost"] = "nas.local",
                ["Rsync:SshUser"] = "sync",
                ["Rsync:SshKey"] = "/tmp/id_ed25519",
                ["Discovery:Provider"] = providerName
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSyncSharp(config, addHostedWorker: false);

        using var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<IMediaDiscoveryProvider>();
        provider.Should().BeOfType(expectedType);
    }
}
