using System.ComponentModel.DataAnnotations;

namespace Sync.Sharp.Models.Options;

/// <summary>
/// Configuration for pluggable media discovery providers (Radarr, Sonarr, Jarvis, Trending).
/// </summary>
public sealed class DiscoveryConfigOpts
{
    public const string SectionName = "Discovery";

    /// <summary>
    /// Active discovery provider name: "Radarr" (default), "Sonarr", "Jarvis", or legacy "Trending".
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string Provider { get; set; } = "Radarr";

    public RadarrConfigOpts Radarr { get; set; } = new();
    public SonarrConfigOpts Sonarr { get; set; } = new();
    public JarvisConfigOpts Jarvis { get; set; } = new();
}

public sealed class RadarrConfigOpts
{
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public bool SyncOnImport { get; set; } = true;
    public string MinimumAvailability { get; set; } = "released";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl) &&
        !string.IsNullOrWhiteSpace(ApiKey);
}

public sealed class SonarrConfigOpts
{
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public bool SyncOnImport { get; set; } = true;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl) &&
        !string.IsNullOrWhiteSpace(ApiKey);
}

public sealed class JarvisConfigOpts
{
    [Range(0, 65535)]
    public int WebhookPort { get; set; }

    public string? WebhookSecret { get; set; }
}
