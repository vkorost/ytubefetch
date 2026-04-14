using System.IO;

namespace YTubeFetch.Models;

public class AppConfig
{
    // Download folders (separate per type)
    public string VideoDownloadFolder { get; set; } = string.Empty;
    public string AudioDownloadFolder { get; set; } = string.Empty;
    public string SubtitlesDownloadFolder { get; set; } = string.Empty;

    // Legacy single folder (used as fallback if new fields empty)
    public string DownloadFolder { get; set; } = string.Empty;

    // General settings
    public bool PrependUploadDate { get; set; } = true;
    public bool MonitorClipboard { get; set; } = true;
    public bool ShowLogPanel { get; set; } = true;

    // Video settings
    public string VideoQuality { get; set; } = "best";
    public bool PreferOriginalAudioVideo { get; set; } = true;

    // Audio settings
    public string AudioFormat { get; set; } = "mp3";
    public string AudioQuality { get; set; } = "0";
    public bool PreferOriginalAudioAudio { get; set; } = true;

    // Subtitle settings
    public List<string> SubtitleLanguages { get; set; } = new() { "ru", "en" };
    public bool SubtitleFallbackOriginal { get; set; } = true;
    public bool SubtitlePreferManual { get; set; } = true;

    // Toggle states
    public bool VideoEnabled { get; set; } = true;
    public bool AudioEnabled { get; set; } = false;
    public bool SubtitlesEnabled { get; set; } = false;

    // Window layout
    public double WindowWidth { get; set; } = 1050;
    public double WindowHeight { get; set; } = 620;
    public double WindowLeft { get; set; } = double.NaN;
    public double WindowTop { get; set; } = double.NaN;
    public double JobsColumnWidth { get; set; } = 250;
    public double MiddleColumnWidth { get; set; } = 300;
    public double LogColumnWidth { get; set; } = 0; // 0 = auto (star, fills remaining)
    public int WindowState { get; set; } = 0;

    /// <summary>
    /// Returns the effective download folder for a given type, falling back
    /// to legacy DownloadFolder, then ~/Downloads.
    /// </summary>
    public string GetDownloadFolder(DownloadType type)
    {
        var folder = type switch
        {
            DownloadType.Video => VideoDownloadFolder,
            DownloadType.Audio => AudioDownloadFolder,
            DownloadType.Subtitles => SubtitlesDownloadFolder,
            _ => ""
        };

        if (!string.IsNullOrWhiteSpace(folder)) return folder;
        if (!string.IsNullOrWhiteSpace(DownloadFolder)) return DownloadFolder;
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }
}
