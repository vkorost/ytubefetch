using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;

namespace YTubeFetch.Services;

public static class LogService
{
    private static readonly string LogPath;
    private static readonly object Lock = new();

    /// <summary>
    /// Fired after a line has been written to the log file. The argument is the
    /// fully formatted line (with timestamp, no trailing newline). Handlers may
    /// be invoked on any thread — marshal to the UI thread if required.
    /// </summary>
    public static event Action<string>? LogWritten;

    static LogService()
    {
        // For single-file apps, AppContext.BaseDirectory points to temp extraction dir.
        // Use the actual exe location instead.
        var exePath = Environment.ProcessPath;
        var exeDir = exePath != null ? Path.GetDirectoryName(exePath) : AppContext.BaseDirectory;
        exeDir ??= AppContext.BaseDirectory;

        LogPath = Path.Combine(exeDir, "ytubefetch.log");

        // Each run starts fresh
        try { File.WriteAllText(LogPath, ""); } catch { }

        Log($"=== YTubeFetch started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
        Log($"ExePath: {exePath}");
        Log($"LogPath: {LogPath}");
        Log($"BaseDirectory: {AppContext.BaseDirectory}");
        Log($"OS: {Environment.OSVersion}");
        Log($".NET: {Environment.Version}");
    }

    public static void Log(string message)
    {
        var formatted = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        lock (Lock)
        {
            try
            {
                File.AppendAllText(LogPath, $"{formatted}{Environment.NewLine}");
            }
            catch { }
        }
        try { LogWritten?.Invoke(formatted); } catch { }
    }

    /// <summary>
    /// Returns the current contents of the log file (or empty string on error).
    /// Used by the UI to pre-populate the live log panel.
    /// </summary>
    public static string GetLogContent()
    {
        lock (Lock)
        {
            try { return File.ReadAllText(LogPath); }
            catch { return string.Empty; }
        }
    }

    public static void Error(string message, Exception? ex = null,
        [CallerMemberName] string caller = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        var source = $"{Path.GetFileName(file)}:{line} ({caller})";
        Log($"ERROR [{source}] {message}");
        if (ex != null)
        {
            Log($"  Exception: {ex.GetType().Name}: {ex.Message}");
            Log($"  StackTrace: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                Log($"  Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                Log($"  Inner StackTrace: {ex.InnerException.StackTrace}");
            }
        }
    }
}
