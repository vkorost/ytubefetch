using System.Windows;
using System.Windows.Threading;

namespace YTubeFetch.Services;

public class ClipboardMonitor
{
    private readonly DispatcherTimer _timer;
    private string? _lastUrl;

    public event Action<string>? UrlDetected;

    public bool IsEnabled { get; set; }

    public ClipboardMonitor()
    {
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += CheckClipboard;
    }

    public void Start()
    {
        _timer.Start();
        LogService.Log("Clipboard monitor started");
    }

    public void Stop()
    {
        _timer.Stop();
        LogService.Log("Clipboard monitor stopped");
    }

    private void CheckClipboard(object? sender, EventArgs e)
    {
        if (!IsEnabled) return;

        try
        {
            if (!Clipboard.ContainsText()) return;

            var text = Clipboard.GetText().Trim();
            if (string.IsNullOrWhiteSpace(text)) return;

            if (DownloadService.IsValidYouTubeUrl(text))
            {
                if (text == _lastUrl) return;
                _lastUrl = text;
                LogService.Log($"Clipboard monitor detected URL: {text}");
                UrlDetected?.Invoke(text);
            }
            else
            {
                _lastUrl = null;
            }
        }
        catch
        {
            // Clipboard access can throw if another app has it locked
        }
    }
}
