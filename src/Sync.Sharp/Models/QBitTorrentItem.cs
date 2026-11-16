using System.Text.Json.Serialization;

namespace Sync.Sharp.Models;

/// <summary>
/// Strongly-typed enumeration of known qBittorrent torrent states.
/// </summary>
public enum QBitTorrentState
{
    Unknown,
    Allocating,
    Downloading,
    MetaDL,
    ForcedDL,
    PausedDL,
    QueuedDL,
    CheckingDL,
    StalledDL,
    Uploading,
    PausedUP,
    QueuedUP,
    StalledUP,
    CheckingUP,
    ForcedUP,
    CheckingResumeData,
    Moving,
    MissingFiles,
    Error,
    StoppedDL,
    StoppedUP
}

public sealed record QBitTorrentItem(
    string Hash,
    string Name,
    string State,
    double Progress,
    long Size,
    double Ratio,
    [property: JsonPropertyName("seeding_time")] long SeedingTime,
    string? Category = null,
    [property: JsonPropertyName("content_path")] string? ContentPath = null,
    [property: JsonPropertyName("save_path")] string? SavePath = null
)
{
    [JsonIgnore]
    public long SeedingTimeSeconds => SeedingTime;

    /// <summary>
    /// Gets whether the torrent has completed downloading (progress >= 1.0 or state indicates completed/seeding).
    /// </summary>
    [JsonIgnore]
    public bool IsCompleted => Progress >= 1.0 || IsSeeding;

    /// <summary>
    /// Gets whether the torrent is currently in seeding/upload mode.
    /// </summary>
    [JsonIgnore]
    public bool IsSeeding => TorrentState is
        QBitTorrentState.Uploading or
        QBitTorrentState.StalledUP or
        QBitTorrentState.ForcedUP or
        QBitTorrentState.QueuedUP or
        QBitTorrentState.CheckingUP or
        QBitTorrentState.PausedUP or
        QBitTorrentState.StoppedUP;

    /// <summary>
    /// Gets whether the torrent is currently paused or stopped.
    /// </summary>
    [JsonIgnore]
    public bool IsPaused => TorrentState is
        QBitTorrentState.PausedDL or
        QBitTorrentState.StoppedDL or
        QBitTorrentState.PausedUP or
        QBitTorrentState.StoppedUP;

    /// <summary>
    /// Strongly-typed <see cref="MediaCategory"/> parsed from <see cref="Category"/>.
    /// </summary>
    [JsonIgnore]
    public MediaCategory MediaCategory => MediaCategoryParser.FromStringOrDefault(Category);

    /// <summary>
    /// Strongly-typed <see cref="QBitTorrentState"/> parsed from <see cref="State"/>.
    /// </summary>
    [JsonIgnore]
    public QBitTorrentState TorrentState =>
        !string.IsNullOrWhiteSpace(State) &&
        Enum.TryParse<QBitTorrentState>(State.Trim(), ignoreCase: true, out var parsed) &&
        Enum.IsDefined(parsed)
            ? parsed
            : QBitTorrentState.Unknown;
}

public sealed record QBitTorrentFileItem(
    int Index,
    string Name,
    long Size,
    double Progress,
    int Priority,
    [property: JsonPropertyName("is_seed")] bool? IsSeed = null
);
