namespace Sync.Sharp.Models;

public sealed record MediaHistoryEntry(
    string? ExternalId,
    string InfoHash,
    string Title,
    MediaCategory Category,
    MediaSyncStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SyncedAt
);

public sealed record SyncStateDocument(
    List<MediaHistoryEntry> Items
);
