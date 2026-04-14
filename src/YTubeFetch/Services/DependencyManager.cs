using System.Diagnostics;
using System.IO;
using System.Text;

namespace YTubeFetch.Services;

public static class DependencyManager
{
    private static readonly string BinDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "YTubeFetch", "bin");

    public static string? YtDlpPath { get; private set; }
    public static string? FfmpegPath { get; private set; }

    /// <summary>
    /// Initializes the dependency manager. Checks the local bin folder first,
    /// then discovers from system if missing and copies to bin folder.
    /// </summary>
    public static (string? ytdlpPath, string? ffmpegPath) Initialize()
    {
        Directory.CreateDirectory(BinDir);
        LogService.Log($"DependencyManager: bin dir = {BinDir}");

        var ytdlpBin = Path.Combine(BinDir, "yt-dlp.exe");
        var ffmpegBin = Path.Combine(BinDir, "ffmpeg.exe");

        // yt-dlp: check bin folder, then discover and copy
        if (File.Exists(ytdlpBin) && IsExecutableValid(ytdlpBin))
        {
            YtDlpPath = ytdlpBin;
            LogService.Log($"yt-dlp found in bin: {ytdlpBin}");
        }
        else
        {
            var found = FindExecutable("yt-dlp");
            if (found != null)
            {
                try
                {
                    File.Copy(found, ytdlpBin, true);
                    YtDlpPath = ytdlpBin;
                    LogService.Log($"yt-dlp copied to bin: {found} -> {ytdlpBin}");
                }
                catch (Exception ex)
                {
                    // If copy fails, use the found path directly
                    YtDlpPath = found;
                    LogService.Log($"yt-dlp copy failed ({ex.Message}), using directly: {found}");
                }
            }
            else
            {
                LogService.Log("yt-dlp not found anywhere");
            }
        }

        // ffmpeg: check bin folder, then discover and copy
        if (File.Exists(ffmpegBin) && IsExecutableValid(ffmpegBin))
        {
            FfmpegPath = ffmpegBin;
            LogService.Log($"ffmpeg found in bin: {ffmpegBin}");
        }
        else
        {
            var found = FindExecutable("ffmpeg");
            if (found != null)
            {
                try
                {
                    File.Copy(found, ffmpegBin, true);
                    FfmpegPath = ffmpegBin;
                    LogService.Log($"ffmpeg copied to bin: {found} -> {ffmpegBin}");
                }
                catch (Exception ex)
                {
                    FfmpegPath = found;
                    LogService.Log($"ffmpeg copy failed ({ex.Message}), using directly: {found}");
                }
            }
            else
            {
                LogService.Log("ffmpeg not found anywhere");
            }
        }

        LogService.Log($"DependencyManager result: yt-dlp={YtDlpPath ?? "(not found)"}, ffmpeg={FfmpegPath ?? "(not found)"}");
        return (YtDlpPath, FfmpegPath);
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

    private static bool IsExecutableValid(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            return fi.Length > 1024; // Sanity check — not a stub/corrupted file
        }
        catch
        {
            return false;
        }
    }

    private static string? FindExecutable(string name)
    {
        // 1. System PATH (via where)
        var onPath = FindOnPath(name);
        if (onPath != null) return onPath;

        // 2. Python Scripts locations (pip install --user)
        var candidates = GetPythonScriptsDirs();
        foreach (var dir in candidates)
        {
            var exePath = Path.Combine(dir, name + ".exe");
            if (File.Exists(exePath))
            {
                LogService.Log($"Found {name} in Python Scripts: {exePath}");
                return exePath;
            }
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
            if (File.Exists(path))
            {
                LogService.Log($"Found {name} at common location: {path}");
                return path;
            }
        }

        return null;
    }

    private static string? FindOnPath(string name)
    {
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
            if (process == null) return null;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            if (process.ExitCode == 0)
            {
                var firstLine = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
                if (!string.IsNullOrWhiteSpace(firstLine) && File.Exists(firstLine))
                    return firstLine;
            }
        }
        catch { }
        return null;
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
