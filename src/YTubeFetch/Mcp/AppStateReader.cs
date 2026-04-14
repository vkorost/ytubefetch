#if ENABLE_MCP
using System.Windows;
using System.Windows.Controls;
using YTubeFetch.Models;
using YTubeFetch.Services;

namespace YTubeFetch.Mcp;

/// <summary>
/// Reads YTubeFetcher-specific application state for the /state endpoint.
/// Accesses both the service layer directly (DownloadService, ConfigService)
/// and a small set of named UI elements for status bar text.
/// Must be called on the UI thread.
/// </summary>
internal static class AppStateReader
{
    public static AppStateResponse Read(Window mainWindow, DownloadService downloadService)
    {
        // --- Service-layer state (authoritative) ---
        var config = downloadService.Config;
        var currentJob = downloadService.CurrentJob;
        var queuedJobs = downloadService.QueuedJobs;

        // Build active jobs list (the currently executing job, if any)
        var activeJobInfos = new List<JobStateInfo>();
        if (currentJob != null)
            activeJobInfos.Add(MapJob(currentJob));

        // Build queued jobs list
        var queuedJobInfos = queuedJobs.Select(MapJob).ToArray();

        // Count completed / errored from all jobs observable collection via UI
        int completedCount = 0;
        int errorCount = 0;

        // Read the job list from the ListBox named "JobList"
        var jobList = mainWindow.FindName("JobList") as ListBox;
        if (jobList != null)
        {
            foreach (var item in jobList.Items)
            {
                // JobViewModel has a .Job property of type DownloadJob
                var jobProp = item?.GetType().GetProperty("Job");
                if (jobProp?.GetValue(item) is DownloadJob dj)
                {
                    if (dj.Status == JobStatus.Completed) completedCount++;
                    else if (dj.Status == JobStatus.Error) errorCount++;
                }
            }
        }

        // --- UI state (status bar text, operation running) ---
        string statusBarText = "Ready";
        var statusText = mainWindow.FindName("StatusText") as TextBlock;
        if (statusText != null)
            statusBarText = statusText.Text ?? "Ready";

        bool isRunning = currentJob != null;

        // Default download folder (video folder as representative)
        string downloadFolder = config.GetDownloadFolder(DownloadType.Video);

        return new AppStateResponse(
            DownloadFolder: downloadFolder,
            ActiveJobs: activeJobInfos.ToArray(),
            QueuedJobs: queuedJobInfos,
            CompletedCount: completedCount,
            ErrorCount: errorCount,
            StatusBarText: statusBarText,
            IsOperationRunning: isRunning);
    }

    private static JobStateInfo MapJob(DownloadJob job)
    {
        return new JobStateInfo(
            Title: job.Title ?? string.Empty,
            Type: job.Type.ToString().ToLowerInvariant(),
            Status: job.Status.ToString().ToLowerInvariant(),
            Progress: 0.0, // DownloadService doesn't expose per-job progress; overall progress is in ProgressBar
            Url: job.Url,
            OutputPath: job.OutputPath);
    }
}
#endif
