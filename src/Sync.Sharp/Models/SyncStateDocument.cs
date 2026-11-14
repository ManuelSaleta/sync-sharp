using System.Text.Json.Serialization;

namespace Sync.Sharp.Models;

public sealed record MediaHistoryEntry
{
    [JsonConstructor]
    public MediaHistoryEntry(
        string? ExternalId,
        string InfoHash,
        string Title,
        MediaCategory Category,
        MediaSyncStatus Status,
        DateTimeOffset CreatedAt,
        DateTimeOffset? SyncedAt)
    {
        this.ExternalId = ExternalId;
        this.InfoHash = InfoHash;
        this.Title = Title;
        this.Category = Category;
        this.Status = Status;
        this.CreatedAt = CreatedAt;
        this.SyncedAt = SyncedAt;
    }

    public MediaHistoryEntry(
        string? ExternalId,
        string InfoHash,
        string Title,
        string Category,
        string Status,
        DateTimeOffset CreatedAt,
        DateTimeOffset? SyncedAt)
        : this(
            ExternalId,
            InfoHash,
            Title,
            MediaCategoryParser.FromStringOrDefault(Category),
            MediaSyncStatusParser.FromStringOrDefault(Status),
            CreatedAt,
            SyncedAt)
    {
    }

    public string? ExternalId { get; init; }
    public string InfoHash { get; init; }
    public string Title { get; init; }
    public MediaCategory Category { get; init; }
    public MediaSyncStatus Status { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? SyncedAt { get; init; }

    public void Deconstruct(
        out string? externalId,
        out string infoHash,
        out string title,
        out MediaCategory category,
        out MediaSyncStatus status,
        out DateTimeOffset createdAt,
        out DateTimeOffset? syncedAt)
    {
        externalId = ExternalId;
        infoHash = InfoHash;
        title = Title;
        category = Category;
        status = Status;
        createdAt = CreatedAt;
        syncedAt = SyncedAt;
    }
}

public sealed record SyncStateDocument(
    List<MediaHistoryEntry> Items
);
