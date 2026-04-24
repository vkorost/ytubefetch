namespace YTubeFetch.Models;

public enum DownloadType
{
    Video,
    Audio,
    Subtitles
}

public enum JobStatus
{
    Queued,
    Running,
    Completed,
    Error
}

public class DownloadJob
{
    public string Url { get; set; } = string.Empty;
    public DownloadType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public JobStatus Status { get; set; } = JobStatus.Queued;
    public string? ErrorMessage { get; set; }
    public string? OutputPath { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? UploadDate { get; set; }
    public string? Description { get; set; }
    public bool IsBatch { get; set; }
    public List<string> BatchUrls { get; set; } = new();
    public int CurrentIndex { get; set; }
    public int TotalCount { get; set; } = 1;
}
