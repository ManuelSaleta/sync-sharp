using System.ComponentModel.DataAnnotations;

namespace Sync.Sharp.Models.Options;

/// <summary>
/// Local persistence and lock-file storage configuration.
/// </summary>
public sealed class StorageConfigOpts
{
    public const string SectionName = "Storage";

    /// <summary>
    /// File path for the exclusive process execution lock preventing concurrent sync runs.
    /// </summary>
    public string LockFile { get; set; } = string.Empty;

    /// <summary>
    /// Local file system path to the persistent JSON state database storing acquired release history.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string StateFilePath { get; set; } = string.Empty;
}
