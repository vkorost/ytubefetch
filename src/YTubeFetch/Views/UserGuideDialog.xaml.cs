using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using YTubeFetch.Services;

namespace YTubeFetch.Views;

public partial class UserGuideDialog : Window
{
    public UserGuideDialog()
    {
        InitializeComponent();
        ThemeService.ApplyDarkTitleBar(this);
        BuildGuideContent();
    }

    private void BuildGuideContent()
    {
        var doc = GuideDocument;
        var fg = new SolidColorBrush(ThemeService.TextPrimary);
        var fgSecondary = new SolidColorBrush(ThemeService.TextSecondary);
        var accent = new SolidColorBrush(ThemeService.AccentColor);

        doc.Foreground = fg;

        // Title
        AddHeading(doc, "YTubeFetch User Guide", 22, accent);
        AddParagraph(doc, "YTubeFetch is a Windows desktop application for downloading YouTube videos, audio, and subtitles. It uses yt-dlp under the hood and provides a simple drag-and-drop interface.");

        // Getting Started
        AddHeading(doc, "Getting Started", 18, accent);
        AddParagraph(doc, "There are several ways to start a download:");
        AddBullet(doc, "Paste a YouTube URL with Ctrl+V anywhere in the window");
        AddBullet(doc, "Drag and drop a YouTube URL from your browser onto the window");
        AddBullet(doc, "Type or paste a URL into the URL bar at the top of the middle panel and press Enter or click Fetch");
        AddBullet(doc, "Copy a YouTube URL to your clipboard \u2014 YTubeFetch detects it automatically and shows a notification (if clipboard monitoring is enabled)");
        AddParagraph(doc, "Before downloading, make sure at least one download type (Video, Audio, or Subtitles) is active. The active types are shown as highlighted panels in the middle of the window.");

        // Download Types
        AddHeading(doc, "Download Types", 18, accent);
        AddParagraph(doc, "The three toggle panels in the middle of the window control which content types are downloaded:");
        AddBulletBold(doc, "Video", "Downloads the video file in MP4 format. Quality can be configured in Preferences (up to best available, or limited to 1080p, 720p, etc.).");
        AddBulletBold(doc, "Audio", "Extracts and saves just the audio track. Supports MP3, AAC, OGG, OPUS, and WAV formats with configurable bitrate.");
        AddBulletBold(doc, "Subtitles", "Downloads subtitles as a plain text file with timestamps removed. The app automatically detects the video's language and tries the most relevant subtitle language first.");
        AddParagraph(doc, "Click a panel to toggle it on or off. When you submit a URL, downloads are created for all active types. You can also drag a URL directly onto an inactive panel to include that type in the download without toggling it permanently.");

        // URL Bar
        AddHeading(doc, "URL Bar", 18, accent);
        AddParagraph(doc, "The URL bar at the top of the middle panel accepts YouTube URLs. The Fetch button is disabled until a valid YouTube URL is entered. Only YouTube URLs are accepted \u2014 pasting other content is silently ignored to protect your privacy.");
        AddParagraph(doc, "Supported URL formats include regular videos (youtube.com/watch?v=...), short links (youtu.be/...), Shorts (/shorts/...), live streams (/live/...), embeds (/embed/...), clips (/clip/...), playlists, and channel URLs.");

        // Job List
        AddHeading(doc, "Job List", 18, accent);
        AddParagraph(doc, "The left panel shows all download jobs. Each job displays:");
        AddBullet(doc, "A small icon indicating the type (video, audio, or subtitles)");
        AddBullet(doc, "A thumbnail of the video (loaded automatically)");
        AddBullet(doc, "The video title");
        AddParagraph(doc, "Job colors indicate status: blue text means the job is currently downloading, red means an error occurred, and the default text color means the job is queued or completed. The thumbnail border is also highlighted in blue for the currently active download.");

        AddSubheading(doc, "Context Menu", 15, fg);
        AddParagraph(doc, "Right-click a job to access these options:");
        AddBulletBold(doc, "Open File", "Opens the downloaded file with your default application.");
        AddBulletBold(doc, "Open Containing Folder", "Opens the download folder in Explorer with the file selected.");
        AddBulletBold(doc, "Retry", "Re-queues the same download as a new job.");
        AddBulletBold(doc, "Remove from List", "Removes the job entry from the list (does not delete the file).");
        AddBulletBold(doc, "Clear List", "Removes all job entries from the list.");

        // Playlists and Channels
        AddHeading(doc, "Playlists and Channels", 18, accent);
        AddParagraph(doc, "YTubeFetch can download entire playlists and channels. If a playlist contains more than 50 items, you will be prompted to choose how many to download (10, 50, 100, or all). Each video is downloaded one at a time with a short pause between items to avoid rate limiting.");
        AddParagraph(doc, "Note: if a URL contains both a video ID (watch?v=) and a playlist parameter (list=), YTubeFetch treats it as a single video download, not a playlist.");

        // Stopping and Resuming
        AddHeading(doc, "Stopping and Resuming", 18, accent);
        AddParagraph(doc, "Use Actions \u2192 Stop Operation to cancel the current download and all queued jobs. Stopped jobs appear as errors in the job list.");
        AddParagraph(doc, "After stopping, Actions \u2192 Resume Operation becomes available. Clicking it re-queues all previously stopped jobs so they can be retried.");

        // Clipboard Monitoring
        AddHeading(doc, "Clipboard Monitoring", 18, accent);
        AddParagraph(doc, "When enabled (Preferences \u2192 General \u2192 Monitor clipboard), YTubeFetch watches your clipboard for YouTube URLs. When it detects one:");
        AddBullet(doc, "The URL is placed into the URL bar automatically");
        AddBullet(doc, "If the window is minimized to the system tray, a balloon notification appears");
        AddBullet(doc, "Clicking the notification restores the window and starts the download");
        AddParagraph(doc, "Non-YouTube clipboard content is completely ignored \u2014 YTubeFetch does not read or log clipboard content that is not a YouTube URL.");

        // System Tray
        AddHeading(doc, "System Tray", 18, accent);
        AddParagraph(doc, "YTubeFetch minimizes to the system tray instead of the taskbar. The tray icon provides:");
        AddBullet(doc, "Double-click to restore the window");
        AddBullet(doc, "Right-click for a menu with Show and Exit options");
        AddBullet(doc, "Balloon notifications for clipboard URL detection and download completion");

        // Preferences
        AddHeading(doc, "Preferences", 18, accent);
        AddParagraph(doc, "Open via File \u2192 Preferences. Settings are organized into four tabs:");

        AddSubheading(doc, "General", 15, fg);
        AddBulletBold(doc, "Download folders", "Set separate folders for video, audio, and subtitle downloads. Each type can go to a different location.");
        AddBulletBold(doc, "Prepend upload date", "When enabled, downloaded files are prefixed with the video's upload date (YYYY-MM-DD) for easy sorting.");
        AddBulletBold(doc, "Monitor clipboard", "Enables automatic detection of YouTube URLs copied to the clipboard.");
        AddBulletBold(doc, "Show log panel", "Toggles the live log panel on the right side of the window.");

        AddSubheading(doc, "Video", 15, fg);
        AddBulletBold(doc, "Quality", "Maximum resolution: Best Available, 1080p, 720p, 480p, or 360p.");
        AddBulletBold(doc, "Prefer original audio", "When enabled, avoids AI-dubbed audio tracks and uses the video's original language audio.");

        AddSubheading(doc, "Audio", 15, fg);
        AddBulletBold(doc, "Format", "Output format: MP3, AAC, OGG, OPUS, or WAV.");
        AddBulletBold(doc, "Quality", "Bitrate: Best (variable), 320, 256, 192, or 128 kbps.");
        AddBulletBold(doc, "Prefer original audio", "Same as the video setting \u2014 avoids AI-dubbed tracks.");

        AddSubheading(doc, "Subtitles", 15, fg);
        AddBulletBold(doc, "Preferred languages", "Ordered list of language codes (e.g., en, ru). Use the up/down buttons to change priority. The app tries each language in order.");
        AddBulletBold(doc, "Fallback to original", "If no subtitles are found in your preferred languages, try the video's original language.");
        AddBulletBold(doc, "Prefer manual subtitles", "Try human-written subtitles before auto-generated ones. Manual subtitles are usually higher quality.");

        // Updating yt-dlp
        AddHeading(doc, "Updating yt-dlp", 18, accent);
        AddParagraph(doc, "YTubeFetch relies on yt-dlp to communicate with YouTube. YouTube frequently changes its systems, which can cause downloads to fail. When this happens, use Actions \u2192 Update yt-dlp to download the latest version. This usually fixes compatibility issues.");
        AddParagraph(doc, "If downloads fail after updating yt-dlp, the issue is likely a temporary YouTube-side problem. Wait a few hours and try again.");

        // Log Panel
        AddHeading(doc, "Log Panel", 18, accent);
        AddParagraph(doc, "The right panel shows a live log of everything the application is doing. This is useful for diagnosing problems. You can:");
        AddBullet(doc, "Click Clear to empty the log display (the log file is not affected)");
        AddBullet(doc, "Hide the panel via Preferences \u2192 General \u2192 Show log panel");
        AddParagraph(doc, "A log file (ytubefetch.log) is also written next to the executable for troubleshooting.");

        // Keyboard Shortcuts
        AddHeading(doc, "Keyboard Shortcuts", 18, accent);
        AddBulletBold(doc, "Ctrl+V (anywhere)", "Paste a YouTube URL and immediately start downloading for all active types.");
        AddBulletBold(doc, "Ctrl+V (in URL bar)", "Paste a YouTube URL into the URL bar for review before fetching.");
        AddBulletBold(doc, "Enter (in URL bar)", "Start the download for the URL in the URL bar.");

        // Troubleshooting
        AddHeading(doc, "Troubleshooting", 18, accent);
        AddBulletBold(doc, "Downloads fail immediately", "Run Actions \u2192 Update yt-dlp. YouTube changes frequently and older yt-dlp versions may not work.");
        AddBulletBold(doc, "\"HTTP Error 429\"", "YouTube is rate-limiting you. The app retries automatically with increasing delays (10s, 30s, 60s). If it keeps failing, wait a while before trying again.");
        AddBulletBold(doc, "Missing dependencies error on startup", "yt-dlp and ffmpeg must be installed. Install yt-dlp with \"pip install yt-dlp\" and download ffmpeg from ffmpeg.org.");
        AddBulletBold(doc, "No subtitles found", "Not all videos have subtitles. Try enabling \"Fallback to original\" in Preferences \u2192 Subtitles.");
        AddBulletBold(doc, "File already exists", "YTubeFetch skips downloads if the output file already exists. Delete the existing file manually to re-download.");
    }

    private static void AddHeading(FlowDocument doc, string text, double size, Brush color)
    {
        var para = new Paragraph(new Run(text))
        {
            FontSize = size,
            FontWeight = FontWeights.SemiBold,
            Foreground = color,
            Margin = new Thickness(0, 16, 0, 6)
        };
        doc.Blocks.Add(para);
    }

    private static void AddSubheading(FlowDocument doc, string text, double size, Brush color)
    {
        var para = new Paragraph(new Run(text))
        {
            FontSize = size,
            FontWeight = FontWeights.SemiBold,
            Foreground = color,
            Margin = new Thickness(0, 10, 0, 4)
        };
        doc.Blocks.Add(para);
    }

    private static void AddParagraph(FlowDocument doc, string text)
    {
        var para = new Paragraph(new Run(text))
        {
            Margin = new Thickness(0, 0, 0, 8)
        };
        doc.Blocks.Add(para);
    }

    private static void AddBullet(FlowDocument doc, string text)
    {
        var para = new Paragraph(new Run("\u2022  " + text))
        {
            Margin = new Thickness(16, 0, 0, 4)
        };
        doc.Blocks.Add(para);
    }

    private static void AddBulletBold(FlowDocument doc, string label, string description)
    {
        var para = new Paragraph
        {
            Margin = new Thickness(16, 0, 0, 4)
        };
        para.Inlines.Add(new Run("\u2022  "));
        para.Inlines.Add(new Bold(new Run(label)));
        para.Inlines.Add(new Run(" \u2014 " + description));
        doc.Blocks.Add(para);
    }
}
