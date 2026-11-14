using System.Collections.Concurrent;
using Bogus;
using Plex.Core.Http.Models;
using Sync.Sharp.Models;
using Sync.Sharp.Models.Options;
using Sync.Sharp.Services;

namespace Sync.Sharp.Tests;

/// <summary>
/// Centralized test data factory powered by Bogus for creating domain objects,
/// DTOs, options, and collections with realistic test data fixtures.
/// </summary>
public static class BogusFactory
{
    private interface IFakerRegistration
    {
        object GenerateOne();
        object GenerateMany(int count);
        Array GenerateArray(int count);
    }

    private sealed class FakerRegistration<TItem>(Faker<TItem> faker) : IFakerRegistration where TItem : class
    {
        public object GenerateOne() => faker.Generate();
        public object GenerateMany(int count) => faker.Generate(count);
        public Array GenerateArray(int count) => faker.Generate(count).ToArray();
    }

    private static readonly Dictionary<Type, IFakerRegistration> Registrations = new()
    {
        [typeof(PlexMetadataItem)] = new FakerRegistration<PlexMetadataItem>(CreatePlexMetadataItemFaker()),
        [typeof(PlexLibrarySection)] = new FakerRegistration<PlexLibrarySection>(CreatePlexLibrarySectionFaker()),
        [typeof(ProwlarrRelease)] = new FakerRegistration<ProwlarrRelease>(CreateProwlarrReleaseFaker()),
        [typeof(ProwlarrCategory)] = new FakerRegistration<ProwlarrCategory>(CreateProwlarrCategoryFaker()),
        [typeof(QBitTorrentItem)] = new FakerRegistration<QBitTorrentItem>(CreateQBitTorrentItemFaker()),
        [typeof(QBitTorrentFileItem)] = new FakerRegistration<QBitTorrentFileItem>(CreateQBitTorrentFileItemFaker()),
        [typeof(MediaHistoryEntry)] = new FakerRegistration<MediaHistoryEntry>(CreateMediaHistoryEntryFaker()),
        [typeof(SyncStateDocument)] = new FakerRegistration<SyncStateDocument>(CreateSyncStateDocumentFaker()),
        [typeof(DiscoveryCandidate)] = new FakerRegistration<DiscoveryCandidate>(CreateDiscoveryCandidateFaker()),
        [typeof(TrendingMovieItem)] = new FakerRegistration<TrendingMovieItem>(CreateTrendingMovieItemFaker()),
        [typeof(TrendingTvItem)] = new FakerRegistration<TrendingTvItem>(CreateTrendingTvItemFaker()),
        [typeof(CategoryFilterConfigOpts)] = new FakerRegistration<CategoryFilterConfigOpts>(CreateCategoryFilterConfigOptsFaker()),
        [typeof(DownloaderConfigOpts)] = new FakerRegistration<DownloaderConfigOpts>(CreateDownloaderConfigOptsFaker()),
        [typeof(FiltersConfigOpts)] = new FakerRegistration<FiltersConfigOpts>(CreateFiltersConfigOptsFaker()),
        [typeof(IndexerConfigOpts)] = new FakerRegistration<IndexerConfigOpts>(CreateIndexerConfigOptsFaker()),
        [typeof(RsyncConfigOpts)] = new FakerRegistration<RsyncConfigOpts>(CreateRsyncConfigOptsFaker()),
        [typeof(SchedulingConfigOpts)] = new FakerRegistration<SchedulingConfigOpts>(CreateSchedulingConfigOptsFaker()),
        [typeof(TrendingConfigOpts)] = new FakerRegistration<TrendingConfigOpts>(CreateTrendingConfigOptsFaker()),
        [typeof(VpnConfigOpts)] = new FakerRegistration<VpnConfigOpts>(CreateVpnConfigOptsFaker())
    };

    /// <summary>
    /// Generates a single instance of <typeparamref name="T"/> when <paramref name="count"/> is 1,
    /// or a <see cref="List{T}"/> containing <paramref name="count"/> instances when <paramref name="count"/> is greater than 1.
    /// Collections can also be requested explicitly (e.g. <c>Create&lt;List&lt;T&gt;&gt;(count)</c> or <c>Create&lt;T[]&gt;(count)</c>).
    /// </summary>
    /// <typeparam name="T">The registered POCO type or collection of registered POCOs to create.</typeparam>
    /// <param name="count">The number of instances to generate (defaults to 1).</param>
    /// <returns>
    /// A single instance of <typeparamref name="T"/> when <paramref name="count"/> is 1;
    /// otherwise a <see cref="List{T}"/> of generated instances.
    /// </returns>
    public static dynamic Create<T>(int count = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1, nameof(count));

        var targetType = typeof(T);

        // Case 1: Direct POCO registration
        if (Registrations.TryGetValue(targetType, out var registration))
        {
            if (count > 1)
            {
                return registration.GenerateMany(count);
            }

            return registration.GenerateOne();
        }

        // Case 2: Array of registered type
        if (targetType.IsArray)
        {
            var elementType = targetType.GetElementType()!;
            if (Registrations.TryGetValue(elementType, out var arrayRegistration))
            {
                return arrayRegistration.GenerateArray(count);
            }
        }

        // Case 3: Generic collection of registered type (List<T>, IReadOnlyList<T>, IEnumerable<T>, etc.)
        if (targetType.IsGenericType)
        {
            var genericArgs = targetType.GetGenericArguments();
            if (genericArgs.Length == 1 && Registrations.TryGetValue(genericArgs[0], out var collectionRegistration))
            {
                return collectionRegistration.GenerateMany(count);
            }
        }

        throw new NotSupportedException(
            $"No Bogus factory registered for type '{targetType.Name}'. " +
            $"Registered types: {string.Join(", ", Registrations.Keys.Select(k => k.Name))}");
    }

    private static Faker<PlexMetadataItem> CreatePlexMetadataItemFaker() =>
        new Faker<PlexMetadataItem>()
            .CustomInstantiator(f =>
            {
                var ratingKey = f.Random.Number(1000, 99999).ToString();
                var title = f.PickRandom("Inception", "Interstellar", "Dune", "The Dark Knight", "Severance", "Breaking Bad");
                return new PlexMetadataItem
                {
                    RatingKey = ratingKey,
                    Key = $"/library/metadata/{ratingKey}",
                    Guid = $"plex://movie/{f.Random.AlphaNumeric(24)}",
                    Title = title,
                    OriginalTitle = title,
                    Type = f.PickRandom("movie", "show"),
                    Year = f.Random.Number(1990, 2026),
                    Summary = f.Lorem.Paragraph(),
                    Duration = f.Random.Number(1800000, 7200000)
                };
            });

    private static Faker<PlexLibrarySection> CreatePlexLibrarySectionFaker() =>
        new Faker<PlexLibrarySection>()
            .CustomInstantiator(f =>
            {
                var id = f.Random.Number(1, 10).ToString();
                return new PlexLibrarySection
                {
                    Key = id,
                    Title = f.PickRandom("Movies", "TV Shows", "Anime", "Documentaries"),
                    Type = f.PickRandom("movie", "show"),
                    Agent = "tv.plex.agents.movie",
                    Scanner = "Plex Movie",
                    Language = "en-US",
                    Uuid = f.Random.Uuid().ToString()
                };
            });

    private static Faker<ProwlarrCategory> CreateProwlarrCategoryFaker() =>
        new Faker<ProwlarrCategory>()
            .CustomInstantiator(f => new ProwlarrCategory(
                Id: f.PickRandom(2000, 2040, 5000, 5040),
                Name: f.PickRandom("Movies", "Movies/HD", "TV", "TV/HD")
            ));

    private static Faker<ProwlarrRelease> CreateProwlarrReleaseFaker() =>
        new Faker<ProwlarrRelease>()
            .CustomInstantiator(f =>
            {
                var hash = f.Random.Hexadecimal(40, "").ToLowerInvariant();
                var title = $"{f.Lorem.Word().Replace(" ", ".")}.{f.Random.Number(2010, 2026)}.1080p.WEB-DL.DDP5.1.H.264";
                return new ProwlarrRelease(
                    Guid: f.Random.AlphaNumeric(32),
                    Title: title,
                    Size: f.Random.Long(500L * 1024 * 1024, 15L * 1024 * 1024 * 1024),
                    IndexerId: f.Random.Number(1, 10),
                    Indexer: f.PickRandom("TorrentLeech", "IPTorrents", "FL", "Gazelle"),
                    InfoHash: hash,
                    MagnetUrl: $"magnet:?xt=urn:btih:{hash}&dn={Uri.EscapeDataString(title)}",
                    DownloadUrl: f.Internet.UrlWithPath(),
                    Seeders: f.Random.Number(5, 500),
                    Leechers: f.Random.Number(0, 50),
                    PublishDate: f.Date.RecentOffset(30),
                    Categories: [new ProwlarrCategory(2000, "Movies")]
                );
            });

    private static Faker<QBitTorrentItem> CreateQBitTorrentItemFaker() =>
        new Faker<QBitTorrentItem>()
            .CustomInstantiator(f =>
            {
                var hash = f.Random.Hexadecimal(40, "").ToLowerInvariant();
                var name = $"{f.Lorem.Word().Replace(" ", ".")}.{f.Random.Number(2015, 2026)}.1080p.mkv";
                var category = f.PickRandom("movies", "tv");
                return new QBitTorrentItem(
                    Hash: hash,
                    Name: name,
                    State: f.PickRandom("pausedUP", "uploading", "stalledUP", "downloading", "pausedDL"),
                    Progress: f.Random.Double(0.0, 1.0),
                    Size: f.Random.Long(1_000_000_000, 8_000_000_000),
                    Ratio: f.Random.Double(0.0, 3.5),
                    Seeding_Time: f.Random.Long(0, 86400),
                    Category: category,
                    Content_Path: $"/downloads/{category}/{name}",
                    Save_Path: $"/downloads/{category}"
                );
            });

    private static Faker<QBitTorrentFileItem> CreateQBitTorrentFileItemFaker() =>
        new Faker<QBitTorrentFileItem>()
            .CustomInstantiator(f => new QBitTorrentFileItem(
                Index: f.Random.Number(0, 100),
                Name: $"{f.Lorem.Word()}.1080p.mkv",
                Size: f.Random.Long(500_000_000, 4_000_000_000),
                Progress: f.Random.Double(0.0, 1.0),
                Priority: 1,
                Is_Seed: false
            ));

    private static Faker<MediaHistoryEntry> CreateMediaHistoryEntryFaker() =>
        new Faker<MediaHistoryEntry>()
            .CustomInstantiator(f => new MediaHistoryEntry(
                ExternalId: $"imdb:tt{f.Random.Number(1000000, 9999999)}",
                InfoHash: f.Random.Hexadecimal(40, "").ToLowerInvariant(),
                Title: f.Lorem.Sentence(3).TrimEnd('.'),
                Category: f.PickRandom("movies", "tv"),
                Status: f.PickRandom("grabbed", "synced"),
                CreatedAt: f.Date.RecentOffset(14),
                SyncedAt: f.Random.Bool() ? f.Date.RecentOffset(7) : null
            ));

    private static Faker<SyncStateDocument> CreateSyncStateDocumentFaker() =>
        new Faker<SyncStateDocument>()
            .CustomInstantiator(f =>
            {
                var entryFaker = CreateMediaHistoryEntryFaker();
                return new SyncStateDocument(entryFaker.Generate(f.Random.Number(1, 5)));
            });

    private static Faker<DiscoveryCandidate> CreateDiscoveryCandidateFaker() =>
        new Faker<DiscoveryCandidate>()
            .CustomInstantiator(f =>
            {
                var category = f.PickRandom("movies", "tv");
                var query = f.Lorem.Word();
                return new DiscoveryCandidate(
                    ExternalId: $"custom:{category}:{query.ToLowerInvariant()}",
                    SearchQuery: query,
                    Category: category
                );
            });

    private static Faker<TrendingMovieItem> CreateTrendingMovieItemFaker() =>
        new Faker<TrendingMovieItem>()
            .CustomInstantiator(f => new TrendingMovieItem(
                Title: f.Lorem.Sentence(2).TrimEnd('.'),
                Name: null,
                Imdb_Id: $"tt{f.Random.Number(1000000, 9999999)}",
                Poster_Url: f.Internet.Url()
            ));

    private static Faker<TrendingTvItem> CreateTrendingTvItemFaker() =>
        new Faker<TrendingTvItem>()
            .CustomInstantiator(f => new TrendingTvItem(
                Title: f.Lorem.Sentence(2).TrimEnd('.'),
                Name: null,
                ShowName: null,
                Imdb_Id: $"tt{f.Random.Number(1000000, 9999999)}",
                Poster_Url: f.Internet.Url()
            ));

    private static Faker<CategoryFilterConfigOpts> CreateCategoryFilterConfigOptsFaker() =>
        new Faker<CategoryFilterConfigOpts>()
            .CustomInstantiator(f => new CategoryFilterConfigOpts
            {
                CategoryIds = [2000, 2040],
                MinSizeMb = 500,
                MaxSizeMb = 25000,
                AllowedPatterns = ["1080p", "2160p"],
                BlockedPatterns = ["720p", "CAM", "TS"],
                PreferSeasonPacks = f.Random.Bool()
            });

    private static Faker<DownloaderConfigOpts> CreateDownloaderConfigOptsFaker() =>
        new Faker<DownloaderConfigOpts>()
            .CustomInstantiator(f => new DownloaderConfigOpts
            {
                Client = "qbittorrent",
                BaseUrl = "http://127.0.0.1:8080",
                Username = f.Internet.UserName(),
                Password = f.Internet.Password(),
                StagingDir = "/downloads",
                RemotePathPrefix = "/downloads",
                RatioLimit = 1.0,
                SeedingTimeLimitMinutes = 1440
            });

    private static Faker<FiltersConfigOpts> CreateFiltersConfigOptsFaker() =>
        new Faker<FiltersConfigOpts>()
            .CustomInstantiator(_ =>
            {
                var catFaker = CreateCategoryFilterConfigOptsFaker();
                return new FiltersConfigOpts
                {
                    Movies = catFaker.Generate(),
                    Tv = catFaker.Generate()
                };
            });

    private static Faker<IndexerConfigOpts> CreateIndexerConfigOptsFaker() =>
        new Faker<IndexerConfigOpts>()
            .CustomInstantiator(f => new IndexerConfigOpts
            {
                Type = "prowlarr",
                BaseUrl = "http://127.0.0.1:9696",
                ApiKey = f.Random.AlphaNumeric(32),
                MinSeeders = 10,
                MaxGrabPerRun = 5
            });

    private static Faker<RsyncConfigOpts> CreateRsyncConfigOptsFaker() =>
        new Faker<RsyncConfigOpts>()
            .CustomInstantiator(f => new RsyncConfigOpts
            {
                Enabled = true,
                VerifyWithFfprobe = true,
                SshUser = f.Internet.UserName(),
                SshHost = f.Internet.Ip(),
                SshKey = "/home/user/.ssh/id_ed25519",
                DestMoviesDir = "/mnt/export/storage/plex/media/Movies",
                DestTvDir = "/mnt/export/storage/plex/media/TV Shows",
                Chown = "1000:1000",
                RemoveLocalAfterSync = false
            });

    private static Faker<SchedulingConfigOpts> CreateSchedulingConfigOptsFaker() =>
        new Faker<SchedulingConfigOpts>()
            .CustomInstantiator(f => new SchedulingConfigOpts
            {
                IntervalMinutes = 15,
                CronExpression = "*/15 * * * *",
                LockFile = "/tmp/sync-sharp.lock",
                WebhookPort = 8089,
                WebhookSecretToken = f.Random.AlphaNumeric(16),
                StateFilePath = "/tmp/sync-sharp-state.json"
            });

    private static Faker<TrendingConfigOpts> CreateTrendingConfigOptsFaker() =>
        new Faker<TrendingConfigOpts>()
            .CustomInstantiator(f => new TrendingConfigOpts
            {
                Enabled = true,
                PopularMoviesUrl = f.Internet.Url(),
                PopularTvUrl = f.Internet.Url(),
                MovieFeedUrls = [],
                TvFeedUrls = [],
                CustomQueries = [f.Lorem.Word(), f.Lorem.Word()],
                CustomTvQueries = [f.Lorem.Word()]
            });

    private static Faker<VpnConfigOpts> CreateVpnConfigOptsFaker() =>
        new Faker<VpnConfigOpts>()
            .CustomInstantiator(f => new VpnConfigOpts
            {
                Provider = "nordlynx",
                RequiredInterface = "nordlynx",
                PauseTorrentsWhenDisconnected = f.Random.Bool()
            });
}
