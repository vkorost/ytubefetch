using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using YTubeFetch.Models;

namespace YTubeFetch.Services;

public partial class DownloadService
{
    private readonly Queue<DownloadJob> _queue = new();
    private DownloadJob? _currentJob;
    private Process? _currentProcess;
    private CancellationTokenSource? _cts;
    private bool _isRunning;

    public string YtDlpPath { get; set; } = "yt-dlp";
    public string FfmpegPath { get; set; } = "ffmpeg";
    public AppConfig Config { get; set; } = new();

    /// <summary>
    /// Callback to prompt user for batch size when playlist has more than 50 items.
    /// Parameter: total item count. Returns: selected limit (0=All), or null if cancelled.
    /// </summary>
    public Func<int, Task<int?>>? BatchSizePrompt { get; set; }

    public event Action<DownloadJob>? JobStarted;
    public event Action<DownloadJob>? JobCompleted;
    public event Action<DownloadJob, string>? JobError;
    public event Action<DownloadJob>? JobUpdated;
    public event Action<double>? ProgressChanged;
    public event Action<string>? StatusChanged;
    public event Action<string>? DuplicateSkipped;
    public event Action<int>? AllCompleted;

    public bool IsRunning => _isRunning;
    public IReadOnlyCollection<DownloadJob> QueuedJobs => _queue;
    public DownloadJob? CurrentJob => _currentJob;

    public void Enqueue(DownloadJob job)
    {
        LogService.Log($"Enqueue job: {job.Type} - {job.Url}");
        _queue.Enqueue(job);
        if (!_isRunning)
            _ = ProcessQueueAsync();
    }

    public async Task<(string title, string thumbnailUrl, string uploadDate, string description)> FetchPreviewAsync(string url, CancellationToken ct = default)
    {
        // Strip playlist params for single videos
        if (IsSingleVideo(url))
            url = StripPlaylistParams(url);

        var (uploadDate, title, thumbnailUrl, description) = await GetMetadataAsync(url, ct);
        return (string.IsNullOrWhiteSpace(title) ? "Unknown" : title, thumbnailUrl, uploadDate, description);
    }

    public void StopAll()
    {
        LogService.Log("StopAll called");
        if (_currentJob != null && _currentJob.Status == JobStatus.Running)
        {
            _currentJob.Status = JobStatus.Error;
            _currentJob.ErrorMessage = "Stopped by user";
            JobError?.Invoke(_currentJob, "Stopped by user");
        }
        foreach (var queued in _queue)
        {
            queued.Status = JobStatus.Error;
            queued.ErrorMessage = "Stopped by user";
            JobError?.Invoke(queued, "Stopped by user");
        }

        _cts?.Cancel();
        try { _currentProcess?.Kill(true); } catch { }
        _queue.Clear();
        _isRunning = false;
        _currentJob = null;
        StatusChanged?.Invoke("Ready");
        ProgressChanged?.Invoke(0);
    }

    private async Task ProcessQueueAsync()
    {
        _isRunning = true;
        _cts = new CancellationTokenSource();
        int totalDownloaded = 0;

        try
        {
            while (_queue.Count > 0 && !_cts.Token.IsCancellationRequested)
            {
                _currentJob = _queue.Dequeue();
                _currentJob.Status = JobStatus.Running;
                JobStarted?.Invoke(_currentJob);

                try
                {
                    int count = await ExecuteJobAsync(_currentJob, _cts.Token);
                    totalDownloaded += count;
                    _currentJob.Status = JobStatus.Completed;
                    JobCompleted?.Invoke(_currentJob);
                    LogService.Log($"Job completed: {_currentJob.Title}");
                }
                catch (OperationCanceledException)
                {
                    LogService.Log("Job cancelled");
                    break;
                }
                catch (Exception ex)
                {
                    _currentJob.Status = JobStatus.Error;
                    _currentJob.ErrorMessage = ex.Message;
                    JobError?.Invoke(_currentJob, ex.Message);
                    LogService.Error($"Job failed: {_currentJob.Url}", ex);
                }
            }
        }
        finally
        {
            _isRunning = false;
            _currentJob = null;
            if (totalDownloaded > 0)
            {
                LogService.Log($"All jobs done: {totalDownloaded} files downloaded");
                AllCompleted?.Invoke(totalDownloaded);
            }
            else if (!_cts.Token.IsCancellationRequested)
            {
                StatusChanged?.Invoke("Ready");
            }
            ProgressChanged?.Invoke(0);
        }
    }

    private async Task<int> ExecuteJobAsync(DownloadJob job, CancellationToken ct)
    {
        bool isBatch = IsPlaylistOrChannel(job.Url);
        LogService.Log($"ExecuteJob: batch={isBatch}, type={job.Type}, url={job.Url}");

        if (isBatch)
            return await ExecuteBatchAsync(job, ct);
        else
        {
            bool downloaded = await ExecuteSingleAsync(job.Url, job.Type, job, ct);
            return downloaded ? 1 : 0;
        }
    }

    private async Task<int> ExecuteBatchAsync(DownloadJob job, CancellationToken ct)
    {
        // Get total playlist count
        int totalCount = await GetPlaylistCountAsync(job.Url, ct);
        LogService.Log($"Playlist count: {totalCount}");

        int limit;
        if (totalCount > 50 && BatchSizePrompt != null)
        {
            var selected = await BatchSizePrompt(totalCount);
            if (selected == null)
            {
                LogService.Log("Batch size prompt cancelled by user");
                return 0;
            }
            limit = selected.Value == 0 ? totalCount : selected.Value;
        }
        else
        {
            limit = totalCount > 0 ? Math.Min(totalCount, 50) : 50;
        }

        var urls = await GetPlaylistUrlsAsync(job.Url, limit, ct);
        job.TotalCount = urls.Count;
        job.IsBatch = true;
        int downloaded = 0;
        LogService.Log($"Batch: downloading {urls.Count} of {totalCount} items");

        for (int i = 0; i < urls.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            job.CurrentIndex = i + 1;
            StatusChanged?.Invoke($"Downloading {job.Type} [{job.CurrentIndex}/{job.TotalCount}]: {job.Title}");

            try
            {
                bool ok = await ExecuteSingleAsync(urls[i], job.Type, job, ct);
                if (ok) downloaded++;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                LogService.Error($"Batch item {i + 1} failed: {urls[i]}", ex);
                JobError?.Invoke(job, $"Item {i + 1}: {ex.Message}");
            }

            if (i < urls.Count - 1)
                await Task.Delay(10000, ct);
        }

        return downloaded;
    }

    private async Task<bool> ExecuteSingleAsync(string url, DownloadType type, DownloadJob job, CancellationToken ct)
    {
        // Strip playlist params from single video URLs so yt-dlp downloads only that video
        if (IsSingleVideo(url))
        {
            var stripped = StripPlaylistParams(url);
            if (stripped != url)
            {
                LogService.Log($"Stripped playlist params: {url} -> {stripped}");
                url = stripped;
            }
        }

        // Step 1: Get metadata (skip if preview already fetched it for this exact URL)
        string uploadDate;
        string title;
        string description;
        if (!job.IsBatch && !string.IsNullOrWhiteSpace(job.Title) && job.Title != "Loading..." && job.UploadDate != null)
        {
            uploadDate = job.UploadDate;
            title = job.Title;
            description = job.Description ?? "";
            LogService.Log($"Metadata (cached from preview): date={uploadDate}, title={title}");
        }
        else
        {
            LogService.Log($"Getting metadata for: {url}");
            string thumbnailUrl;
            (uploadDate, title, thumbnailUrl, description) = await GetMetadataAsync(url, ct);
            if (string.IsNullOrWhiteSpace(title))
                title = "Unknown";

            if (string.IsNullOrWhiteSpace(job.ThumbnailUrl) && !string.IsNullOrWhiteSpace(thumbnailUrl))
                job.ThumbnailUrl = thumbnailUrl;

            if (string.IsNullOrWhiteSpace(job.Title) || job.Title == "Loading...")
            {
                job.Title = title;
                JobUpdated?.Invoke(job);
            }

            job.Description = description;

            LogService.Log($"Metadata: date={uploadDate}, title={title}");
        }
        StatusChanged?.Invoke($"Downloading {type}{(job.IsBatch ? $" [{job.CurrentIndex}/{job.TotalCount}]" : "")}: {title}");

        var safeTitle = SanitizeFilename(title);
        var datePrefix = Config.PrependUploadDate ? FormatDatePrefix(uploadDate) : "";

        if (type == DownloadType.Subtitles)
            return await DownloadSubtitlesAsync(url, datePrefix, safeTitle, title, uploadDate, description, job, ct);

        // Build filename
        string ext = type == DownloadType.Video ? ".mp4" : GetAudioExtension();
        string filename = string.IsNullOrEmpty(datePrefix)
            ? $"{safeTitle}{ext}"
            : $"{datePrefix}-{safeTitle}{ext}";

        string folder = Config.GetDownloadFolder(type);
        string outputPath = Path.Combine(folder, filename);
        LogService.Log($"Output path: {outputPath}");

        // Check duplicate
        if (File.Exists(outputPath))
        {
            LogService.Log($"Duplicate skipped: {filename}");
            job.OutputPath = outputPath;
            DuplicateSkipped?.Invoke(filename);
            return false;
        }

        // Build and run yt-dlp
        string args = type == DownloadType.Video
            ? BuildVideoArgs(outputPath, url)
            : BuildAudioArgs(outputPath, url);

        await RunYtDlpWithRetryAsync(args, ct);
        job.OutputPath = outputPath;
        return true;
    }

    private string BuildVideoArgs(string outputPath, string url)
    {
        string heightFilter = Config.VideoQuality switch
        {
            "1080" => "[height<=1080]",
            "720" => "[height<=720]",
            "480" => "[height<=480]",
            "360" => "[height<=360]",
            _ => ""
        };

        string origAudio = Config.PreferOriginalAudioVideo ? "[format_note*=original]" : "";

        string formatArg = string.IsNullOrEmpty(heightFilter)
            ? $"\"bv*+ba{origAudio}/bv*+ba/b\""
            : $"\"bv*{heightFilter}+ba{origAudio}/bv*{heightFilter}+ba/b\"";

        return $"-f {formatArg} --merge-output-format mp4 --format-sort lang,quality,res --windows-filenames --no-overwrites -o \"{outputPath}\" \"{url}\"";
    }

    private string BuildAudioArgs(string outputPath, string url)
    {
        string origAudio = Config.PreferOriginalAudioAudio ? "[format_note*=original]" : "";
        string formatArg = $"\"ba{origAudio}/ba/b\"";

        string audioFormat = Config.AudioFormat.ToLower();
        if (string.IsNullOrWhiteSpace(audioFormat)) audioFormat = "mp3";

        string audioQuality = Config.AudioQuality switch
        {
            "320" => "320K",
            "256" => "256K",
            "192" => "192K",
            "128" => "128K",
            _ => "0"
        };

        return $"-f {formatArg} --format-sort lang,quality -x --audio-format {audioFormat} --audio-quality {audioQuality} --windows-filenames --no-overwrites -o \"{outputPath}\" \"{url}\"";
    }

    private string GetAudioExtension()
    {
        return Config.AudioFormat.ToLower() switch
        {
            "aac" => ".aac",
            "ogg" => ".ogg",
            "opus" => ".opus",
            "wav" => ".wav",
            _ => ".mp3"
        };
    }

    /// <summary>
    /// Checks whether a string contains any Cyrillic characters.
    /// </summary>
    private static bool HasCyrillic(string text) =>
        text.Any(c => c is >= '\u0400' and <= '\u04FF');

    private async Task<bool> DownloadSubtitlesAsync(string url, string datePrefix, string safeTitle, string originalTitle, string uploadDate, string description, DownloadJob job, CancellationToken ct)
    {
        // Smart language reordering: if the title has no Cyrillic, try English first;
        // if it has Cyrillic, try Russian first. This overrides the config order.
        var languages = Config.SubtitleLanguages.ToList();
        if (!string.IsNullOrWhiteSpace(job.Title) && languages.Contains("en") && languages.Contains("ru"))
        {
            bool titleIsCyrillic = HasCyrillic(job.Title);
            if (!titleIsCyrillic && languages.IndexOf("en") > languages.IndexOf("ru"))
            {
                // English video but Russian is first — swap to put English first
                languages.Remove("en");
                languages.Insert(0, "en");
                LogService.Log($"Smart subtitle order: English title detected, reordered to [{string.Join(",", languages)}]");
            }
            else if (titleIsCyrillic && languages.IndexOf("ru") > languages.IndexOf("en"))
            {
                // Russian video but English is first — swap to put Russian first
                languages.Remove("ru");
                languages.Insert(0, "ru");
                LogService.Log($"Smart subtitle order: Russian title detected, reordered to [{string.Join(",", languages)}]");
            }
        }

        // Build subtitle attempt list
        var attempts = new List<string[]>();

        foreach (var lang in languages)
        {
            if (Config.SubtitlePreferManual)
            {
                attempts.Add([lang, "--write-subs", "--sub-lang", lang]);
                attempts.Add([lang, "--write-auto-subs", "--sub-lang", lang]);
            }
            else
            {
                attempts.Add([lang, "--write-auto-subs", "--sub-lang", lang]);
                attempts.Add([lang, "--write-subs", "--sub-lang", lang]);
            }
        }

        if (Config.SubtitleFallbackOriginal)
        {
            attempts.Add(["orig", "--write-subs", "--sub-lang", "all"]);
            attempts.Add(["orig", "--write-auto-subs", "--sub-lang", "all"]);
        }

        LogService.Log($"Subtitle attempts: {attempts.Count} (languages: {string.Join(",", languages)})");

        foreach (var attempt in attempts)
        {
            ct.ThrowIfCancellationRequested();

            string lang = attempt[0];
            string subArgs = string.Join(" ", attempt[1..]);
            LogService.Log($"Trying subtitles: lang={lang}, args={subArgs}");

            string ext = Config.SubtitleObsidianFormat ? ".md" : ".txt";
            string filename = string.IsNullOrEmpty(datePrefix)
                ? $"{safeTitle} [{lang}]{ext}"
                : $"{datePrefix}-{safeTitle} [{lang}]{ext}";

            string folder = Config.GetDownloadFolder(DownloadType.Subtitles);
            string outputPath = Path.Combine(folder, filename);

            if (File.Exists(outputPath))
            {
                LogService.Log($"Subtitle duplicate skipped: {filename}");
                job.OutputPath = outputPath;
                DuplicateSkipped?.Invoke(filename);
                return false;
            }

            string tempDir = Path.Combine(Path.GetTempPath(), "YTubeFetch_subs_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(tempDir);

            try
            {
                string tempTemplate = Path.Combine(tempDir, "sub");
                string args = $"{subArgs} --skip-download -o \"{tempTemplate}\" \"{url}\"";

                try
                {
                    await RunYtDlpAsync(args, ct);
                }
                catch (Exception ex)
                {
                    LogService.Log($"Subtitle attempt failed ({lang}): {ex.Message}");
                    continue;
                }

                var subFiles = Directory.GetFiles(tempDir, "sub.*");
                LogService.Log($"Subtitle files found in temp: {subFiles.Length}");
                if (subFiles.Length == 0)
                    continue;

                var rawText = await File.ReadAllTextAsync(subFiles[0], ct);
                var cleanText = SubtitleProcessor.StripTimestamps(rawText);

                if (string.IsNullOrWhiteSpace(cleanText))
                    continue;

                // Build output with metadata header
                string output;
                if (Config.SubtitleObsidianFormat)
                    output = FormatObsidianSubtitles(originalTitle, url, uploadDate, description, cleanText);
                else
                    output = FormatPlainTextSubtitles(originalTitle, url, uploadDate, description, cleanText);

                await File.WriteAllTextAsync(outputPath, output, ct);
                LogService.Log($"Subtitles saved: {outputPath}");
                job.OutputPath = outputPath;
                return true;
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        throw new Exception("No subtitles found in any language");
    }

    private async Task<(string uploadDate, string title, string thumbnailUrl, string description)> GetMetadataAsync(string url, CancellationToken ct)
    {
        string args = $"--print \"%(upload_date)s\" --print \"%(title)s\" --print \"%(thumbnail)s\" --print \"---YTF_DESC---\" --print \"%(description)s\" --no-download \"{url}\"";
        var output = await RunYtDlpCaptureAsync(args, ct);

        // Description can be multiline, so split on delimiter
        string headerPart, description;
        var delimIndex = output.IndexOf("---YTF_DESC---");
        if (delimIndex >= 0)
        {
            headerPart = output[..delimIndex];
            description = output[(delimIndex + "---YTF_DESC---".Length)..].Trim();
        }
        else
        {
            headerPart = output;
            description = "";
        }

        var lines = headerPart.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        string uploadDate = lines.Length > 0 ? lines[0].Trim() : "";
        string title = lines.Length > 1 ? lines[1].Trim() : "";
        string thumbnailUrl = lines.Length > 2 ? lines[2].Trim() : "";

        if (uploadDate == "NA" || uploadDate == "null")
            uploadDate = "";
        if (thumbnailUrl == "NA" || thumbnailUrl == "null")
            thumbnailUrl = "";
        if (description == "NA" || description == "null")
            description = "";

        return (uploadDate, title, thumbnailUrl, description);
    }

    private async Task<int> GetPlaylistCountAsync(string url, CancellationToken ct)
    {
        string args = $"--flat-playlist --print \"%(playlist_count)s\" --playlist-end 1 \"{url}\"";
        var output = await RunYtDlpCaptureAsync(args, ct);
        var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        if (int.TryParse(line, out int count) && count > 0)
            return count;

        // Fallback: count all URLs
        LogService.Log("Playlist count unavailable, counting URLs...");
        args = $"--flat-playlist --print url \"{url}\"";
        output = await RunYtDlpCaptureAsync(args, ct);
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private async Task<List<string>> GetPlaylistUrlsAsync(string url, int limit, CancellationToken ct)
    {
        string limitArg = limit > 0 ? $"--playlist-end {limit}" : "";
        string args = $"--flat-playlist --print url {limitArg} \"{url}\"";
        var output = await RunYtDlpCaptureAsync(args, ct);
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(u => u.Trim())
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .ToList();
    }

    private async Task RunYtDlpWithRetryAsync(string args, CancellationToken ct)
    {
        int[] delays = [10000, 30000, 60000];

        for (int attempt = 0; attempt <= 2; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                await RunYtDlpAsync(args, ct);
                return;
            }
            catch (YtDlpException ex) when (ex.Is429 && attempt < 2)
            {
                LogService.Log($"429 rate limited, retry {attempt + 1}/3 after {delays[attempt] / 1000}s");
                StatusChanged?.Invoke($"Rate limited (429). Retrying in {delays[attempt] / 1000}s...");
                await Task.Delay(delays[attempt], ct);
            }
        }
    }

    private async Task RunYtDlpAsync(string args, CancellationToken ct)
    {
        // --encoding utf-8 is required: the standalone yt-dlp build ignores PYTHONIOENCODING
        // and falls back to the console codepage, mangling Cyrillic titles to '?'.
        args = "--encoding utf-8 --js-runtimes node --remote-components ejs:github " + args;
        LogService.Log($"yt-dlp: {args}");

        var psi = new ProcessStartInfo
        {
            FileName = YtDlpPath,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        psi.Environment["PYTHONIOENCODING"] = "utf-8";

        using var process = new Process { StartInfo = psi };
        _currentProcess = process;

        var stderr = new List<string>();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            ParseProgress(e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                stderr.Add(e.Data);
                LogService.Log($"yt-dlp stderr: {e.Data}");
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { }
            throw;
        }
        finally
        {
            _currentProcess = null;
        }

        LogService.Log($"yt-dlp exited with code {process.ExitCode}");

        if (process.ExitCode != 0)
        {
            var errorText = string.Join("\n", stderr);
            bool is429 = errorText.Contains("HTTP Error 429", StringComparison.OrdinalIgnoreCase)
                       || errorText.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase);
            LogService.Error($"yt-dlp failed (exit={process.ExitCode}, 429={is429}): {errorText}");
            throw new YtDlpException(errorText, is429);
        }
    }

    private async Task<string> RunYtDlpCaptureAsync(string args, CancellationToken ct)
    {
        // --encoding utf-8 is required: the standalone yt-dlp build ignores PYTHONIOENCODING
        // and falls back to the console codepage, mangling Cyrillic titles to '?'.
        args = "--encoding utf-8 --js-runtimes node --remote-components ejs:github " + args;
        LogService.Log($"yt-dlp capture: {args}");

        var psi = new ProcessStartInfo
        {
            FileName = YtDlpPath,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        psi.Environment["PYTHONIOENCODING"] = "utf-8";

        using var process = new Process { StartInfo = psi };
        process.Start();

        string output = await process.StandardOutput.ReadToEndAsync(ct);
        string error = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        LogService.Log($"yt-dlp capture exit={process.ExitCode}, stdout={output.Length} chars");
        if (!string.IsNullOrWhiteSpace(error))
            LogService.Log($"yt-dlp capture stderr: {error}");

        return output;
    }

    private void ParseProgress(string line)
    {
        if (!line.Contains("[download]"))
            return;

        var match = ProgressRegex().Match(line);
        if (match.Success && double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double pct))
        {
            ProgressChanged?.Invoke(pct);
        }
        else
        {
            ProgressChanged?.Invoke(-1);
        }
    }

    public static bool IsPlaylistOrChannel(string url)
    {
        // Single video URLs take priority even when list= is present
        if (IsSingleVideo(url))
            return false;

        return url.Contains("list=") || url.Contains("/@") || url.Contains("/channel/")
            || url.Contains("/c/") || url.Contains("/user/");
    }

    public static bool IsSingleVideo(string url)
    {
        // All single-video URL patterns:
        // youtube.com/watch?v=ID, youtu.be/ID, /shorts/ID, /live/ID, /embed/ID, /v/ID, /clip/ID
        return url.Contains("watch?v=") || url.Contains("youtu.be/")
            || url.Contains("/shorts/") || url.Contains("/live/")
            || url.Contains("/embed/") || url.Contains("/v/")
            || url.Contains("/clip/");
    }

    public static bool IsValidYouTubeUrl(string url)
    {
        if (!IsYouTubeDomain(url))
            return false;
        return IsSingleVideo(url) || IsPlaylistOrChannel(url);
    }

    private static bool IsYouTubeDomain(string url)
    {
        try
        {
            var uri = new Uri(url);
            var host = uri.Host.ToLowerInvariant();
            return host is "youtube.com" or "www.youtube.com" or "m.youtube.com"
                or "music.youtube.com" or "youtu.be" or "www.youtu.be";
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Strips playlist-related query params (list=, index=, pp=) from single video URLs.
    /// </summary>
    public static string StripPlaylistParams(string url)
    {
        // Remove &list=..., &index=..., &pp=... (also handle them as first param after ?)
        url = Regex.Replace(url, @"[&?]list=[^&]*", "");
        url = Regex.Replace(url, @"[&?]index=[^&]*", "");
        url = Regex.Replace(url, @"[&?]pp=[^&]*", "");
        // Clean up any dangling ? or & left over
        url = Regex.Replace(url, @"\?&", "?");
        url = url.TrimEnd('?', '&');
        return url;
    }

    private static string SanitizeFilename(string title)
    {
        string sanitized = InvalidCharsRegex().Replace(title, "");
        sanitized = sanitized.Trim('.', ' ');
        return string.IsNullOrWhiteSpace(sanitized) ? "download" : sanitized;
    }

    private static string FormatDatePrefix(string uploadDate)
    {
        if (string.IsNullOrWhiteSpace(uploadDate) || uploadDate.Length != 8)
            return "";

        return $"{uploadDate[..4]}-{uploadDate[4..6]}-{uploadDate[6..8]}";
    }

    private static string FormatObsidianSubtitles(string title, string url, string uploadDate, string description, string transcript)
    {
        var sb = new StringBuilder();
        var formattedDate = FormatDatePrefix(uploadDate);

        // YAML frontmatter
        sb.AppendLine("---");
        sb.AppendLine($"title: \"{title.Replace("\"", "\\\"")}\"");
        sb.AppendLine($"url: {url}");
        if (!string.IsNullOrWhiteSpace(formattedDate))
            sb.AppendLine($"date: {formattedDate}");
        sb.AppendLine("---");
        sb.AppendLine();

        // Title heading
        sb.AppendLine($"# {title}");
        sb.AppendLine();

        // Info callout
        sb.AppendLine("> [!info] Video Details");
        sb.AppendLine($"> **URL:** {url}");
        if (!string.IsNullOrWhiteSpace(formattedDate))
            sb.AppendLine($"> **Date:** {formattedDate}");
        sb.AppendLine();

        // Description
        if (!string.IsNullOrWhiteSpace(description))
        {
            sb.AppendLine("## Description");
            sb.AppendLine();
            sb.AppendLine(description);
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine();

        // Transcript
        sb.AppendLine("## Transcript");
        sb.AppendLine();
        sb.Append(transcript);

        return sb.ToString();
    }

    private static string FormatPlainTextSubtitles(string title, string url, string uploadDate, string description, string transcript)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Title: {title}");
        sb.AppendLine($"URL: {url}");
        var formattedDate = FormatDatePrefix(uploadDate);
        if (!string.IsNullOrWhiteSpace(formattedDate))
            sb.AppendLine($"Date: {formattedDate}");
        if (!string.IsNullOrWhiteSpace(description))
        {
            sb.AppendLine();
            sb.AppendLine("Description:");
            sb.AppendLine(description);
        }
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.Append(transcript);

        return sb.ToString();
    }

    [GeneratedRegex(@"([\d.]+)%")]
    private static partial Regex ProgressRegex();

    [GeneratedRegex(@"[<>:""/\\|?*]")]
    private static partial Regex InvalidCharsRegex();
}

public class YtDlpException : Exception
{
    public bool Is429 { get; }

    public YtDlpException(string message, bool is429) : base(message)
    {
        Is429 = is429;
    }
}
