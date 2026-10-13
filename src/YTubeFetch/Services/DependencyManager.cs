using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;

namespace YTubeFetch.Services;

public static class DependencyManager
{
    private static readonly string BinDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "YTubeFetch", "bin");

    // Minimum plausible sizes for a real binary.
    //
    // A pip-installed yt-dlp is a ~108 KB launcher that shells out to python.exe.
    // It reports the right version, so it looks fine, but it cannot self-update
    // ("Update yt-dlp" fails with "You installed yt-dlp with pip ... use that to
    // update"), which silently leaves it stale until YouTube starts returning
    // HTTP 403. The real standalone build is ~18 MB.
    //
    // ffmpeg builds are ~86 MB; anything under 1 MB is not ffmpeg.
    private const long MinYtDlpBytes = 2L * 1024 * 1024;
    private const long MinFfmpegBytes = 1L * 1024 * 1024;

    // Sanity floor for "is this a file at all", used only for the degraded fallback.
    private const long MinAnyBytes = 1024;

    // yt-dlp goes stale fast — YouTube changes extraction constantly, and an old
    // build starts failing in ways that look like app bugs. Nothing here ever
    // refreshed it after the first download, so check for a newer one on launch.
    //
    // The test is "is a newer build published", not "is ours older than N days":
    // yt-dlp's own stable release is routinely weeks old, so an absolute age
    // threshold can never be satisfied and would re-download 18 MB every launch.
    private const int VersionCheckIntervalHours = 24;

    private const string YtDlpDownloadUrl =
        "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";

    // Redirects to .../releases/tag/<version>, which names the latest build
    // without downloading it and without the API's unauthenticated rate limit.
    private const string YtDlpLatestUrl =
        "https://github.com/yt-dlp/yt-dlp/releases/latest";

    // Stamped after every check, successful or not, so a failing check backs off
    // for the interval instead of retrying on every launch.
    private static string VersionCheckMarker => Path.Combine(BinDir, "yt-dlp.lastcheck");

    public static string? YtDlpPath { get; private set; }
    public static string? FfmpegPath { get; private set; }

    /// <summary>
    /// Asked before downloading yt-dlp. Return true to proceed. If left null,
    /// the download proceeds without prompting.
    /// </summary>
    public static Func<string, bool>? ConfirmDownload { get; set; }

    /// <summary>Status messages for the UI (download progress, warnings).</summary>
    public static Action<string>? StatusChanged { get; set; }

    /// <summary>
    /// Initializes the dependency manager. Prefers the local bin folder, then a
    /// properly sized copy discovered on the system, then downloads the official
    /// standalone yt-dlp build.
    /// </summary>
    public static async Task<(string? ytdlpPath, string? ffmpegPath)> InitializeAsync()
    {
        Directory.CreateDirectory(BinDir);
        LogService.Log($"DependencyManager: bin dir = {BinDir}");

        YtDlpPath = await ResolveYtDlpAsync();
        FfmpegPath = ResolveTool("ffmpeg", MinFfmpegBytes);

        LogService.Log($"DependencyManager result: yt-dlp={YtDlpPath ?? "(not found)"}, ffmpeg={FfmpegPath ?? "(not found)"}");
        return (YtDlpPath, FfmpegPath);
    }

    /// <summary>
    /// Resolves a tool: bin folder first, then discovery + copy into the bin folder.
    /// Candidates smaller than <paramref name="minBytes"/> are ignored entirely.
    /// </summary>
    private static string? ResolveTool(string name, long minBytes)
    {
        var binPath = Path.Combine(BinDir, name + ".exe");

        if (File.Exists(binPath))
        {
            var size = FileSize(binPath);
            if (size >= minBytes)
            {
                LogService.Log($"{name} found in bin: {binPath} ({size:N0} bytes)");
                return binPath;
            }

            // Do not trust it, and do not leave it shadowing a good copy.
            LogService.Log($"{name} in bin rejected: {size:N0} bytes, need >= {minBytes:N0}");
        }

        var found = FindExecutable(name, minBytes);
        if (found == null)
            return null;

        try
        {
            File.Copy(found, binPath, true);
            LogService.Log($"{name} copied to bin: {found} -> {binPath} ({FileSize(binPath):N0} bytes)");
            return binPath;
        }
        catch (Exception ex)
        {
            LogService.Log($"{name} copy failed ({ex.Message}), using directly: {found}");
            return found;
        }
    }

    private static async Task<string?> ResolveYtDlpAsync()
    {
        var resolved = ResolveTool("yt-dlp", MinYtDlpBytes);
        if (resolved != null)
            return await RefreshIfStaleAsync(resolved);

        // Nothing usable on this machine — fetch the official standalone build.
        var downloaded = await TryDownloadYtDlpAsync(prompt: true);
        if (downloaded != null)
            return downloaded;

        // Offline, or the user declined. Fall back to whatever exists, even a pip
        // launcher — a degraded yt-dlp beats no yt-dlp. Deliberately used in place
        // and never copied into bin, so a proper install is picked up next launch
        // rather than being shadowed by a stale copy we made.
        var fallback = FindExecutable("yt-dlp", MinAnyBytes);
        if (fallback != null)
        {
            LogService.Log($"WARNING: falling back to undersized yt-dlp at {fallback} " +
                           $"({FileSize(fallback):N0} bytes). This looks like a pip launcher; " +
                           "'Update yt-dlp' will not work against it.");
            StatusChanged?.Invoke("yt-dlp looks like a pip install — 'Update yt-dlp' will not work.");
            return fallback;
        }

        LogService.Log("yt-dlp not found anywhere");
        return null;
    }

    /// <summary>
    /// Re-downloads yt-dlp when the local build is older than
    /// <see cref="MaxYtDlpAgeDays"/>. Best effort by design: any failure keeps the
    /// build we already have, because a stale yt-dlp still beats no yt-dlp.
    /// </summary>
    private static async Task<string> RefreshIfStaleAsync(string path)
    {
        var binPath = Path.Combine(BinDir, "yt-dlp.exe");
        if (!string.Equals(path, binPath, StringComparison.OrdinalIgnoreCase))
        {
            // Someone else owns this copy (a system install we could not copy, or
            // the degraded pip fallback). Replacing it is not ours to do.
            LogService.Log($"yt-dlp is not our bin copy ({path}); skipping staleness check");
            return path;
        }

        var sinceLastCheck = TimeSinceLastVersionCheck();
        if (sinceLastCheck != null && sinceLastCheck.Value.TotalHours < VersionCheckIntervalHours)
        {
            LogService.Log($"yt-dlp version checked {sinceLastCheck.Value.TotalHours:F1}h ago " +
                           $"(interval {VersionCheckIntervalHours}h); skipping");
            return path;
        }

        var localBuild = await GetYtDlpBuildDateAsync(path);
        if (localBuild == null)
        {
            LogService.Log("yt-dlp build date unavailable; skipping version check");
            return path;
        }

        // Stamp before the network call so an outage backs off rather than
        // retrying on every launch.
        StampVersionCheck();

        var latestBuild = await GetLatestYtDlpBuildDateAsync();
        if (latestBuild == null)
        {
            LogService.Log("Latest yt-dlp version unavailable; keeping the current build");
            return path;
        }

        if (latestBuild.Value.Date <= localBuild.Value.Date)
        {
            LogService.Log($"yt-dlp is current: local {localBuild:yyyy-MM-dd}, " +
                           $"latest {latestBuild:yyyy-MM-dd}");
            return path;
        }

        LogService.Log($"Newer yt-dlp available: local {localBuild:yyyy-MM-dd} -> " +
                       $"latest {latestBuild:yyyy-MM-dd}");
        StatusChanged?.Invoke($"Updating yt-dlp to {latestBuild:yyyy-MM-dd}...");

        var refreshed = await TryDownloadYtDlpAsync(prompt: false);
        if (refreshed == null)
        {
            LogService.Log("yt-dlp refresh failed; continuing with the existing build");
            StatusChanged?.Invoke("yt-dlp update failed — using the existing build.");
            return path;
        }

        var newBuild = await GetYtDlpBuildDateAsync(refreshed);
        LogService.Log($"yt-dlp refreshed: build {localBuild:yyyy-MM-dd} -> " +
                       $"{(newBuild == null ? "unknown" : newBuild.Value.ToString("yyyy-MM-dd"))}");
        StatusChanged?.Invoke("yt-dlp updated.");
        return refreshed;
    }

    private static TimeSpan? TimeSinceLastVersionCheck()
    {
        try
        {
            if (!File.Exists(VersionCheckMarker)) return null;
            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(VersionCheckMarker);
            // A clock change can put the marker in the future; treat that as due.
            return age < TimeSpan.Zero ? null : age;
        }
        catch { return null; }
    }

    private static void StampVersionCheck()
    {
        try { File.WriteAllText(VersionCheckMarker, DateTime.UtcNow.ToString("O")); }
        catch (Exception ex) { LogService.Log($"Could not stamp version check: {ex.Message}"); }
    }

    /// <summary>
    /// Reads the latest published yt-dlp version without downloading it: the
    /// "releases/latest" URL redirects to "releases/tag/&lt;version&gt;".
    /// </summary>
    private static async Task<DateTime?> GetLatestYtDlpBuildDateAsync()
    {
        try
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };

            using var response = await http.GetAsync(YtDlpLatestUrl, HttpCompletionOption.ResponseHeadersRead);
            var location = response.Headers.Location?.ToString();
            if (string.IsNullOrWhiteSpace(location))
            {
                LogService.Log($"No redirect from {YtDlpLatestUrl} (status {(int)response.StatusCode})");
                return null;
            }

            var tag = location[(location.LastIndexOf('/') + 1)..];
            var parsed = ParseYtDlpVersion(tag);
            if (parsed == null)
                LogService.Log($"Unrecognized latest yt-dlp tag: '{tag}'");

            return parsed;
        }
        catch (Exception ex)
        {
            LogService.Log($"Latest yt-dlp version check failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Reads yt-dlp's build date from "--version", which prints a date-stamped
    /// version such as "2026.08.04" or "2026.08.04.234419". Returns null when it
    /// cannot be determined, which callers treat as "do not touch it".
    /// </summary>
    private static async Task<DateTime?> GetYtDlpBuildDateAsync(string path)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = path,
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            using var process = Process.Start(psi);
            if (process == null) return null;

            var stdout = await process.StandardOutput.ReadToEndAsync();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await process.WaitForExitAsync(cts.Token);

            var version = stdout.Trim().Split('\n')[0].Trim();
            var parsed = ParseYtDlpVersion(version);
            if (parsed == null)
                LogService.Log($"Unrecognized yt-dlp version string: '{version}'");

            return parsed;
        }
        catch (Exception ex)
        {
            LogService.Log($"yt-dlp --version failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Parses a yt-dlp version — "2026.08.19" or the nightly "2026.08.04.234419" —
    /// into its build date. Returns null if the string is not that shape.
    /// </summary>
    private static DateTime? ParseYtDlpVersion(string version)
    {
        var parts = version.Trim().Split('.');
        if (parts.Length < 3) return null;

        if (int.TryParse(parts[0], out int year)
            && int.TryParse(parts[1], out int month)
            && int.TryParse(parts[2], out int day)
            && year is >= 2000 and <= 2999
            && month is >= 1 and <= 12
            && day is >= 1 and <= 31)
        {
            try { return new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc); }
            catch (ArgumentOutOfRangeException) { return null; }
        }

        return null;
    }

    private static async Task<string?> TryDownloadYtDlpAsync(bool prompt)
    {
        if (prompt && ConfirmDownload != null &&
            !ConfirmDownload("A usable yt-dlp was not found.\n\n" +
                             "Only a pip launcher may be installed, which cannot update itself " +
                             "and goes stale silently.\n\n" +
                             "Download the official standalone yt-dlp.exe (~18 MB) now?"))
        {
            LogService.Log("yt-dlp download declined by user");
            return null;
        }

        var dest = Path.Combine(BinDir, "yt-dlp.exe");
        var temp = dest + ".download";

        try
        {
            LogService.Log($"Downloading yt-dlp from {YtDlpDownloadUrl}");
            StatusChanged?.Invoke("Downloading yt-dlp...");

            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            var bytes = await http.GetByteArrayAsync(YtDlpDownloadUrl);

            // Guard against a captive portal or error page landing on disk as an exe.
            if (bytes.Length < MinYtDlpBytes)
            {
                LogService.Log($"Downloaded yt-dlp is implausibly small ({bytes.Length:N0} bytes); discarding");
                return null;
            }

            // Write to a temp name first so an interrupted download cannot leave a
            // truncated yt-dlp.exe behind.
            await File.WriteAllBytesAsync(temp, bytes);
            File.Move(temp, dest, true);

            LogService.Log($"yt-dlp downloaded to {dest} ({bytes.Length:N0} bytes)");
            StatusChanged?.Invoke("yt-dlp downloaded.");
            return dest;
        }
        catch (Exception ex)
        {
            LogService.Error("yt-dlp download failed", ex);
            StatusChanged?.Invoke("yt-dlp download failed.");
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            return null;
        }
    }

    /// <summary>
    /// Runs yt-dlp -U to update the local copy. Returns the output text.
    /// </summary>
    public static async Task<string> UpdateYtDlpAsync()
    {
        if (YtDlpPath == null)
            throw new InvalidOperationException("yt-dlp is not available");

        LogService.Log($"Updating yt-dlp at: {YtDlpPath}");

        var psi = new ProcessStartInfo
        {
            FileName = YtDlpPath,
            Arguments = "-U",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        string stdout = await process.StandardOutput.ReadToEndAsync();
        string stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        var result = stdout.Trim();
        if (!string.IsNullOrWhiteSpace(stderr))
            result += "\n" + stderr.Trim();

        LogService.Log($"yt-dlp update exit={process.ExitCode}: {result}");
        return result;
    }

    private static long FileSize(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }

    /// <summary>
    /// Finds an executable of at least <paramref name="minBytes"/>. Undersized
    /// matches are skipped and the search continues, so a pip launcher sitting on
    /// PATH does not mask a real binary installed elsewhere.
    /// </summary>
    private static string? FindExecutable(string name, long minBytes)
    {
        // 1. System PATH (via where) — may return several hits
        foreach (var candidate in FindOnPath(name))
        {
            if (FileSize(candidate) >= minBytes)
            {
                LogService.Log($"Found {name} on PATH: {candidate}");
                return candidate;
            }
            LogService.Log($"Skipping undersized {name} on PATH: {candidate} ({FileSize(candidate):N0} bytes)");
        }

        // 2. Python Scripts locations (pip install --user)
        foreach (var dir in GetPythonScriptsDirs())
        {
            var exePath = Path.Combine(dir, name + ".exe");
            if (!File.Exists(exePath)) continue;

            if (FileSize(exePath) >= minBytes)
            {
                LogService.Log($"Found {name} in Python Scripts: {exePath}");
                return exePath;
            }
            LogService.Log($"Skipping undersized {name} in Python Scripts: {exePath} ({FileSize(exePath):N0} bytes)");
        }

        // 3. Common install locations
        string[] commonPaths =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", name, name + ".exe"),
            Path.Combine("C:\\ProgramData\\chocolatey\\bin", name + ".exe"),
            Path.Combine("C:\\Program Files", name, name + ".exe"),
            Path.Combine("C:\\Program Files (x86)", name, name + ".exe"),
        ];

        foreach (var path in commonPaths)
        {
            if (!File.Exists(path)) continue;

            if (FileSize(path) >= minBytes)
            {
                LogService.Log($"Found {name} at common location: {path}");
                return path;
            }
            LogService.Log($"Skipping undersized {name} at {path} ({FileSize(path):N0} bytes)");
        }

        return null;
    }

    private static List<string> FindOnPath(string name)
    {
        var results = new List<string>();
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "where",
                Arguments = name,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process == null) return results;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            if (process.ExitCode == 0)
            {
                foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmed = line.Trim();
                    if (!string.IsNullOrWhiteSpace(trimmed) && File.Exists(trimmed))
                        results.Add(trimmed);
                }
            }
        }
        catch { }
        return results;
    }

    private static List<string> GetPythonScriptsDirs()
    {
        var dirs = new List<string>();
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        // Roaming pip --user installs: %APPDATA%/Python/PythonXYZ/Scripts
        try
        {
            var pythonRoaming = Path.Combine(appData, "Python");
            if (Directory.Exists(pythonRoaming))
            {
                foreach (var d in Directory.GetDirectories(pythonRoaming))
                {
                    var scripts = Path.Combine(d, "Scripts");
                    if (Directory.Exists(scripts))
                        dirs.Add(scripts);
                }
            }
        }
        catch { }

        // Local pip installs: %LOCALAPPDATA%/Programs/Python/PythonXYZ/Scripts
        try
        {
            var pythonLocal = Path.Combine(localAppData, "Programs", "Python");
            if (Directory.Exists(pythonLocal))
            {
                foreach (var d in Directory.GetDirectories(pythonLocal))
                {
                    var scripts = Path.Combine(d, "Scripts");
                    if (Directory.Exists(scripts))
                        dirs.Add(scripts);
                }
            }
        }
        catch { }

        // System Python installs: C:\PythonXYZ\Scripts
        try
        {
            foreach (var d in Directory.GetDirectories("C:\\", "Python*"))
            {
                var scripts = Path.Combine(d, "Scripts");
                if (Directory.Exists(scripts))
                    dirs.Add(scripts);
            }
        }
        catch { }

        return dirs;
    }
}
