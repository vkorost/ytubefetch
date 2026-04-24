using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using YTubeFetch.Models;
using YTubeFetch.Services;
using YTubeFetch.Views;
using WinForms = System.Windows.Forms;

namespace YTubeFetch;

public partial class MainWindow : Window
{
    private readonly ConfigService _configService = new();
    private readonly DownloadService _downloadService = new();
    private readonly ClipboardMonitor _clipboardMonitor = new();
    private AppConfig _config = null!;
    private readonly ObservableCollection<JobViewModel> _jobs = new();

    // Exposed for MCP server startup integration in App.xaml.cs
#if ENABLE_MCP
    internal DownloadService DownloadService => _downloadService;
#endif

    // Toggle states
    private bool _videoEnabled = true;
    private bool _audioEnabled;
    private bool _subsEnabled;

    // System tray
    private WinForms.NotifyIcon? _trayIcon;
    private string? _pendingClipboardUrl;

    public MainWindow()
    {
        LogService.Log("MainWindow constructor start");
        try
        {
            InitializeComponent();
            JobList.ItemsSource = _jobs;

            // Override ALL paste paths on the URL bar (Ctrl+V, Shift+Insert, context menu, etc.)
            UrlBar.CommandBindings.Add(new CommandBinding(
                ApplicationCommands.Paste,
                (_, _) => PasteYouTubeUrlToBar()));

            LogViewer.Text = LogService.GetLogContent();
            LogViewer.CaretIndex = LogViewer.Text.Length;
            LogViewer.ScrollToEnd();
            LogService.LogWritten += OnLogWritten;

            LogService.Log("MainWindow constructor completed");
        }
        catch (Exception ex)
        {
            LogService.Error("MainWindow constructor failed", ex);
            throw;
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        LogService.Log("Window_Loaded start");
        try
        {
            var (ytdlpPath, ffmpegPath) = DependencyManager.Initialize();

            if (ytdlpPath == null || ffmpegPath == null)
            {
                var missing = new List<string>();
                if (ytdlpPath == null) missing.Add("yt-dlp");
                if (ffmpegPath == null) missing.Add("ffmpeg");

                LogService.Log($"Missing dependencies: {string.Join(", ", missing)}");
                MessageBox.Show(this,
                    $"The following required tools could not be found:\n\n" +
                    $"  {string.Join(", ", missing)}\n\n" +
                    $"Install yt-dlp: pip install yt-dlp\n" +
                    $"  or download from https://github.com/yt-dlp/yt-dlp/releases\n\n" +
                    $"Install ffmpeg: https://ffmpeg.org/download.html\n\n" +
                    $"Both must be installed on your system.",
                    "Missing Dependencies", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
                return;
            }

            _downloadService.YtDlpPath = ytdlpPath;
            _downloadService.FfmpegPath = ffmpegPath;

            // Load config
            _config = _configService.Load();
            _downloadService.Config = _config;
            LogService.Log($"Config loaded");

            // Set batch size prompt callback
            _downloadService.BatchSizePrompt = PromptBatchSizeAsync;

            // Restore layout and toggle states
            ApplyLayout(_config);
            _videoEnabled = _config.VideoEnabled;
            _audioEnabled = _config.AudioEnabled;
            _subsEnabled = _config.SubtitlesEnabled;
            UpdateAllToggleVisuals();

            // Wire up download service events
            _downloadService.JobStarted += OnJobStarted;
            _downloadService.JobCompleted += OnJobCompleted;
            _downloadService.JobError += OnJobError;
            _downloadService.JobUpdated += OnJobUpdated;
            _downloadService.ProgressChanged += OnProgressChanged;
            _downloadService.StatusChanged += OnStatusChanged;
            _downloadService.DuplicateSkipped += OnDuplicateSkipped;
            _downloadService.AllCompleted += OnAllCompleted;

            // System tray
            InitializeTrayIcon();

            // Clipboard monitor
            _clipboardMonitor.IsEnabled = _config.MonitorClipboard;
            _clipboardMonitor.UrlDetected += OnClipboardUrlDetected;
            _clipboardMonitor.Start();

            LogService.Log("Window_Loaded completed successfully");
        }
        catch (Exception ex)
        {
            LogService.Error("Window_Loaded failed", ex);
            throw;
        }
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        LogService.Log("Window closing - auto-saving layout");
        LogService.LogWritten -= OnLogWritten;

        // Stop clipboard monitor
        _clipboardMonitor.Stop();

        // Dispose tray icon
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        if (_config != null)
        {
            CaptureLayout(_config);
            _config.VideoEnabled = _videoEnabled;
            _config.AudioEnabled = _audioEnabled;
            _config.SubtitlesEnabled = _subsEnabled;
            _configService.Save(_config);
        }
        _downloadService.StopAll();
        LogService.Log("Window closed, download service stopped");
    }

    // --- System tray ---

    private void InitializeTrayIcon()
    {
        try
        {
            _trayIcon = new WinForms.NotifyIcon();

            var iconUri = new Uri("pack://application:,,,/Resources/YTubeFetch.ico");
            var streamInfo = System.Windows.Application.GetResourceStream(iconUri);
            if (streamInfo != null)
                _trayIcon.Icon = new System.Drawing.Icon(streamInfo.Stream);

            _trayIcon.Text = "YTubeFetch";
            _trayIcon.Visible = true;

            var trayMenu = new WinForms.ContextMenuStrip();
            trayMenu.Items.Add("Show", null, (_, _) => ShowFromTray());
            trayMenu.Items.Add("-");
            trayMenu.Items.Add("Exit", null, (_, _) => ExitFromTray());
            _trayIcon.ContextMenuStrip = trayMenu;

            _trayIcon.DoubleClick += (_, _) => ShowFromTray();
            _trayIcon.BalloonTipClicked += OnBalloonTipClicked;

            LogService.Log("System tray icon initialized");
        }
        catch (Exception ex)
        {
            LogService.Error("Failed to initialize tray icon", ex);
        }
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized)
        {
            Hide();
            LogService.Log("Window minimized to tray");
        }
    }

    private void ShowFromTray()
    {
        Dispatcher.Invoke(() =>
        {
            // Capture valid URL before restore (clipboard monitor may have set it while hidden)
            string? preservedUrl = null;
            if (DownloadService.IsValidYouTubeUrl(UrlBar.Text.Trim()))
                preservedUrl = UrlBar.Text;

            Show();
            WindowState = WindowState.Normal;
            Activate();

            // After activation, WPF may deliver stale paste/input to the focused TextBox.
            // Restore only the valid URL (or clear if there wasn't one).
            if (preservedUrl != null)
                UrlBar.Text = preservedUrl;
            else if (!DownloadService.IsValidYouTubeUrl(UrlBar.Text.Trim()))
                UrlBar.Clear();

            LogService.Log("Window restored from tray");
        });
    }

    private void ExitFromTray()
    {
        Dispatcher.Invoke(() =>
        {
            LogService.Log("Exit from tray");
            Close();
        });
    }

    private void OnBalloonTipClicked(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (_pendingClipboardUrl != null)
            {
                ShowFromTray();
                EnqueueDownloads(_pendingClipboardUrl);
                _pendingClipboardUrl = null;
            }
            else
            {
                ShowFromTray();
            }
        });
    }

    // --- Clipboard monitoring ---

    private void OnClipboardUrlDetected(string url)
    {
        // Always populate the URL bar (works even if window is hidden — will be visible when restored)
        Dispatcher.Invoke(() =>
        {
            if (UrlBar.Text != url)
            {
                UrlBar.Text = url;
                LogService.Log($"Clipboard URL populated into URL bar: {url}");
            }
        });

        // Show balloon notification only if window is not visible (minimized to tray)
        if (!IsVisible || WindowState == WindowState.Minimized)
        {
            _pendingClipboardUrl = url;
            _trayIcon?.ShowBalloonTip(5000, "YTubeFetch",
                "YouTube URL detected. Click to download.",
                WinForms.ToolTipIcon.Info);
        }
    }

    // --- Toggle panels ---

    private void VideoToggle_Click(object sender, MouseButtonEventArgs e)
    {
        _videoEnabled = !_videoEnabled;
        UpdateToggleVisual(VideoBorder, _videoEnabled);
        UpdateToggleTooltips();
        LogService.Log($"Video toggle: {_videoEnabled}");
    }

    private void AudioToggle_Click(object sender, MouseButtonEventArgs e)
    {
        _audioEnabled = !_audioEnabled;
        UpdateToggleVisual(AudioBorder, _audioEnabled);
        UpdateToggleTooltips();
        LogService.Log($"Audio toggle: {_audioEnabled}");
    }

    private void SubsToggle_Click(object sender, MouseButtonEventArgs e)
    {
        _subsEnabled = !_subsEnabled;
        UpdateToggleVisual(SubsBorder, _subsEnabled);
        UpdateToggleTooltips();
        LogService.Log($"Subtitles toggle: {_subsEnabled}");
    }

    private void UpdateToggleVisual(Border border, bool enabled)
    {
        if (enabled)
        {
            border.BorderBrush = new SolidColorBrush(ThemeService.PanelHighlight);
            border.Background = new SolidColorBrush(ThemeService.DropHighlight);
        }
        else
        {
            border.BorderBrush = new SolidColorBrush(ThemeService.PanelBorder);
            border.Background = new SolidColorBrush(ThemeService.PanelBackground);
        }
    }

    private void UpdateAllToggleVisuals()
    {
        UpdateToggleVisual(VideoBorder, _videoEnabled);
        UpdateToggleVisual(AudioBorder, _audioEnabled);
        UpdateToggleVisual(SubsBorder, _subsEnabled);
        UpdateToggleTooltips();
    }

    private void UpdateToggleTooltips()
    {
        VideoBorder.ToolTip = GetToggleTooltip("Video", _videoEnabled);
        AudioBorder.ToolTip = GetToggleTooltip("Audio", _audioEnabled);
        SubsBorder.ToolTip = GetToggleTooltip("Subtitles", _subsEnabled);
    }

    private string GetToggleTooltip(string type, bool enabled)
    {
        var others = new List<string>();
        if (type != "Video" && _videoEnabled) others.Add("Video");
        if (type != "Audio" && _audioEnabled) others.Add("Audio");
        if (type != "Subtitles" && _subsEnabled) others.Add("Subtitles");

        if (enabled)
        {
            if (others.Count > 0)
                return $"{type} is ON. Drop or paste a URL anywhere to download {type} + {string.Join(" + ", others)}.\nClick to turn off.";
            else
                return $"{type} is ON. Drop or paste a URL anywhere to download {type}.\nClick to turn off.";
        }
        else
        {
            if (others.Count > 0)
                return $"{type} is OFF. Drop a URL here to download {type} + {string.Join(" + ", others)}.\nClick to turn on.";
            else
                return $"{type} is OFF. Drop a URL here to download only {type}.\nClick to turn on.";
        }
    }

    // --- URL bar ---

    private void UrlBar_TextChanged(object sender, TextChangedEventArgs e)
    {
        FetchButton.IsEnabled = DownloadService.IsValidYouTubeUrl(UrlBar.Text.Trim());
    }

    private void UrlBar_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            FetchFromUrlBar();
            e.Handled = true;
        }
    }

    private void UrlBar_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Intercept all paste shortcuts — only allow YouTube URLs
        if ((e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control) ||
            (e.Key == Key.Insert && Keyboard.Modifiers == ModifierKeys.Shift))
        {
            e.Handled = true;
            PasteYouTubeUrlToBar();
        }
    }

    private void UrlBar_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        // Intercept right-click Paste and any other paste path — only allow YouTube URLs
        e.CancelCommand();
        PasteYouTubeUrlToBar();
    }

    private void PasteYouTubeUrlToBar()
    {
        if (!Clipboard.ContainsText()) return;
        var text = Clipboard.GetText().Trim();
        if (DownloadService.IsValidYouTubeUrl(text))
        {
            UrlBar.Text = text;
            UrlBar.CaretIndex = text.Length;
        }
        // Silently ignore non-YouTube content — no logging to avoid privacy concerns
    }

    private void FetchButton_Click(object sender, RoutedEventArgs e)
    {
        FetchFromUrlBar();
    }

    private void FetchFromUrlBar()
    {
        var url = UrlBar.Text.Trim();
        if (!string.IsNullOrWhiteSpace(url))
        {
            LogService.Log($"URL bar fetch: \"{url}\"");
            EnqueueDownloads(url);
            UrlBar.Clear();
        }
    }

    // --- Drop area ---

    private void DropArea_DragEnter(object sender, DragEventArgs e)
    {
        if (HasUrl(e))
        {
            DropArea.Background = new SolidColorBrush(ThemeService.DropHighlight);
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void DropArea_DragLeave(object sender, DragEventArgs e)
    {
        DropArea.Background = Brushes.Transparent;
        e.Handled = true;
    }

    private void DropArea_Drop(object sender, DragEventArgs e)
    {
        DropArea.Background = Brushes.Transparent;
        // Skip if already handled by a toggle panel drop
        if (e.Handled) return;
        var url = ExtractUrl(e);
        if (!string.IsNullOrWhiteSpace(url))
        {
            LogService.Log($"DROP: \"{url}\"");
            EnqueueDownloads(url);
        }
        else
        {
            LogService.Log("DROP: no URL extracted from drag data");
        }
        e.Handled = true;
    }

    private void TogglePanel_Drop(object sender, DragEventArgs e)
    {
        DropArea.Background = Brushes.Transparent;
        var url = ExtractUrl(e);
        if (string.IsNullOrWhiteSpace(url))
        {
            e.Handled = true;
            return;
        }

        // Determine which panel received the drop
        var border = sender as Border;
        var tag = border?.Tag?.ToString();
        var dropType = tag switch
        {
            "Video" => DownloadType.Video,
            "Audio" => DownloadType.Audio,
            "Subtitles" => DownloadType.Subtitles,
            _ => (DownloadType?)null
        };

        // Collect types to download: the drop target + any active toggles
        var types = new List<DownloadType>();
        if (dropType.HasValue) types.Add(dropType.Value);
        if (_videoEnabled && dropType != DownloadType.Video) types.Add(DownloadType.Video);
        if (_audioEnabled && dropType != DownloadType.Audio) types.Add(DownloadType.Audio);
        if (_subsEnabled && dropType != DownloadType.Subtitles) types.Add(DownloadType.Subtitles);

        LogService.Log($"DROP on {tag}: \"{url}\" -> types: {string.Join(", ", types)}");

        foreach (var type in types)
        {
            var job = new DownloadJob { Url = url, Type = type, Title = "Loading..." };
            var vm = new JobViewModel(job);
            _jobs.Add(vm);
            _downloadService.Enqueue(job);
        }

        if (types.Count > 0)
        {
            _ = PreloadJobInfoAsync(url);
            UrlBar.Clear();
        }

        e.Handled = true;
    }

    private void DropAreaPaste_Click(object sender, RoutedEventArgs e)
    {
        LogService.Log("Context menu: Paste on drop area");
        HandlePaste();
    }

    // --- Keyboard paste (Ctrl+V) ---

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
        {
            // If the URL bar is focused, let the TextBox handle the paste normally
            if (UrlBar.IsFocused)
                return;

            HandlePaste();
            e.Handled = true;
        }
    }

    private void HandlePaste()
    {
        if (Clipboard.ContainsText())
        {
            var url = Clipboard.GetText().Trim();
            LogService.Log($"PASTE: \"{url}\"");
            EnqueueDownloads(url);
        }
        else
        {
            LogService.Log("PASTE: clipboard has no text");
        }
    }

    // --- Core download logic ---

    private void EnqueueDownloads(string url)
    {
        if (!DownloadService.IsValidYouTubeUrl(url))
        {
            LogService.Log($"REJECTED invalid YouTube URL: \"{url}\"");
            SetStatus("Invalid YouTube URL", isError: true);
            return;
        }

        var activeTypes = new List<DownloadType>();
        if (_videoEnabled) activeTypes.Add(DownloadType.Video);
        if (_audioEnabled) activeTypes.Add(DownloadType.Audio);
        if (_subsEnabled) activeTypes.Add(DownloadType.Subtitles);

        if (activeTypes.Count == 0)
        {
            LogService.Log("REJECTED: no download types selected");
            SetStatus("No download types selected. Click Video, Audio, or Subtitles to activate.", isError: true);
            return;
        }

        foreach (var type in activeTypes)
        {
            LogService.Log($"ENQUEUE {type} download: {url}");
            var job = new DownloadJob { Url = url, Type = type, Title = "Loading..." };
            var vm = new JobViewModel(job);
            _jobs.Add(vm);
            _downloadService.Enqueue(job);
        }

        _ = PreloadJobInfoAsync(url);
    }

    private async Task PreloadJobInfoAsync(string url)
    {
        try
        {
            var (title, thumbUrl, uploadDate, description) = await _downloadService.FetchPreviewAsync(url);

            // Update all jobs with this URL that still show "Loading..."
            foreach (var vm in _jobs.Where(j => j.Job.Url == url).ToList())
            {
                if (string.IsNullOrWhiteSpace(vm.Job.Title) || vm.Job.Title == "Loading...")
                {
                    vm.Job.Title = title;
                    vm.Refresh();
                }
                if (string.IsNullOrWhiteSpace(vm.Job.ThumbnailUrl))
                    vm.Job.ThumbnailUrl = thumbUrl;
                if (vm.Job.UploadDate == null)
                    vm.Job.UploadDate = uploadDate;
                if (vm.Job.Description == null)
                    vm.Job.Description = description;
                if (vm.Thumbnail == null && !string.IsNullOrEmpty(thumbUrl))
                    await LoadThumbnailAsync(vm, thumbUrl);
            }
        }
        catch (Exception ex)
        {
            LogService.Log($"Preload failed for {url}: {ex.Message}");
        }
    }

    // --- Batch size prompt ---

    private Task<int?> PromptBatchSizeAsync(int totalCount)
    {
        var tcs = new TaskCompletionSource<int?>();

        Dispatcher.Invoke(() =>
        {
            var dialog = new BatchSizeDialog(totalCount) { Owner = this };
            if (dialog.ShowDialog() == true)
                tcs.SetResult(dialog.SelectedLimit);
            else
                tcs.SetResult(null);
        });

        return tcs.Task;
    }

    // --- Thumbnail loading ---

    private async Task LoadThumbnailAsync(JobViewModel vm, string url)
    {
        try
        {
            LogService.Log($"UI: LoadThumbnailAsync start: {url}");
            var thumb = await ThumbnailService.GetThumbnailAsync(url);
            if (thumb != null)
            {
                LogService.Log($"UI: Thumbnail loaded successfully");
                vm.Thumbnail = thumb;
                vm.Refresh();
            }
            else
            {
                LogService.Log($"UI: Thumbnail returned null for: {url}");
            }
        }
        catch (Exception ex)
        {
            LogService.Error($"UI: LoadThumbnailAsync failed for {url}", ex);
        }
    }

    // --- Job context menu ---

    private void JobList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var element = e.OriginalSource as DependencyObject;
        while (element != null && element is not ListBoxItem)
            element = VisualTreeHelper.GetParent(element);

        if (element is ListBoxItem item)
        {
            item.IsSelected = true;
            JobList.Focus();
        }
    }

    private void JobOpenFile_Click(object sender, RoutedEventArgs e)
    {
        if (JobList.SelectedItem is not JobViewModel vm) return;
        var path = vm.Job.OutputPath;
        LogService.Log($"Open File: OutputPath={path}");
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        else
        {
            SetStatus($"File not found: {path ?? "(no path)"}", isError: true);
        }
    }

    private void JobOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (JobList.SelectedItem is not JobViewModel vm) return;
        var path = vm.Job.OutputPath;
        LogService.Log($"Open Folder: OutputPath={path}");
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
        else if (!string.IsNullOrEmpty(path))
        {
            // File gone but try opening the folder
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                Process.Start("explorer.exe", $"\"{dir}\"");
            else
                SetStatus($"Folder not found: {dir ?? "(no path)"}", isError: true);
        }
        else
        {
            SetStatus("No output path for this job", isError: true);
        }
    }

    private void JobRetry_Click(object sender, RoutedEventArgs e)
    {
        if (JobList.SelectedItem is JobViewModel vm)
        {
            LogService.Log($"Retrying job: {vm.Job.Type} - {vm.Job.Url}");
            var job = new DownloadJob { Url = vm.Job.Url, Type = vm.Job.Type, Title = "Loading..." };
            var newVm = new JobViewModel(job);
            _jobs.Add(newVm);
            _downloadService.Enqueue(job);
        }
    }

    private void JobRemove_Click(object sender, RoutedEventArgs e)
    {
        if (JobList.SelectedItem is JobViewModel vm)
        {
            LogService.Log($"Removing job from list: {vm.Title}");
            _jobs.Remove(vm);
        }
    }

    private void JobClearList_Click(object sender, RoutedEventArgs e)
    {
        LogService.Log("Clearing job list");
        _jobs.Clear();
    }

    // --- Layout save/restore ---

    private void ApplyLayout(AppConfig cfg)
    {
        if (cfg.WindowState == (int)WindowState.Maximized)
        {
            if (cfg.WindowWidth > 0) Width = cfg.WindowWidth;
            if (cfg.WindowHeight > 0) Height = cfg.WindowHeight;
            if (!double.IsNaN(cfg.WindowLeft) && !double.IsNaN(cfg.WindowTop))
            {
                Left = cfg.WindowLeft;
                Top = cfg.WindowTop;
            }
            WindowState = WindowState.Maximized;
        }
        else
        {
            if (cfg.WindowWidth > 0) Width = cfg.WindowWidth;
            if (cfg.WindowHeight > 0) Height = cfg.WindowHeight;
            if (!double.IsNaN(cfg.WindowLeft) && !double.IsNaN(cfg.WindowTop))
            {
                Left = cfg.WindowLeft;
                Top = cfg.WindowTop;
            }
            WindowState = WindowState.Normal;
        }

        if (cfg.JobsColumnWidth > 0)
            JobsColumn.Width = new GridLength(cfg.JobsColumnWidth, GridUnitType.Pixel);
        if (cfg.MiddleColumnWidth > 0)
            MiddleColumn.Width = new GridLength(cfg.MiddleColumnWidth, GridUnitType.Pixel);
        LogColumn.Width = new GridLength(1, GridUnitType.Star);

        ApplyLogPanelVisibility(cfg.ShowLogPanel);

        LogService.Log($"Layout applied: {cfg.WindowWidth}x{cfg.WindowHeight} @ ({cfg.WindowLeft},{cfg.WindowTop}) " +
                       $"state={cfg.WindowState} jobs={cfg.JobsColumnWidth} middle={cfg.MiddleColumnWidth}");
    }

    private void CaptureLayout(AppConfig cfg)
    {
        var bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;

        cfg.WindowLeft = bounds.Left;
        cfg.WindowTop = bounds.Top;
        cfg.WindowWidth = bounds.Width;
        cfg.WindowHeight = bounds.Height;
        cfg.WindowState = (int)WindowState;
        cfg.JobsColumnWidth = JobsColumn.ActualWidth > 0 ? JobsColumn.ActualWidth : JobsColumn.Width.Value;
        cfg.MiddleColumnWidth = MiddleColumn.ActualWidth > 0 ? MiddleColumn.ActualWidth : MiddleColumn.Width.Value;
    }

    // --- Log panel visibility ---

    private double _savedJobsColumnWidth;

    private void ApplyLogPanelVisibility(bool show)
    {
        if (show)
        {
            LogPanel.Visibility = Visibility.Visible;
            LogSplitter.Visibility = Visibility.Visible;
            LogColumn.MinWidth = 180;
            LogColumn.Width = new GridLength(1, GridUnitType.Star);
            // Restore Jobs to pixel width so it's independently resizable
            if (_savedJobsColumnWidth > 0)
                JobsColumn.Width = new GridLength(_savedJobsColumnWidth, GridUnitType.Pixel);
        }
        else
        {
            // Save Jobs pixel width before switching to star
            _savedJobsColumnWidth = JobsColumn.ActualWidth > 0 ? JobsColumn.ActualWidth : JobsColumn.Width.Value;
            LogPanel.Visibility = Visibility.Collapsed;
            LogSplitter.Visibility = Visibility.Collapsed;
            LogColumn.MinWidth = 0;
            LogColumn.Width = new GridLength(0);
            // Jobs takes remaining space when Log is hidden
            JobsColumn.Width = new GridLength(1, GridUnitType.Star);
        }
    }

    // --- Live log panel ---

    private void OnLogWritten(string line)
    {
        if (Dispatcher.CheckAccess())
            AppendLogLine(line);
        else
            Dispatcher.BeginInvoke(new Action(() => AppendLogLine(line)));
    }

    private void AppendLogLine(string line)
    {
        LogViewer.AppendText(line + Environment.NewLine);

        const int maxChars = 200_000;
        if (LogViewer.Text.Length > maxChars)
        {
            var text = LogViewer.Text;
            var trimFrom = text.Length - maxChars;
            var nextNewline = text.IndexOf('\n', trimFrom);
            if (nextNewline >= 0 && nextNewline + 1 < text.Length)
                LogViewer.Text = text.Substring(nextNewline + 1);
        }

        LogViewer.ScrollToEnd();
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e)
    {
        LogViewer.Clear();
        LogService.Log("Log panel cleared by user");
    }

    // --- Menu handlers ---

    private void Preferences_Click(object sender, RoutedEventArgs e)
    {
        LogService.Log("Menu: Preferences clicked");
        var dialog = new PreferencesDialog(_config) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            var r = dialog.ResultConfig;
            _config.VideoDownloadFolder = r.VideoDownloadFolder;
            _config.AudioDownloadFolder = r.AudioDownloadFolder;
            _config.SubtitlesDownloadFolder = r.SubtitlesDownloadFolder;
            _config.PrependUploadDate = r.PrependUploadDate;
            _config.MonitorClipboard = r.MonitorClipboard;
            _config.VideoQuality = r.VideoQuality;
            _config.PreferOriginalAudioVideo = r.PreferOriginalAudioVideo;
            _config.AudioFormat = r.AudioFormat;
            _config.AudioQuality = r.AudioQuality;
            _config.PreferOriginalAudioAudio = r.PreferOriginalAudioAudio;
            _config.SubtitleLanguages = r.SubtitleLanguages;
            _config.SubtitleObsidianFormat = r.SubtitleObsidianFormat;
            _config.SubtitleFallbackOriginal = r.SubtitleFallbackOriginal;
            _config.SubtitlePreferManual = r.SubtitlePreferManual;

            _config.ShowLogPanel = r.ShowLogPanel;

            _downloadService.Config = _config;
            _clipboardMonitor.IsEnabled = _config.MonitorClipboard;
            ApplyLogPanelVisibility(_config.ShowLogPanel);
            _configService.Save(_config);
            LogService.Log("Preferences saved");
        }
        else
        {
            LogService.Log("Preferences dialog cancelled");
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        LogService.Log("Menu: Exit clicked");
        Close();
    }

    private void StopOperation_Click(object sender, RoutedEventArgs e)
    {
        LogService.Log("Menu: Stop Operation clicked");
        _downloadService.StopAll();
        HideProgress();
        UpdateMenuState();
    }

    private void ResumeOperation_Click(object sender, RoutedEventArgs e)
    {
        LogService.Log("Menu: Resume Operation clicked");
        var stoppedJobs = _jobs.Where(vm =>
            vm.Job.Status == JobStatus.Error && vm.Job.ErrorMessage == "Stopped by user").ToList();

        foreach (var vm in stoppedJobs)
        {
            vm.Job.Status = JobStatus.Queued;
            vm.Job.ErrorMessage = null;
            vm.Refresh();
            _downloadService.Enqueue(vm.Job);
        }

        LogService.Log($"Resumed {stoppedJobs.Count} stopped jobs");
        UpdateMenuState();
    }

    private void UpdateMenuState()
    {
        StopOperationMenu.IsEnabled = _downloadService.IsRunning;
        ResumeOperationMenu.IsEnabled = !_downloadService.IsRunning &&
            _jobs.Any(vm => vm.Job.Status == JobStatus.Error && vm.Job.ErrorMessage == "Stopped by user");
    }

    private void OpenVideoFolder_Click(object sender, RoutedEventArgs e)
        => OpenFolder(_config.GetDownloadFolder(DownloadType.Video));

    private void OpenAudioFolder_Click(object sender, RoutedEventArgs e)
        => OpenFolder(_config.GetDownloadFolder(DownloadType.Audio));

    private void OpenSubsFolder_Click(object sender, RoutedEventArgs e)
        => OpenFolder(_config.GetDownloadFolder(DownloadType.Subtitles));

    private void OpenFolder(string folder)
    {
        LogService.Log($"Menu: Open folder: {folder}");
        if (Directory.Exists(folder))
            Process.Start("explorer.exe", folder);
        else
        {
            LogService.Log($"Folder does not exist: {folder}");
            MessageBox.Show(this, $"Folder does not exist:\n{folder}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void UserGuide_Click(object sender, RoutedEventArgs e)
    {
        LogService.Log("Menu: User Guide clicked");
        new UserGuideDialog { Owner = this }.ShowDialog();
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        LogService.Log("Menu: About clicked");
        new AboutDialog { Owner = this }.ShowDialog();
    }

    private async void UpdateYtDlp_Click(object sender, RoutedEventArgs e)
    {
        LogService.Log("Menu: Update yt-dlp clicked");
        SetStatus("Updating yt-dlp...");
        try
        {
            var result = await DependencyManager.UpdateYtDlpAsync();
            SetStatus($"yt-dlp update: {result.Split('\n').LastOrDefault(l => !string.IsNullOrWhiteSpace(l)) ?? "done"}");
        }
        catch (Exception ex)
        {
            LogService.Error("yt-dlp update failed", ex);
            SetStatus($"yt-dlp update failed: {ex.Message}", isError: true);
        }
    }

    // --- Download service event handlers ---

    private void OnJobStarted(DownloadJob job)
    {
        LogService.Log($"UI: Job started - {job.Type}: {job.Title} ({job.Url})");
        Dispatcher.Invoke(() =>
        {
            var vm = FindJobVm(job);
            if (vm != null)
            {
                vm.Refresh();
                // Load thumbnail if it's already available and not yet loaded
                if (vm.Thumbnail == null && !string.IsNullOrEmpty(job.ThumbnailUrl))
                {
                    LogService.Log($"UI: Loading thumbnail in OnJobStarted: {job.ThumbnailUrl}");
                    _ = LoadThumbnailAsync(vm, job.ThumbnailUrl);
                }
            }
            ShowProgress();
            UpdateMenuState();
        });
    }

    private void OnJobCompleted(DownloadJob job)
    {
        LogService.Log($"UI: Job completed - {job.Type}: {job.Title}");
        Dispatcher.Invoke(() =>
        {
            var vm = FindJobVm(job);
            vm?.Refresh();
        });
    }

    private void OnJobError(DownloadJob job, string error)
    {
        LogService.Log($"UI: Job error - {job.Type}: {job.Title} - {error}");
        Dispatcher.Invoke(() =>
        {
            var vm = FindJobVm(job);
            vm?.Refresh();
            SetStatus(error, isError: true);
        });
    }

    private void OnJobUpdated(DownloadJob job)
    {
        LogService.Log($"UI: JobUpdated: title={job.Title}, thumbUrl={job.ThumbnailUrl}");
        Dispatcher.Invoke(() =>
        {
            var vm = FindJobVm(job);
            if (vm != null)
            {
                vm.Refresh();
                if (vm.Thumbnail == null && !string.IsNullOrEmpty(job.ThumbnailUrl))
                {
                    LogService.Log($"UI: Loading thumbnail in OnJobUpdated: {job.ThumbnailUrl}");
                    _ = LoadThumbnailAsync(vm, job.ThumbnailUrl);
                }
            }
        });
    }

    private void OnProgressChanged(double progress)
    {
        Dispatcher.Invoke(() =>
        {
            if (progress < 0)
            {
                ShowProgress();
                ProgressBar.IsIndeterminate = true;
            }
            else if (progress == 0)
            {
                HideProgress();
            }
            else
            {
                ShowProgress();
                ProgressBar.IsIndeterminate = false;
                ProgressBar.Value = progress;
            }
        });
    }

    private void OnStatusChanged(string status)
    {
        LogService.Log($"UI: Status changed: {status}");
        Dispatcher.Invoke(() => SetStatus(status));
    }

    private void OnDuplicateSkipped(string filename)
    {
        LogService.Log($"UI: Duplicate skipped: {filename}");
        Dispatcher.Invoke(() =>
            SetStatus($"File already exists: {filename}. Delete it manually to re-download.", isError: true));
    }

    private void OnAllCompleted(int count)
    {
        LogService.Log($"UI: All completed, {count} files downloaded");
        Dispatcher.Invoke(() =>
        {
            HideProgress();
            SetStatus($"Done: downloaded {count} files");
            UpdateMenuState();

            // Show tray notification if minimized
            if (!IsVisible || WindowState == WindowState.Minimized)
            {
                _trayIcon?.ShowBalloonTip(5000, "YTubeFetch",
                    $"Downloaded {count} files",
                    WinForms.ToolTipIcon.Info);
            }
        });
    }

    // --- Progress bar ---

    private void ShowProgress()
    {
        if (ProgressBar.Visibility != Visibility.Visible)
            ProgressBar.Visibility = Visibility.Visible;
    }

    private void HideProgress()
    {
        ProgressBar.IsIndeterminate = false;
        ProgressBar.Value = 0;
        ProgressBar.Visibility = Visibility.Collapsed;
    }

    // --- Helpers ---

    private void SetStatus(string text, bool isError = false)
    {
        StatusText.Text = text;
        StatusText.Foreground = isError
            ? Brushes.Red
            : new SolidColorBrush(ThemeService.TextPrimary);
        StatusText.FontWeight = isError ? FontWeights.Bold : FontWeights.Normal;

        if (isError)
            LogService.Log($"STATUS ERROR: {text}");
    }

    private JobViewModel? FindJobVm(DownloadJob job) =>
        _jobs.FirstOrDefault(vm => vm.Job == job);

    private static bool HasUrl(DragEventArgs e)
    {
        return e.Data.GetDataPresent(DataFormats.Text) ||
               e.Data.GetDataPresent(DataFormats.UnicodeText) ||
               e.Data.GetDataPresent("UniformResourceLocator");
    }

    private static string? ExtractUrl(DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.UnicodeText))
            return e.Data.GetData(DataFormats.UnicodeText) as string;
        if (e.Data.GetDataPresent(DataFormats.Text))
            return e.Data.GetData(DataFormats.Text) as string;
        if (e.Data.GetDataPresent("UniformResourceLocator"))
            return e.Data.GetData("UniformResourceLocator") as string;
        return null;
    }
}

public class JobViewModel : INotifyPropertyChanged
{
    public DownloadJob Job { get; }

    public JobViewModel(DownloadJob job) => Job = job;

    public string Title
    {
        get
        {
            var title = string.IsNullOrWhiteSpace(Job.Title) ? "Loading..." : Job.Title;
            return title;
        }
    }

    public string TypeIcon => Job.Type switch
    {
        DownloadType.Video => "/Resources/Icons/video.png",
        DownloadType.Audio => "/Resources/Icons/audio.png",
        DownloadType.Subtitles => "/Resources/Icons/subs.png",
        _ => "/Resources/Icons/video.png"
    };

    public BitmapImage? Thumbnail { get; set; }

    public Visibility ThumbnailVisibility =>
        Thumbnail != null ? Visibility.Visible : Visibility.Collapsed;

    public Brush ThumbnailBorderBrush => Job.Status == JobStatus.Running
        ? new SolidColorBrush(ThemeService.AccentColor)
        : new SolidColorBrush(ThemeService.PanelBorder);

    public Brush StatusColor => Job.Status switch
    {
        JobStatus.Running => new SolidColorBrush(ThemeService.AccentColor),
        JobStatus.Error => Brushes.Red,
        JobStatus.Completed => new SolidColorBrush(ThemeService.TextPrimary),
        _ => new SolidColorBrush(ThemeService.TextSecondary)
    };

    public FontWeight StatusWeight => Job.Status switch
    {
        JobStatus.Running => FontWeights.SemiBold,
        JobStatus.Error => FontWeights.Bold,
        _ => FontWeights.Normal
    };

    public void Refresh()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusColor)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusWeight)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThumbnailVisibility)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThumbnailBorderBrush)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
