using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sync.Sharp.Models;

/// <summary>
/// Strongly-typed lifecycle status of a media item in the synchronization history store.
/// </summary>
[JsonConverter(typeof(MediaSyncStatusJsonConverter))]
public enum MediaSyncStatus
{
    Grabbed,
    Synced
}

/// <summary>
/// Parsing and normalization routines for <see cref="MediaSyncStatus"/>.
/// </summary>
public static class MediaSyncStatusParser
{
    /// <summary>
    /// Parses a raw status string into a <see cref="MediaSyncStatus"/>. Throws <see cref="ArgumentException"/> if invalid.
    /// </summary>
    public static MediaSyncStatus Parse(string? value)
    {
        if (TryParse(value, out var status))
        {
            return status;
        }

        throw new ArgumentException($"Invalid or unsupported media sync status: '{value}'", nameof(value));
    }

    /// <summary>
    /// Attempts to parse a status string into <see cref="MediaSyncStatus"/>.
    /// Handles "grabbed" and "synced" (case-insensitive).
    /// </summary>
    public static bool TryParse(string? value, out MediaSyncStatus status)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            Enum.TryParse<MediaSyncStatus>(value.Trim(), ignoreCase: true, out status))
        {
            return true;
        }

        status = MediaSyncStatus.Grabbed;
        return false;
    }

    /// <summary>
    /// Parses a status string, returning <paramref name="defaultStatus"/> if parsing fails.
    /// </summary>
    public static MediaSyncStatus FromStringOrDefault(string? value, MediaSyncStatus defaultStatus = MediaSyncStatus.Grabbed) =>
        TryParse(value, out var status) ? status : defaultStatus;

    /// <summary>
    /// Normalizes a status string into standard canonical lowercase ("grabbed" or "synced").
    /// </summary>
    public static string Normalize(string? value) =>
        FromStringOrDefault(value).ToNormalizedString();
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

/// <summary>
/// JSON converter for <see cref="MediaSyncStatus"/> supporting case-insensitive reading and canonical lowercase output.
/// </summary>
public sealed class MediaSyncStatusJsonConverter : JsonConverter<MediaSyncStatus>
{
    public override MediaSyncStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var str = reader.GetString();
            if (MediaSyncStatusParser.TryParse(str, out var parsed))
            {
                return parsed;
            }
        }
        else if (reader.TokenType == JsonTokenType.Number)
        {
            var intVal = reader.GetInt32();
            if (Enum.IsDefined(typeof(MediaSyncStatus), intVal))
            {
                return (MediaSyncStatus)intVal;
            }
        }

        return MediaSyncStatus.Grabbed;
    }

    public override void Write(Utf8JsonWriter writer, MediaSyncStatus value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToNormalizedString());
    }
}
