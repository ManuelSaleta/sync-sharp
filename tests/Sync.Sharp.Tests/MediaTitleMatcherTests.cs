using Plex.Core.Http.Models;
using Sync.Sharp.Services;

namespace Sync.Sharp.Tests;

public class MediaTitleMatcherTests
{
    [Theory]
    [InlineData("Dune.Part.Two.2024.1080p.WEB-DL.DDP5.1.H.264-FLUX.mkv", "dune part two", 2024)]
    [InlineData("Severance S01E01 2160p ATVP WEB-DL", "severance", null)]
    [InlineData("The.Batman.2022.2160p.UHD.BluRay.x265", "batman", 2022)]
    [InlineData("2001.A.Space.Odyssey.1968.1080p.BluRay", "2001 a space odyssey", 1968)]
    public void ParseReleaseTitle_ShouldCleanSceneTagsAndExtractYear(string raw, string expectedTitle, int? expectedYear)
    {
        var (cleanTitle, year) = MediaTitleMatcher.ParseReleaseTitle(raw);

        cleanTitle.Should().Be(expectedTitle);
        year.Should().Be(expectedYear);
    }

    [Fact]
    public void IsMatch_ExactAndYearMatch_ShouldReturnTrue()
    {
        var item = new PlexMetadataItem
        {
            Title = "The Matrix",
            Year = 1999
        };

        var matched = MediaTitleMatcher.IsMatch(
            "The.Matrix.1999.1080p.BluRay.x264",
            "movies",
            item);

        matched.Should().BeTrue();
    }

    [Fact]
    public void IsMatch_MismatchedYearBeyondTolerance_ShouldReturnFalse()
    {
        var item = new PlexMetadataItem
        {
            Title = "Total Recall",
            Year = 1990
        };

        var matched = MediaTitleMatcher.IsMatch(
            "Total.Recall.2012.1080p.BluRay.x264",
            "movies",
            item);

        matched.Should().BeFalse();
    }

    [Fact]
    public void IsMatch_CompactTitleMatch_ShouldReturnTrue()
    {
        var item = new PlexMetadataItem
        {
            Title = "Spider-Man",
            Year = 2002
        };

        var matched = MediaTitleMatcher.IsMatch(
            "Spiderman 2002 1080p WEBRip",
            "movies",
            item);

        matched.Should().BeTrue();
    }
}
