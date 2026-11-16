using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Sync.Sharp.Models;
using Sync.Sharp.Models.Options;

namespace Sync.Sharp.Services;

/// <summary>
/// Filters and scores candidate Prowlarr releases against category rules.
/// </summary>
public interface IReleaseScorer
{
    ProwlarrRelease? SelectBestRelease(IReadOnlyList<ProwlarrRelease> releases, CategoryFilterConfigOpts filter, int minSeeders);
    int GetSeasonPackScore(string title, bool preferSeasonPacks);
}

public sealed partial class ReleaseScorer : IReleaseScorer
{
    private static readonly ConcurrentDictionary<string, Regex> RegexCache = new(StringComparer.Ordinal);
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    [GeneratedRegex(
        @"\b(S\d{1,2}\s*E\d{1,3}|\d{1,2}x\d{1,3}|E(pisode)?\s*\d{1,3})\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex SoloEpisodeRegex();

    [GeneratedRegex(
        @"\b(Season\s*\d+|Seasons|Complete|S\d{1,2}(?!\s*E\d+)|S\d{1,2}\s*-\s*S?\d{1,2})\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex SeasonPackRegex();

    public ProwlarrRelease? SelectBestRelease(
        IReadOnlyList<ProwlarrRelease> releases,
        CategoryFilterConfigOpts filter,
        int minSeeders)
    {
        var minBytes = filter.MinSizeMb * 1024L * 1024L;
        var maxBytes = filter.MaxSizeMb * 1024L * 1024L;

        var allowedRegexes = filter.AllowedPatterns.Select(GetOrCompileRegex).ToArray();
        var blockedRegexes = filter.BlockedPatterns.Select(GetOrCompileRegex).ToArray();

        return releases
            .Where(r => !string.IsNullOrWhiteSpace(r.GetBestDownloadUri()))
            .Where(r => (r.Seeders ?? 0) >= minSeeders)
            .Where(r => r.Size >= minBytes && r.Size <= maxBytes)
            .Where(r => allowedRegexes.Length == 0 || allowedRegexes.Any(rx => rx.IsMatch(r.Title)))
            .Where(r => !blockedRegexes.Any(rx => rx.IsMatch(r.Title)))
            .OrderByDescending(r => GetSeasonPackScore(r.Title, filter.PreferSeasonPacks))
            .ThenByDescending(r => r.Seeders ?? 0)
            .FirstOrDefault();
    }

    public int GetSeasonPackScore(string title, bool preferSeasonPacks)
    {
        if (!preferSeasonPacks) return 0;
        var isSolo = SoloEpisodeRegex().IsMatch(title);
        var isSeason = SeasonPackRegex().IsMatch(title) && !isSolo;
        if (isSeason) return 2;
        if (!isSolo) return 1;
        return 0;
    }

    private static Regex GetOrCompileRegex(string pattern) =>
        RegexCache.GetOrAdd(
            pattern,
            static p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout));
}
