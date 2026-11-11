using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Plex.Core.Http;
using Plex.Core.Http.Extensions;
using Sync.Sharp.Models.Options;
using Sync.Sharp.Services;
using Sync.Sharp.Workers;

namespace Sync.Sharp.Extensions;

/// <summary>
/// Dependency injection and configuration extensions for registering Sync.Sharp services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Sync.Sharp external configuration overlays and flat environment variable mappings to the <see cref="IConfigurationBuilder"/>.
    /// </summary>
    public static IConfigurationBuilder AddSyncSharpConfiguration(
        this IConfigurationBuilder configuration,
        string? customConfigPath = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        customConfigPath ??= Environment.GetEnvironmentVariable("CONFIG_PATH");
        if (string.IsNullOrWhiteSpace(customConfigPath))
        {
            var candidates = new[]
            {
                "appsettings.local.json",
                "../appsettings.local.json",
                "../../appsettings.local.json",
                Path.Combine(AppContext.BaseDirectory, "appsettings.local.json")
            };

            customConfigPath = candidates.FirstOrDefault(File.Exists);
        }

        if (!string.IsNullOrWhiteSpace(customConfigPath) && File.Exists(customConfigPath))
        {
            customConfigPath = Path.GetFullPath(customConfigPath);
            Console.WriteLine($"Loaded external configuration overlay from '{customConfigPath}'.");
            configuration.AddJsonFile(customConfigPath, optional: true, reloadOnChange: true);
        }

        var flatEnvMappings = new Dictionary<string, string?>
        {
            ["Indexer:ApiKey"] = Environment.GetEnvironmentVariable("PROWLARR_API_KEY"),
            ["Indexer:BaseUrl"] = Environment.GetEnvironmentVariable("PROWLARR_BASE_URL"),
            ["Downloader:Password"] = Environment.GetEnvironmentVariable("QBIT_PASSWORD"),
            ["Downloader:Username"] = Environment.GetEnvironmentVariable("QBIT_USERNAME"),
            ["Downloader:BaseUrl"] = Environment.GetEnvironmentVariable("QBIT_BASE_URL"),
            ["Schedule:WebhookSecretToken"] = Environment.GetEnvironmentVariable("WEBHOOK_SECRET_TOKEN"),
            ["PlexClientOptions:ServerUrl"] = Environment.GetEnvironmentVariable("PLEX_SERVER_URL"),
            ["PlexClientOptions:Token"] = Environment.GetEnvironmentVariable("PLEX_TOKEN"),
            ["Trending:Enabled"] = Environment.GetEnvironmentVariable("ENABLE_TRENDING_FEEDS"),
            ["Trending:PopularMoviesUrl"] = Environment.GetEnvironmentVariable("POPULAR_MOVIES_URL"),
            ["Trending:PopularTvUrl"] = Environment.GetEnvironmentVariable("POPULAR_TV_URL")
        };

        var activeOverrides = flatEnvMappings
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        if (activeOverrides.Count > 0)
        {
            configuration.AddInMemoryCollection(activeOverrides!);
        }

        return configuration;
    }

    /// <summary>
    /// Registers all Sync.Sharp options, HTTP clients, orchestrator services, and background worker into the <see cref="IServiceCollection"/>.
    /// </summary>
    public static IServiceCollection AddSyncSharp(
        this IServiceCollection services,
        IConfiguration configuration,
        bool addHostedWorker = true)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<SchedulingConfigOpts>()
            .Bind(configuration.GetSection(SchedulingConfigOpts.SectionName));

        services.AddOptions<VpnConfigOpts>()
            .Bind(configuration.GetSection(VpnConfigOpts.SectionName));

        services.AddOptions<TrendingConfigOpts>()
            .Bind(configuration.GetSection(TrendingConfigOpts.SectionName));

        services.AddOptions<IndexerConfigOpts>()
            .Bind(configuration.GetSection(IndexerConfigOpts.SectionName));

        services.AddOptions<FiltersConfigOpts>()
            .Bind(configuration.GetSection(FiltersConfigOpts.SectionName));

        services.AddOptions<DownloaderConfigOpts>()
            .Bind(configuration.GetSection(DownloaderConfigOpts.SectionName));

        services.AddOptions<RsyncConfigOpts>()
            .Bind(configuration.GetSection(RsyncConfigOpts.SectionName));

        services.AddOptions<PlexClientOptions>()
            .Bind(configuration.GetSection("PlexClientOptions"));

        var flexibleStringConverter = new FlexibleStringJsonConverter();
        if (!PlexClient.DefaultJsonOptions.Converters.Any(c => c is FlexibleStringJsonConverter))
        {
            PlexClient.DefaultJsonOptions.Converters.Add(flexibleStringConverter);
        }

        var jsonSerializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true,
            Converters = { flexibleStringConverter }
        };
        services.AddSingleton(jsonSerializerOptions);

        services.AddSingleton<VpnGuardService>();
        services.AddSingleton<JsonStateStore>();

        services.AddPlexClient(options =>
        {
            var serverUrl = configuration["PlexClientOptions:ServerUrl"];
            if (!string.IsNullOrWhiteSpace(serverUrl))
            {
                options.ServerUrl = serverUrl;
            }

            var token = configuration["PlexClientOptions:Token"];
            if (!string.IsNullOrWhiteSpace(token))
            {
                options.Token = token;
            }

            options.ClientIdentifier = configuration["PlexClientOptions:ClientIdentifier"] ?? "plex-media-polling-client";
            options.Product = "Sync.Sharp";
            options.Version = "1.0.0";
        });

        services.AddHttpClient<TrendingFeedClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Sync.Sharp/1.0");
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All
        });

        services.AddHttpClient<ProwlarrClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddHttpClient<QBitClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            UseCookies = true,
            CookieContainer = new CookieContainer()
        });

        services.AddSingleton<MediaTransferService>();
        services.AddSingleton<MediaPollingService>();
        services.AddSingleton<IMediaPollingService>(sp => sp.GetRequiredService<MediaPollingService>());
        services.AddScoped<SyncOrchestrator>();

        if (addHostedWorker)
        {
            services.AddHostedService<SyncSchedulerWorker>();
        }

        return services;
    }
}
