#if ENABLE_MCP
using System.Net;
using System.Text;
using System.Text.Json;
using System.Windows;
using YTubeFetch.Services;

namespace YTubeFetch.Mcp;

/// <summary>
/// Embedded HTTP server exposing the YTubeFetcher UI and state over localhost REST endpoints.
/// Starts on a background thread; all WPF access is marshalled via Dispatcher.Invoke.
/// </summary>
public sealed class McpServer : IDisposable
{
    // Candidate ports — tried in order; first available wins.
    private static readonly int[] CandidatePorts = [9222, 9223, 9224];

    private readonly Window _mainWindow;
    private readonly DownloadService _downloadService;
    private readonly DateTime _startTime = DateTime.UtcNow;

    private HttpListener? _listener;
    private Thread? _listenerThread;
    private int _port;
    private volatile bool _running;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public McpServer(Window mainWindow, DownloadService downloadService)
    {
        _mainWindow = mainWindow;
        _downloadService = downloadService;
    }

    // -----------------------------------------------------------------------
    // Lifecycle
    // -----------------------------------------------------------------------

    public void Start()
    {
        foreach (int port in CandidatePorts)
        {
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://localhost:{port}/");
            try
            {
                listener.Start();
                _listener = listener;
                _port = port;
                break;
            }
            catch (HttpListenerException ex)
            {
                Debug($"Port {port} unavailable: {ex.Message}");
                listener.Close();
            }
        }

        if (_listener == null)
        {
            Debug("MCP server: all ports unavailable — not starting.");
            return;
        }

        _running = true;
        _listenerThread = new Thread(ListenLoop)
        {
            IsBackground = true,
            Name = "McpServerListener"
        };
        _listenerThread.Start();

        Debug($"MCP server listening on http://localhost:{_port}/");
    }

    public void Stop()
    {
        _running = false;
        try { _listener?.Stop(); } catch { }
        Debug("MCP server stopped.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _listener?.Close();
        _listener = null;
    }

    // -----------------------------------------------------------------------
    // Main listener loop (background thread)
    // -----------------------------------------------------------------------

    private void ListenLoop()
    {
        while (_running && _listener is { IsListening: true })
        {
            HttpListenerContext? ctx = null;
            try
            {
                ctx = _listener.GetContext(); // blocks until a request arrives
            }
            catch (HttpListenerException) when (!_running)
            {
                break; // normal shutdown
            }
            catch (Exception ex)
            {
                Debug($"GetContext error: {ex.Message}");
                break;
            }

            // Handle each request on a thread-pool thread so the next request can be accepted
            ThreadPool.QueueUserWorkItem(_ => HandleRequest(ctx));
        }
    }

    // -----------------------------------------------------------------------
    // Request dispatch
    // -----------------------------------------------------------------------

    private void HandleRequest(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var resp = ctx.Response;

        try
        {
            Debug($"MCP {req.HttpMethod} {req.Url?.PathAndQuery}");

            string path = req.Url?.AbsolutePath.TrimEnd('/') ?? "/";

            // Route
            if (req.HttpMethod == "GET" && path == "/health")
                HandleHealth(ctx);
            else if (req.HttpMethod == "GET" && path == "/tree")
                HandleTree(ctx);
            else if (req.HttpMethod == "GET" && path.StartsWith("/component/"))
                HandleComponent(ctx, path["/component/".Length..]);
            else if (req.HttpMethod == "POST" && path == "/action")
                HandleAction(ctx);
            else if (req.HttpMethod == "GET" && path == "/screenshot")
                HandleScreenshot(ctx);
            else if (req.HttpMethod == "GET" && path == "/state")
                HandleState(ctx);
            else
                WriteJson(resp, HttpStatusCode.NotFound, new ErrorResponse("Not found", path));
        }
        catch (Exception ex)
        {
            Debug($"Unhandled exception in MCP handler: {ex}");
            try { WriteJson(resp, HttpStatusCode.InternalServerError, new ErrorResponse("Internal error", ex.Message)); }
            catch { }
        }
    }

    // -----------------------------------------------------------------------
    // GET /health
    // -----------------------------------------------------------------------

    private void HandleHealth(HttpListenerContext ctx)
    {
        var result = DispatchUi(() =>
        {
            int componentCount = CountComponents();
            double uptime = (DateTime.UtcNow - _startTime).TotalSeconds;
            return new HealthResponse("ok", "YTubeFetcher", uptime, componentCount);
        });

        WriteJson(ctx.Response, HttpStatusCode.OK, result);
    }

    private int CountComponents()
    {
        var (nodes, _) = TreeWalker.BuildTree(_mainWindow as Window ?? (Window)_mainWindow,
            interactableOnly: false, typeFilter: null, maxCount: int.MaxValue);
        return nodes.Count;
    }

    // -----------------------------------------------------------------------
    // GET /tree
    // -----------------------------------------------------------------------

    private void HandleTree(HttpListenerContext ctx)
    {
        var qs = ctx.Request.QueryString;
        bool interactable = !string.Equals(qs["interactable"], "false", StringComparison.OrdinalIgnoreCase);
        HashSet<string>? typeFilter = null;
        if (!string.IsNullOrWhiteSpace(qs["type"]))
        {
            typeFilter = new HashSet<string>(
                qs["type"]!.Split(',', StringSplitOptions.RemoveEmptyEntries),
                StringComparer.OrdinalIgnoreCase);
        }

        var result = DispatchUi(() =>
        {
            var window = (Window)_mainWindow;
            var (nodes, totalFound) = TreeWalker.BuildTree(window, interactable, typeFilter, 200);
            return new TreeResponse(
                Components: nodes.ToArray(),
                Truncated: totalFound > nodes.Count,
                TotalComponents: totalFound);
        });

        WriteJson(ctx.Response, HttpStatusCode.OK, result);
    }

    // -----------------------------------------------------------------------
    // GET /component/{nameOrId}
    // -----------------------------------------------------------------------

    private void HandleComponent(HttpListenerContext ctx, string nameOrId)
    {
        if (string.IsNullOrWhiteSpace(nameOrId))
        {
            WriteJson(ctx.Response, HttpStatusCode.BadRequest, new ErrorResponse("Missing name or id"));
            return;
        }

        ComponentDetail? detail = null;
        bool found = false;

        var exc = DispatchUiSafe(() =>
        {
            var window = (Window)_mainWindow;
            FrameworkElement? fe = int.TryParse(nameOrId, out int id)
                ? TreeWalker.FindById(window, id)
                : TreeWalker.FindByName(window, nameOrId);

            if (fe == null) return;
            found = true;
            detail = ComponentInspector.Inspect(fe);
        });

        if (exc != null)
        {
            WriteJson(ctx.Response, HttpStatusCode.ServiceUnavailable, new ErrorResponse("App shutting down", exc.Message));
            return;
        }

        if (!found || detail == null)
        {
            WriteJson(ctx.Response, HttpStatusCode.NotFound, new ErrorResponse($"Component not found: {nameOrId}"));
            return;
        }

        WriteJson(ctx.Response, HttpStatusCode.OK, detail);
    }

    // -----------------------------------------------------------------------
    // POST /action
    // -----------------------------------------------------------------------

    private void HandleAction(HttpListenerContext ctx)
    {
        ActionRequest? req;
        try
        {
            using var reader = new System.IO.StreamReader(ctx.Request.InputStream, Encoding.UTF8);
            string body = reader.ReadToEnd();
            req = JsonSerializer.Deserialize<ActionRequest>(body, JsonOptions);
        }
        catch (Exception ex)
        {
            WriteJson(ctx.Response, HttpStatusCode.BadRequest,
                new ActionResponse(false, "unknown", "unknown", null, $"Invalid JSON: {ex.Message}"));
            return;
        }

        if (req == null || string.IsNullOrWhiteSpace(req.Action) || string.IsNullOrWhiteSpace(req.Target))
        {
            WriteJson(ctx.Response, HttpStatusCode.BadRequest,
                new ActionResponse(false, req?.Action ?? "", req?.Target ?? "", null, "Missing 'action' or 'target'"));
            return;
        }

        string? errorMsg = null;
        var exc = DispatchUiSafe(() =>
        {
            try
            {
                ActionExecutor.Execute(req, (Window)_mainWindow);
            }
            catch (Exception ex)
            {
                errorMsg = ex.Message;
            }
        });

        if (exc != null)
        {
            WriteJson(ctx.Response, HttpStatusCode.ServiceUnavailable,
                new ActionResponse(false, req.Action, req.Target, null, "App shutting down"));
            return;
        }

        if (errorMsg != null)
        {
            WriteJson(ctx.Response, HttpStatusCode.BadRequest,
                new ActionResponse(false, req.Action, req.Target, null, errorMsg));
            return;
        }

        // 100ms settle delay for WPF event propagation
        Thread.Sleep(100);

        // Read back the target element's state
        Dictionary<string, object?>? componentState = null;
        DispatchUiSafe(() =>
        {
            var window = (Window)_mainWindow;
            FrameworkElement? fe = int.TryParse(req.Target, out int id)
                ? TreeWalker.FindById(window, id)
                : TreeWalker.FindByName(window, req.Target);
            if (fe != null)
            {
                var detail = ComponentInspector.Inspect(fe);
                componentState = new Dictionary<string, object?>
                {
                    ["type"] = detail.Type,
                    ["visible"] = detail.Visible,
                    ["enabled"] = detail.Enabled,
                    ["text"] = TreeWalker.ExtractText(fe)
                };
                if (detail.Extra != null)
                    foreach (var kv in detail.Extra)
                        componentState[kv.Key] = kv.Value;
            }
        });

        WriteJson(ctx.Response, HttpStatusCode.OK,
            new ActionResponse(true, req.Action, req.Target, componentState, null));
    }

    // -----------------------------------------------------------------------
    // GET /screenshot
    // -----------------------------------------------------------------------

    private void HandleScreenshot(HttpListenerContext ctx)
    {
        string? component = ctx.Request.QueryString["component"];

        ScreenshotResponse? result = null;
        string? errorMsg = null;

        var exc = DispatchUiSafe(() =>
        {
            try
            {
                result = ScreenshotCapture.Capture((Window)_mainWindow, component);
            }
            catch (Exception ex)
            {
                errorMsg = ex.Message;
            }
        });

        if (exc != null)
        {
            WriteJson(ctx.Response, HttpStatusCode.ServiceUnavailable, new ErrorResponse("App shutting down"));
            return;
        }

        if (errorMsg != null)
        {
            WriteJson(ctx.Response, HttpStatusCode.InternalServerError, new ErrorResponse(errorMsg));
            return;
        }

        WriteJson(ctx.Response, HttpStatusCode.OK, result!);
    }

    // -----------------------------------------------------------------------
    // GET /state
    // -----------------------------------------------------------------------

    private void HandleState(HttpListenerContext ctx)
    {
        AppStateResponse? result = null;
        var exc = DispatchUiSafe(() =>
        {
            result = AppStateReader.Read((Window)_mainWindow, _downloadService);
        });

        if (exc != null)
        {
            WriteJson(ctx.Response, HttpStatusCode.ServiceUnavailable, new ErrorResponse("App shutting down"));
            return;
        }

        WriteJson(ctx.Response, HttpStatusCode.OK, result!);
    }

    // -----------------------------------------------------------------------
    // UI dispatch helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Synchronously dispatches to the UI thread and returns the result.
    /// Throws if the app is shutting down.
    /// </summary>
    private T DispatchUi<T>(Func<T> func)
    {
        return Application.Current.Dispatcher.Invoke(func);
    }

    /// <summary>
    /// Synchronously dispatches to the UI thread; swallows shutdown exceptions
    /// and returns the exception (null if successful).
    /// </summary>
    private Exception? DispatchUiSafe(Action action)
    {
        try
        {
            Application.Current.Dispatcher.Invoke(action);
            return null;
        }
        catch (Exception ex) when (
            ex is TaskCanceledException ||
            ex is OperationCanceledException ||
            (ex.GetType().Name.Contains("InvalidOperationException") && ex.Message.Contains("shut")))
        {
            return ex;
        }
    }

    // -----------------------------------------------------------------------
    // Response helpers
    // -----------------------------------------------------------------------

    private static void WriteJson<T>(HttpListenerResponse resp, HttpStatusCode status, T value)
    {
        try
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
            resp.StatusCode = (int)status;
            resp.ContentType = "application/json; charset=utf-8";
            resp.ContentLength64 = bytes.Length;
            resp.OutputStream.Write(bytes, 0, bytes.Length);
        }
        catch { /* client disconnected */ }
        finally
        {
            try { resp.Close(); } catch { }
        }
    }

    private static void Debug(string msg) =>
        System.Diagnostics.Debug.WriteLine($"[McpServer] {msg}");
}
#endif
