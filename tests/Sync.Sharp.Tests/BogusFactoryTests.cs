using System.Reflection;
using Plex.Core.Http.Models;
using Sync.Sharp.Models;
using Sync.Sharp.Models.Options;
using Sync.Sharp.Services;

namespace Sync.Sharp.Tests;

public class BogusFactoryTests
{
    private sealed class UnregisteredPoco;

    [Fact]
    public void BogusFactory_ShouldHaveOnlyOnePublicMethod_CalledCreate()
    {
        var publicMethods = typeof(BogusFactory)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName) // Exclude property getters/setters if any
            .ToList();

        publicMethods.Should().HaveCount(1, "BogusFactory must expose a single public method called Create<T>");
        publicMethods.Single().Name.Should().Be("Create");
        publicMethods.Single().IsGenericMethod.Should().BeTrue();
    }

    [Fact]
    public void Create_PlexMetadataItem_ShouldReturnPopulatedInstance()
    {
        PlexMetadataItem item = BogusFactory.Create<PlexMetadataItem>();

        item.Should().NotBeNull();
        item.Title.Should().NotBeNullOrWhiteSpace();
        item.RatingKey.Should().NotBeNullOrWhiteSpace();
        item.Key.Should().StartWith("/library/metadata/");
        item.Year.Should().BeInRange(1990, 2026);
    }

    [Fact]
    public void Create_TrendingItems_ShouldReturnPopulatedInstances()
    {
        TrendingMovieItem movie = BogusFactory.Create<TrendingMovieItem>();
        movie.Should().NotBeNull();
        movie.EffectiveTitle.Should().NotBeNullOrWhiteSpace();
        movie.ImdbId.Should().StartWith("tt");
        movie.PosterUrl.Should().StartWith("http");
        movie.Category.Should().Be(MediaCategory.Movies);

        TrendingTvItem tv = BogusFactory.Create<TrendingTvItem>();
        tv.Should().NotBeNull();
        tv.EffectiveTitle.Should().NotBeNullOrWhiteSpace();
        tv.ImdbId.Should().StartWith("tt");
        tv.PosterUrl.Should().StartWith("http");
        tv.Category.Should().Be(MediaCategory.Tv);
    }

    [Fact]
    public void Create_MediaHistoryEntry_ShouldReturnPopulatedInstance()
    {
        MediaHistoryEntry entry = BogusFactory.Create<MediaHistoryEntry>();
        entry.Should().NotBeNull();
        entry.InfoHash.Should().HaveLength(40);
        entry.Title.Should().NotBeNullOrWhiteSpace();
        Enum.IsDefined(typeof(MediaCategory), entry.Category).Should().BeTrue();
        Enum.IsDefined(typeof(MediaSyncStatus), entry.Status).Should().BeTrue();
    }

    [Fact]
    public void Create_DiscoveryCandidate_ShouldReturnPopulatedInstance()
    {
        DiscoveryCandidate candidate = BogusFactory.Create<DiscoveryCandidate>();
        candidate.Should().NotBeNull();
        candidate.ExternalId.Should().NotBeNullOrWhiteSpace();
        candidate.SearchQuery.Should().NotBeNullOrWhiteSpace();
        Enum.IsDefined(typeof(MediaCategory), candidate.Category).Should().BeTrue();
    }

    [Fact]
    public void Create_ProwlarrRelease_ShouldReturnValidRelease()
    {
        ProwlarrRelease release = BogusFactory.Create<ProwlarrRelease>();

        release.Should().NotBeNull();
        release.Title.Should().NotBeNullOrWhiteSpace();
        release.InfoHash.Should().HaveLength(40);
        release.MagnetUrl.Should().StartWith("magnet:?xt=urn:btih:");
        release.Size.Should().BeGreaterThan(0);
        release.Seeders.Should().BeGreaterThanOrEqualTo(5);
        release.Categories.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Create_QBitTorrentItem_ShouldReturnValidTorrent()
    {
        QBitTorrentItem torrent = BogusFactory.Create<QBitTorrentItem>();

        torrent.Should().NotBeNull();
        torrent.Hash.Should().HaveLength(40);
        torrent.Name.Should().EndWith(".mkv");
        torrent.Progress.Should().BeInRange(0.0, 1.0);
        torrent.Size.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Create_SyncStateDocument_ShouldReturnDocumentWithPopulatedItems()
    {
        SyncStateDocument doc = BogusFactory.Create<SyncStateDocument>();

        doc.Should().NotBeNull();
        doc.Items.Should().NotBeNull();
        doc.Items.Should().NotBeEmpty();
        doc.Items.First().InfoHash.Should().HaveLength(40);
        doc.Items.First().Title.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Create_OptionsClasses_ShouldReturnValidConfigurations()
    {
        DownloaderConfigOpts downloader = BogusFactory.Create<DownloaderConfigOpts>();
        IndexerConfigOpts indexer = BogusFactory.Create<IndexerConfigOpts>();
        RsyncConfigOpts rsync = BogusFactory.Create<RsyncConfigOpts>();
        VpnConfigOpts vpn = BogusFactory.Create<VpnConfigOpts>();

        downloader.BaseUrl.Should().NotBeNullOrWhiteSpace();
        indexer.ApiKey.Should().NotBeNullOrWhiteSpace();
        rsync.SshHost.Should().NotBeNullOrWhiteSpace();
        vpn.RequiredInterface.Should().Be("nordlynx");
    }

    [Fact]
    public void Create_List_ShouldReturnSpecifiedCountOfItems()
    {
        var count = 5;
        List<PlexMetadataItem> items = BogusFactory.Create<List<PlexMetadataItem>>(count);

        items.Should().NotBeNull();
        items.Should().HaveCount(count);
        items.All(i => !string.IsNullOrWhiteSpace(i.Title)).Should().BeTrue();
    }

    [Fact]
    public void Create_IReadOnlyList_ShouldReturnSpecifiedCount()
    {
        var count = 3;
        IReadOnlyList<ProwlarrRelease> releases = BogusFactory.Create<IReadOnlyList<ProwlarrRelease>>(count);

        releases.Should().NotBeNull();
        releases.Should().HaveCount(count);
    }

    [Fact]
    public void Create_Array_ShouldReturnPopulatedArray()
    {
        var count = 4;
        QBitTorrentFileItem[] files = BogusFactory.Create<QBitTorrentFileItem[]>(count);

        files.Should().NotBeNull();
        files.Should().HaveCount(count);
        files.All(f => f.Size > 0).Should().BeTrue();
    }

    [Fact]
    public void Create_WithCountGreaterThanOneOnNonCollection_ShouldThrowArgumentException()
    {
        var action = () => BogusFactory.Create<QBitTorrentFileItem>(5);

        action.Should().Throw<ArgumentException>()
            .WithMessage("*not a collection*");
    }

    [Fact]
    public void Create_WithGenericCollectionType_UsingVar_ShouldYieldStronglyTypedList()
    {
        var files = BogusFactory.Create<List<QBitTorrentFileItem>>(5);

        files.Should().NotBeNull();
        files.Should().BeOfType<List<QBitTorrentFileItem>>();
        files.Should().HaveCount(5);
        files.All(f => f.Size > 0).Should().BeTrue();
    }

    [Fact]
    public void Create_WithDefaultCountOne_ShouldReturnSingleInstanceNotList()
    {
        PlexMetadataItem movie = BogusFactory.Create<PlexMetadataItem>();

        movie.Should().NotBeNull();
        movie.Should().BeOfType<PlexMetadataItem>();
        movie.Title.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Create_UnregisteredType_ShouldThrowNotSupportedException()
    {
        var action = () => BogusFactory.Create<UnregisteredPoco>();

        action.Should().Throw<NotSupportedException>()
            .WithMessage("*No Bogus factory registered for type 'UnregisteredPoco'*");
    }
}
