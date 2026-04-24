using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using YTubeFetch.Models;
using YTubeFetch.Services;

namespace YTubeFetch.Views;

public partial class PreferencesDialog : Window
{
    private readonly ObservableCollection<string> _languages = new();

    public AppConfig ResultConfig { get; private set; } = new();

    public PreferencesDialog(AppConfig config)
    {
        InitializeComponent();
        ThemeService.ApplyDarkTitleBar(this);

        // General
        var defaultFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

        VideoFolderBox.Text = !string.IsNullOrWhiteSpace(config.VideoDownloadFolder)
            ? config.VideoDownloadFolder
            : !string.IsNullOrWhiteSpace(config.DownloadFolder) ? config.DownloadFolder : defaultFolder;
        AudioFolderBox.Text = !string.IsNullOrWhiteSpace(config.AudioDownloadFolder)
            ? config.AudioDownloadFolder
            : !string.IsNullOrWhiteSpace(config.DownloadFolder) ? config.DownloadFolder : defaultFolder;
        SubsFolderBox.Text = !string.IsNullOrWhiteSpace(config.SubtitlesDownloadFolder)
            ? config.SubtitlesDownloadFolder
            : !string.IsNullOrWhiteSpace(config.DownloadFolder) ? config.DownloadFolder : defaultFolder;

        PrependDateCheck.IsChecked = config.PrependUploadDate;
        MonitorClipboardCheck.IsChecked = config.MonitorClipboard;
        ShowLogPanelCheck.IsChecked = config.ShowLogPanel;

        // Video
        SelectComboByTag(VideoQualityCombo, config.VideoQuality);
        VideoOriginalAudioCheck.IsChecked = config.PreferOriginalAudioVideo;

        // Audio
        SelectComboByTag(AudioFormatCombo, config.AudioFormat);
        SelectComboByTag(AudioQualityCombo, config.AudioQuality);
        AudioOriginalAudioCheck.IsChecked = config.PreferOriginalAudioAudio;

        // Subtitles
        foreach (var lang in config.SubtitleLanguages)
            _languages.Add(lang);
        LangList.ItemsSource = _languages;
        SubObsidianCheck.IsChecked = config.SubtitleObsidianFormat;
        SubFallbackCheck.IsChecked = config.SubtitleFallbackOriginal;
        SubPreferManualCheck.IsChecked = config.SubtitlePreferManual;

        // Copy layout/toggle fields so they survive the round-trip
        ResultConfig = new AppConfig
        {
            WindowWidth = config.WindowWidth,
            WindowHeight = config.WindowHeight,
            WindowLeft = config.WindowLeft,
            WindowTop = config.WindowTop,
            WindowState = config.WindowState,
            JobsColumnWidth = config.JobsColumnWidth,
            MiddleColumnWidth = config.MiddleColumnWidth,
            LogColumnWidth = config.LogColumnWidth,
            ShowLogPanel = config.ShowLogPanel,
            VideoEnabled = config.VideoEnabled,
            AudioEnabled = config.AudioEnabled,
            SubtitlesEnabled = config.SubtitlesEnabled,
            DownloadFolder = config.DownloadFolder,
        };
    }

    // --- Folder browsers ---

    private void BrowseVideoFolder_Click(object sender, RoutedEventArgs e)
        => BrowseFolder(VideoFolderBox);

    private void BrowseAudioFolder_Click(object sender, RoutedEventArgs e)
        => BrowseFolder(AudioFolderBox);

    private void BrowseSubsFolder_Click(object sender, RoutedEventArgs e)
        => BrowseFolder(SubsFolderBox);

    private void BrowseFolder(TextBox target)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Download Folder",
            InitialDirectory = target.Text
        };
        if (dialog.ShowDialog(this) == true)
            target.Text = dialog.FolderName;
    }

    // --- Language list ---

    private void LangUp_Click(object sender, RoutedEventArgs e)
    {
        int idx = LangList.SelectedIndex;
        if (idx > 0)
        {
            _languages.Move(idx, idx - 1);
            LangList.SelectedIndex = idx - 1;
        }
    }

    private void LangDown_Click(object sender, RoutedEventArgs e)
    {
        int idx = LangList.SelectedIndex;
        if (idx >= 0 && idx < _languages.Count - 1)
        {
            _languages.Move(idx, idx + 1);
            LangList.SelectedIndex = idx + 1;
        }
    }

    private void LangRemove_Click(object sender, RoutedEventArgs e)
    {
        int idx = LangList.SelectedIndex;
        if (idx >= 0)
        {
            _languages.RemoveAt(idx);
            if (_languages.Count > 0)
                LangList.SelectedIndex = Math.Min(idx, _languages.Count - 1);
        }
    }

    private void LangAdd_Click(object sender, RoutedEventArgs e)
    {
        var code = LangAddBox.Text.Trim().ToLower();
        if (!string.IsNullOrWhiteSpace(code) && !_languages.Contains(code))
        {
            _languages.Add(code);
            LangAddBox.Clear();
            LangList.SelectedIndex = _languages.Count - 1;
        }
    }

    // --- OK ---

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        ResultConfig.VideoDownloadFolder = VideoFolderBox.Text;
        ResultConfig.AudioDownloadFolder = AudioFolderBox.Text;
        ResultConfig.SubtitlesDownloadFolder = SubsFolderBox.Text;
        ResultConfig.PrependUploadDate = PrependDateCheck.IsChecked == true;
        ResultConfig.MonitorClipboard = MonitorClipboardCheck.IsChecked == true;
        ResultConfig.ShowLogPanel = ShowLogPanelCheck.IsChecked == true;

        ResultConfig.VideoQuality = GetComboTag(VideoQualityCombo) ?? "best";
        ResultConfig.PreferOriginalAudioVideo = VideoOriginalAudioCheck.IsChecked == true;

        ResultConfig.AudioFormat = GetComboTag(AudioFormatCombo) ?? "mp3";
        ResultConfig.AudioQuality = GetComboTag(AudioQualityCombo) ?? "0";
        ResultConfig.PreferOriginalAudioAudio = AudioOriginalAudioCheck.IsChecked == true;

        ResultConfig.SubtitleLanguages = new List<string>(_languages);
        ResultConfig.SubtitleObsidianFormat = SubObsidianCheck.IsChecked == true;
        ResultConfig.SubtitleFallbackOriginal = SubFallbackCheck.IsChecked == true;
        ResultConfig.SubtitlePreferManual = SubPreferManualCheck.IsChecked == true;

        DialogResult = true;
        Close();
    }

    // --- Helpers ---

    private static void SelectComboByTag(ComboBox combo, string tag)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if (item.Tag?.ToString() == tag)
            {
                combo.SelectedItem = item;
                return;
            }
        }
        if (combo.Items.Count > 0)
            combo.SelectedIndex = 0;
    }

    private static string? GetComboTag(ComboBox combo)
    {
        return (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
    }
}
