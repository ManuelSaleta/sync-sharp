using System.Text.Json.Serialization;

namespace Sync.Sharp.Models;

/// <summary>
/// Strongly-typed lifecycle status of a media item in the synchronization history store.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<MediaSyncStatus>))]
public enum MediaSyncStatus
{
    [JsonStringEnumMemberName("grabbed")]
    Grabbed,
    [JsonStringEnumMemberName("synced")]
    Synced
}

/// <summary>
/// Extension methods for <see cref="MediaSyncStatus"/>.
/// </summary>
public static class MediaSyncStatusExtensions
{
    /// <summary>
    /// Returns the canonical normalized lowercase string ("grabbed" or "synced").
    /// </summary>
    public static string ToNormalizedString(this MediaSyncStatus status) =>
        status switch
        {
            MediaSyncStatus.Synced => "synced",
            _ => "grabbed"
        };

    public static bool IsGrabbed(this MediaSyncStatus status) => status == MediaSyncStatus.Grabbed;
    public static bool IsSynced(this MediaSyncStatus status) => status == MediaSyncStatus.Synced;
}
