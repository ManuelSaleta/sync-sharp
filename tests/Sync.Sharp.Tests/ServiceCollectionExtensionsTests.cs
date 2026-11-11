using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Plex.Core.Http;
using Sync.Sharp.Extensions;
using Sync.Sharp.Models.Options;
using Sync.Sharp.Services;

namespace Sync.Sharp.Tests;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddSyncSharp_ShouldRegisterAllCoreServicesAndOptions()
    {
        var configData = new Dictionary<string, string?>
        {
            ["Schedule:IntervalMinutes"] = "15",
            ["Schedule:StateFilePath"] = "/tmp/test-state.json",
            ["Vpn:Provider"] = "nordlynx",
            ["Vpn:RequiredInterface"] = "wg0",
            ["Indexer:BaseUrl"] = "http://127.0.0.1:9696",
            ["Indexer:ApiKey"] = "test-api-key",
            ["Downloader:BaseUrl"] = "http://127.0.0.1:8080",
            ["Downloader:Password"] = "test-pass",
            ["Downloader:StagingDir"] = "/tmp/downloads",
            ["PlexClientOptions:ServerUrl"] = "http://192.168.1.50:32400",
            ["PlexClientOptions:Token"] = "plex-token-123"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSyncSharp(configuration, addHostedWorker: false);

        using var provider = services.BuildServiceProvider();

        provider.GetService<IPlexClient>().Should().NotBeNull();
        provider.GetService<IMediaPollingService>().Should().NotBeNull();
        provider.GetService<VpnGuardService>().Should().NotBeNull();
        provider.GetService<JsonStateStore>().Should().NotBeNull();
        provider.GetService<ProwlarrClient>().Should().NotBeNull();
        provider.GetService<QBitClient>().Should().NotBeNull();
        provider.GetService<MediaTransferService>().Should().NotBeNull();

        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetService<SyncOrchestrator>().Should().NotBeNull();

        var indexerOpts = provider.GetRequiredService<IOptions<IndexerConfigOpts>>().Value;
        indexerOpts.ApiKey.Should().Be("test-api-key");

        var scheduleOpts = provider.GetRequiredService<IOptions<SchedulingConfigOpts>>().Value;
        scheduleOpts.IntervalMinutes.Should().Be(15);
    }

    [Fact]
    public void FlexibleStringJsonConverter_ShouldReadNumbersAndBooleansAsStrings()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new FlexibleStringJsonConverter());

        JsonSerializer.Deserialize<string>("65592", options).Should().Be("65592");
        JsonSerializer.Deserialize<string>("true", options).Should().Be("true");
        JsonSerializer.Deserialize<string>("\"hello\"", options).Should().Be("hello");
    }
}
