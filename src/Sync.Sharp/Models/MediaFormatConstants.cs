namespace Sync.Sharp.Models;

/// <summary>
/// Centralized file extensions and container format constants for media validation and transfer.
/// </summary>
public static class MediaFormatConstants
{
    public static readonly string[] AllowedVideoExtensions =
    [
        ".mkv", ".mp4", ".mov", ".avi", ".webm"
    ];

    public static readonly string[] AllowedSubtitleExtensions =
    [
        ".srt"
    ];

    public static readonly string[] AllowedFfprobeFormats =
    [
        "matroska", "mp4", "mov", "avi", "webm"
    ];

    public static readonly string[] BlacklistedDangerousExtensions =
    [
        // Executables & script droppers
        ".exe", ".scr", ".bat", ".cmd", ".ps1", ".vbs", ".sh", ".jar",
        ".iso", ".dmg", ".lnk", ".pif", ".cpl", ".msi", ".dll", ".com",
        ".hta", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".msc", ".gadget",
        ".bin", ".app", ".apk", ".deb", ".rpm",
        // Compressed archives (prevent decompression bombs and nested trojans)
        ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".zst",
        ".cab", ".arj", ".lzh", ".ace", ".uue", ".bz", ".tgz"
    ];
}
