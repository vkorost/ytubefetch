#if ENABLE_MCP
using System.Text.Json.Serialization;

namespace YTubeFetch.Mcp;

// GET /health
public record HealthResponse(
    string Status,
    string App,
    double UptimeSeconds,
    int ComponentCount);

// GET /tree
public record TreeResponse(
    ComponentNode[] Components,
    bool Truncated,
    int TotalComponents);

public record ComponentNode(
    int Id,
    string Type,
    string Name,
    int? Parent,
    BoundsRect Bounds,
    bool Visible,
    bool Enabled,
    bool Focused,
    string? Text,
    int ChildCount);

public record BoundsRect(double X, double Y, double Width, double Height);

// GET /component/{nameOrId}
public record ComponentDetail(
    string Name,
    string Type,
    BoundsRect Bounds,
    bool Visible,
    bool Enabled,
    bool Focused,
    string? Foreground,
    string? Background,
    string? Tooltip,
    string? FontFamily,
    double? FontSize,
    // Type-specific extras stored as a dictionary for flexibility
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    Dictionary<string, object?>? Extra);

// POST /action
public record ActionRequest(
    string Action,
    string Target,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Text,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Value,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? Index,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Path,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Url);

public record ActionResponse(
    bool Success,
    string Action,
    string Target,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    Dictionary<string, object?>? ComponentState,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Error);

// GET /screenshot
public record ScreenshotResponse(
    string Image,
    int Width,
    int Height);

// GET /state
public record AppStateResponse(
    string DownloadFolder,
    JobStateInfo[] ActiveJobs,
    JobStateInfo[] QueuedJobs,
    int CompletedCount,
    int ErrorCount,
    string StatusBarText,
    bool IsOperationRunning);

public record JobStateInfo(
    string Title,
    string Type,
    string Status,
    double Progress,
    string Url,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? OutputPath);

// Generic error response
public record ErrorResponse(string Error, string? Detail = null);
#endif
