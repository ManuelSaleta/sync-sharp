using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Sync.Sharp.Models;

namespace Sync.Sharp.Services;

public sealed record JarvisCandidateItem(
    string Title,
    int? Year = null,
    string? Category = null,
    [property: JsonPropertyName("imdbId")] string? ImdbId = null
);

public sealed record JarvisTriggerPayload(
    [property: JsonPropertyName("batch_id")] string? BatchId = null,
    [property: JsonPropertyName("requested_by")] string? RequestedBy = null,
    IReadOnlyList<JarvisCandidateItem>? Items = null
);

public interface IJarvisWebhookQueue
{
    int EnqueueBatch(JarvisTriggerPayload payload);
}

/// <summary>
/// HMAC-SHA256 signature verification matching jarvis-sharp's HmacSigner (<c>sha256=&lt;hex&gt;</c>).
/// </summary>
public static class HmacSignatureValidator
{
    public static bool VerifySignature(string secret, ReadOnlySpan<byte> payload, string? signatureHeader)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(signatureHeader))
        {
            return false;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        Span<byte> hash = stackalloc byte[32];
        hmac.TryComputeHash(payload, hash, out _);

        var expected = $"sha256={Convert.ToHexStringLower(hash)}";
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(signatureHeader.Trim());

        return expectedBytes.Length == actualBytes.Length &&
               CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }
}

/// <summary>
/// Discovery provider backed by an in-memory queue populated by signed <c>jarvis-sharp</c> webhook triggers.
/// </summary>
public sealed class JarvisWebhookDiscoveryProvider(
    IStateStore stateStore,
    ILogger<JarvisWebhookDiscoveryProvider> logger) : IMediaDiscoveryProvider, IJarvisWebhookQueue
{
    private readonly ConcurrentQueue<DiscoveryCandidate> _queue = new();

    public string ProviderName => "Jarvis";

    public int EnqueueBatch(JarvisTriggerPayload payload)
    {
        if (payload.Items is null || payload.Items.Count == 0)
        {
            return 0;
        }

        var count = 0;
        foreach (var item in payload.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Title))
            {
                continue;
            }

            var category = MediaCategoryParser.FromStringOrDefault(item.Category);
            var externalId = !string.IsNullOrWhiteSpace(item.ImdbId)
                ? $"jarvis:imdb:{item.ImdbId.Trim()}"
                : $"jarvis:{category.ToNormalizedString()}:{item.Title.Trim().ToLowerInvariant()}";

            var searchQuery = item.Year is > 0
                ? $"{item.Title.Trim()} {item.Year}"
                : item.Title.Trim();

            _queue.Enqueue(new DiscoveryCandidate(externalId, searchQuery, category));
            count++;
        }

        logger.LogInformation(
            "Enqueued {Count} approved candidate(s) from jarvis-sharp batch '{BatchId}' (requested by {User}).",
            count,
            payload.BatchId ?? "<none>",
            payload.RequestedBy ?? "<unknown>");

        return count;
    }

    public Task<IReadOnlyList<DiscoveryCandidate>> GetWantedCandidatesAsync(CancellationToken ct)
    {
        var candidates = new List<DiscoveryCandidate>();
        while (_queue.TryDequeue(out var candidate))
        {
            if (!stateStore.IsExternalIdProcessed(candidate.ExternalId))
            {
                candidates.Add(candidate);
            }
        }

        return Task.FromResult<IReadOnlyList<DiscoveryCandidate>>(candidates);
    }
}
