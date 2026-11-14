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
        var item = BogusFactory.Create<PlexMetadataItem>();

        item.Should().NotBeNull();
        item.Title.Should().NotBeNullOrWhiteSpace();
        item.RatingKey.Should().NotBeNullOrWhiteSpace();
        item.Key.Should().StartWith("/library/metadata/");
        item.Year.Should().BeInRange(1990, 2026);
    }

    [Fact]
    public void Create_ProwlarrRelease_ShouldReturnValidRelease()
    {
        var release = BogusFactory.Create<ProwlarrRelease>();

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
        var torrent = BogusFactory.Create<QBitTorrentItem>();

        torrent.Should().NotBeNull();
        torrent.Hash.Should().HaveLength(40);
        torrent.Name.Should().EndWith(".mkv");
        torrent.Progress.Should().BeInRange(0.0, 1.0);
        torrent.Size.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Create_SyncStateDocument_ShouldReturnDocumentWithPopulatedItems()
    {
        var doc = BogusFactory.Create<SyncStateDocument>();

        doc.Should().NotBeNull();
        doc.Items.Should().NotBeNull();
        doc.Items.Should().NotBeEmpty();
        doc.Items.First().InfoHash.Should().HaveLength(40);
        doc.Items.First().Title.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Create_OptionsClasses_ShouldReturnValidConfigurations()
    {
        var downloader = BogusFactory.Create<DownloaderConfigOpts>();
        var indexer = BogusFactory.Create<IndexerConfigOpts>();
        var rsync = BogusFactory.Create<RsyncConfigOpts>();
        var vpn = BogusFactory.Create<VpnConfigOpts>();

        downloader.BaseUrl.Should().NotBeNullOrWhiteSpace();
        indexer.ApiKey.Should().NotBeNullOrWhiteSpace();
        rsync.SshHost.Should().NotBeNullOrWhiteSpace();
        vpn.RequiredInterface.Should().Be("nordlynx");
    }

    [Fact]
    public void Create_List_ShouldReturnSpecifiedCountOfItems()
    {
        var count = 5;
        var items = BogusFactory.Create<List<PlexMetadataItem>>(count);

        items.Should().NotBeNull();
        items.Should().HaveCount(count);
        items.All(i => !string.IsNullOrWhiteSpace(i.Title)).Should().BeTrue();
    }

    [Fact]
    public void Create_IReadOnlyList_ShouldReturnSpecifiedCount()
    {
        var count = 3;
        var releases = BogusFactory.Create<IReadOnlyList<ProwlarrRelease>>(count);

        releases.Should().NotBeNull();
        releases.Should().HaveCount(count);
    }

    [Fact]
    public void Create_Array_ShouldReturnPopulatedArray()
    {
        var count = 4;
        var files = BogusFactory.Create<QBitTorrentFileItem[]>(count);

        files.Should().NotBeNull();
        files.Should().HaveCount(count);
        files.All(f => f.Size > 0).Should().BeTrue();
    }

    [Fact]
    public void Create_WithCountGreaterThanOneOnNonCollection_ShouldThrowArgumentException()
    {
        var action = () => BogusFactory.Create<PlexMetadataItem>(5);

        action.Should().Throw<ArgumentException>()
            .WithMessage("*not a collection*");
    }

    [Fact]
    public void Create_WithCountZeroOrNegative_ShouldThrowArgumentOutOfRangeException()
    {
        var action = () => BogusFactory.Create<PlexMetadataItem>(0);

        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Create_UnregisteredType_ShouldThrowNotSupportedException()
    {
        var action = () => BogusFactory.Create<UnregisteredPoco>();

        action.Should().Throw<NotSupportedException>()
            .WithMessage("*No Bogus factory registered for type 'UnregisteredPoco'*");
    }
}
