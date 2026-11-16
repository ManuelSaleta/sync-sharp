using System.Text.Json;
using System.Text.Json.Serialization;
using Sync.Sharp.Models;

namespace Sync.Sharp.Tests;

public class SerializationTests
{
    private readonly JsonSerializerOptions _configuredOpts =
    new(JsonSerializerDefaults.Web)
    {
        // Converts PascalCase property names to snake_case
        // (e.g. PosterUrl -> poster_url, SeedingTime ->  seeding_time)
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,

        // Converts dictionary keys to snake_case as well
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,

        // Allows deserialization to match properties
        // regardless of casing
        PropertyNameCaseInsensitive = true,

        // Optional: serializes enums as snake_case strings
        // instead of numbers
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    private sealed record SnakeCaseSample(string FirstName, int TotalCount);

    [Fact]
    public void Serializes_Pascal_To_SnakeCase()
    {
        var sample = new SnakeCaseSample("Dune", 42);
        var json = JsonSerializer.Serialize(sample, _configuredOpts);

        json.Should().Contain("\"first_name\":\"Dune\"");
        json.Should().Contain("\"total_count\":42");
    }

    [Fact]
    public void Deserializes_SnakeCase_To_PascalPoco()
    {
        var original = BogusFactory.Create<TrendingMovieItem>();
        var json = JsonSerializer.Serialize(original, _configuredOpts);
        var reference = JsonSerializer.Deserialize<TrendingMovieItem>(json, _configuredOpts);

        reference.Should().NotBeNull();
        reference!.Name.Should().Be(original.Name);
    }

    [Fact]
    public void ProwlarrRelease_Deserializes_CamelCase_Under_SnakeCase_NamingPolicy()
    {
        const string json = """
        {
            "guid": "guid-1",
            "title": "Sample.Movie.2026",
            "size": 1000,
            "indexerId": 42,
            "indexer": "TestIndexer",
            "infoHash": "abcdef123456",
            "magnetUrl": "magnet:?xt=urn:btih:abcdef123456",
            "downloadUrl": "https://example.com/torrent",
            "seeders": 10,
            "leechers": 2,
            "publishDate": "2026-01-01T00:00:00Z"
        }
        """;

        var release = JsonSerializer.Deserialize<ProwlarrRelease>(json, _configuredOpts);
        release.Should().NotBeNull();
        release!.IndexerId.Should().Be(42);
        release.InfoHash.Should().Be("abcdef123456");
        release.MagnetUrl.Should().Be("magnet:?xt=urn:btih:abcdef123456");
        release.DownloadUrl.Should().Be("https://example.com/torrent");
        release.GetBestDownloadUri().Should().Be("magnet:?xt=urn:btih:abcdef123456");
    }
}
