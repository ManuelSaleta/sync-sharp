using System.Text.Json;
using Sync.Sharp.Models;

namespace Sync.Sharp.Tests;

public class DomainPrimitivesTests
{
    [Theory]
    [InlineData("tv", MediaCategory.Tv)]
    [InlineData("TV", MediaCategory.Tv)]
    [InlineData("show", MediaCategory.Tv)]
    [InlineData("shows", MediaCategory.Tv)]
    [InlineData("movies", MediaCategory.Movies)]
    [InlineData("MOVIES", MediaCategory.Movies)]
    [InlineData("movie", MediaCategory.Movies)]
    public void MediaCategoryParser_Parse_ShouldParseCorrectly(string input, MediaCategory expected)
    {
        var result = MediaCategoryParser.Parse(input);
        result.Should().Be(expected);

        var tryParsed = MediaCategoryParser.TryParse(input, out var category);
        tryParsed.Should().BeTrue();
        category.Should().Be(expected);
    }

    [Fact]
    public void MediaCategoryParser_Parse_InvalidInput_ShouldThrowArgumentException()
    {
        var action = () => MediaCategoryParser.Parse("invalid-category");
        action.Should().Throw<ArgumentException>();

        MediaCategoryParser.TryParse("invalid-category", out var category).Should().BeFalse();
        category.Should().Be(MediaCategory.Movies);

        MediaCategoryParser.FromStringOrDefault("invalid-category", MediaCategory.Tv).Should().Be(MediaCategory.Tv);
    }

    [Fact]
    public void MediaCategory_Extensions_ShouldMapCorrectly()
    {
        MediaCategory.Tv.ToNormalizedString().Should().Be("tv");
        MediaCategory.Movies.ToNormalizedString().Should().Be("movies");

        MediaCategory.Tv.ToPlexTargetType().Should().Be("show");
        MediaCategory.Movies.ToPlexTargetType().Should().Be("movie");

        MediaCategory.Tv.ToPlexSectionKey().Should().Be("My TV Shows");
        MediaCategory.Movies.ToPlexSectionKey().Should().Be("My Movies");

        MediaCategory.Tv.IsTv().Should().BeTrue();
        MediaCategory.Tv.IsMovies().Should().BeFalse();
        MediaCategory.Movies.IsMovies().Should().BeTrue();
        MediaCategory.Movies.IsTv().Should().BeFalse();
    }

    [Fact]
    public void MediaCategory_JsonSerialization_ShouldSerializeAndDeserialize()
    {
        var category = MediaCategory.Tv;
        var json = JsonSerializer.Serialize(category);
        json.Should().Be("\"tv\"");

        var deserializedTv = JsonSerializer.Deserialize<MediaCategory>("\"tv\"");
        deserializedTv.Should().Be(MediaCategory.Tv);

        var deserializedShows = JsonSerializer.Deserialize<MediaCategory>("\"shows\"");
        deserializedShows.Should().Be(MediaCategory.Tv);

        var deserializedMovies = JsonSerializer.Deserialize<MediaCategory>("\"movies\"");
        deserializedMovies.Should().Be(MediaCategory.Movies);
    }

    [Theory]
    [InlineData("grabbed", MediaSyncStatus.Grabbed)]
    [InlineData("GRABBED", MediaSyncStatus.Grabbed)]
    [InlineData("Grabbed", MediaSyncStatus.Grabbed)]
    [InlineData("synced", MediaSyncStatus.Synced)]
    [InlineData("SYNCED", MediaSyncStatus.Synced)]
    [InlineData("Synced", MediaSyncStatus.Synced)]
    public void MediaSyncStatusParser_Parse_ShouldParseCorrectly(string input, MediaSyncStatus expected)
    {
        var result = MediaSyncStatusParser.Parse(input);
        result.Should().Be(expected);

        var tryParsed = MediaSyncStatusParser.TryParse(input, out var status);
        tryParsed.Should().BeTrue();
        status.Should().Be(expected);
    }

    [Fact]
    public void MediaSyncStatusParser_Parse_InvalidInput_ShouldThrowArgumentException()
    {
        var action = () => MediaSyncStatusParser.Parse("unknown-status");
        action.Should().Throw<ArgumentException>();

        MediaSyncStatusParser.TryParse("unknown-status", out var status).Should().BeFalse();
        status.Should().Be(MediaSyncStatus.Grabbed);

        MediaSyncStatusParser.FromStringOrDefault("unknown-status", MediaSyncStatus.Synced).Should().Be(MediaSyncStatus.Synced);
    }

    [Fact]
    public void MediaSyncStatus_Extensions_ShouldMapCorrectly()
    {
        MediaSyncStatus.Grabbed.ToNormalizedString().Should().Be("grabbed");
        MediaSyncStatus.Synced.ToNormalizedString().Should().Be("synced");

        MediaSyncStatus.Grabbed.IsGrabbed().Should().BeTrue();
        MediaSyncStatus.Grabbed.IsSynced().Should().BeFalse();
        MediaSyncStatus.Synced.IsSynced().Should().BeTrue();
        MediaSyncStatus.Synced.IsGrabbed().Should().BeFalse();
    }

    [Fact]
    public void MediaSyncStatus_JsonSerialization_ShouldSerializeAndDeserialize()
    {
        var status = MediaSyncStatus.Synced;
        var json = JsonSerializer.Serialize(status);
        json.Should().Be("\"synced\"");

        var deserializedGrabbed = JsonSerializer.Deserialize<MediaSyncStatus>("\"grabbed\"");
        deserializedGrabbed.Should().Be(MediaSyncStatus.Grabbed);

        var deserializedSynced = JsonSerializer.Deserialize<MediaSyncStatus>("\"synced\"");
        deserializedSynced.Should().Be(MediaSyncStatus.Synced);
    }

    [Fact]
    public void TrendingFeedModels_ShouldDeserializeSnakeCasePropertiesAndImplementInterface()
    {
        const string movieJson = """
            {
                "title": "Dune: Part Two",
                "name": null,
                "imdb_id": "tt15239678",
                "poster_url": "https://example.com/dune2.jpg"
            }
            """;

        var movie = JsonSerializer.Deserialize<TrendingMovieItem>(movieJson);
        movie.Should().NotBeNull();
        movie!.Title.Should().Be("Dune: Part Two");
        movie.PosterUrl.Should().Be("https://example.com/dune2.jpg");
        movie.EffectiveTitle.Should().Be("Dune: Part Two");
        movie.Category.Should().Be(MediaCategory.Movies);

        ITrendingItem movieItem = movie;
        movieItem.EffectiveTitle.Should().Be("Dune: Part Two");
        movieItem.Category.Should().Be(MediaCategory.Movies);

        const string tvJson = """
            {
                "title": null,
                "name": null,
                "show_name": "Severance",
                "imdb_id": "tt11280740",
                "poster_url": "https://example.com/severance.jpg"
            }
            """;

        var tv = JsonSerializer.Deserialize<TrendingTvItem>(tvJson);
        tv.Should().NotBeNull();
        tv!.ShowName.Should().Be("Severance");
        tv.ImdbId.Should().Be("tt11280740");
        tv.PosterUrl.Should().Be("https://example.com/severance.jpg");
        tv.EffectiveTitle.Should().Be("Severance");
        tv.Category.Should().Be(MediaCategory.Tv);

        ITrendingItem tvItem = tv;
        tvItem.EffectiveTitle.Should().Be("Severance");
        tvItem.Category.Should().Be(MediaCategory.Tv);
    }

    [Fact]
    public void QBitTorrentItem_ShouldDeserializeSnakeCaseAndProvideDomainHelperMethods()
    {
        const string torrentJson = """
            {
                "hash": "abcdef1234567890abcdef1234567890abcdef12",
                "name": "Dune.Part.Two.2024.1080p.mkv",
                "state": "pausedUP",
                "progress": 1.0,
                "size": 5000000000,
                "ratio": 1.5,
                "seeding_time": 3600,
                "category": "movies",
                "content_path": "/downloads/movies/Dune.Part.Two.2024.1080p.mkv",
                "save_path": "/downloads/movies"
            }
            """;

        var item = JsonSerializer.Deserialize<QBitTorrentItem>(torrentJson);
        item.Should().NotBeNull();
        item!.Hash.Should().Be("abcdef1234567890abcdef1234567890abcdef12");
        item.SeedingTime.Should().Be(3600);
        item.SeedingTimeSeconds.Should().Be(3600);
        item.ContentPath.Should().Be("/downloads/movies/Dune.Part.Two.2024.1080p.mkv");
        item.SavePath.Should().Be("/downloads/movies");
        item.Category.Should().Be("movies");
        item.MediaCategory.Should().Be(MediaCategory.Movies);
        item.TorrentState.Should().Be(QBitTorrentState.PausedUP);

        // Helper method validations
        item.IsCompleted.Should().BeTrue();
        item.IsSeeding.Should().BeTrue();
        item.IsPaused.Should().BeTrue();

#pragma warning disable CS0618
        item.Seeding_Time.Should().Be(3600);
        item.Content_Path.Should().Be("/downloads/movies/Dune.Part.Two.2024.1080p.mkv");
        item.Save_Path.Should().Be("/downloads/movies");
#pragma warning restore CS0618
    }

    [Fact]
    public void QBitTorrentItem_DownloadingState_ShouldReflectNotCompletedAndNotSeeding()
    {
        var downloading = new QBitTorrentItem(
            Hash: "123456",
            Name: "Sample",
            State: "downloading",
            Progress: 0.45,
            Size: 1000,
            Ratio: 0.0,
            SeedingTime: 0,
            Category: "tv"
        );

        downloading.IsCompleted.Should().BeFalse();
        downloading.IsSeeding.Should().BeFalse();
        downloading.IsPaused.Should().BeFalse();
        downloading.MediaCategory.Should().Be(MediaCategory.Tv);
        downloading.TorrentState.Should().Be(QBitTorrentState.Downloading);
    }

    [Fact]
    public void QBitTorrentFileItem_ShouldDeserializeSnakeCaseProperties()
    {
        const string fileJson = """
            {
                "index": 0,
                "name": "sample.mkv",
                "size": 1024,
                "progress": 1.0,
                "priority": 1,
                "is_seed": true
            }
            """;

        var fileItem = JsonSerializer.Deserialize<QBitTorrentFileItem>(fileJson);
        fileItem.Should().NotBeNull();
        fileItem!.Index.Should().Be(0);
        fileItem.Name.Should().Be("sample.mkv");
        fileItem.IsSeed.Should().BeTrue();

#pragma warning disable CS0618
        fileItem.Is_Seed.Should().BeTrue();
#pragma warning restore CS0618
    }
}
