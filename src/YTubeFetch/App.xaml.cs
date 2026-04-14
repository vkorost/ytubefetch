using System.IO;
using System.Windows;
using System.Windows.Threading;
using YTubeFetch.Services;
#if ENABLE_MCP
using YTubeFetch.Mcp;
#endif

namespace YTubeFetch;

public partial class App : Application
{
#if ENABLE_MCP
    private McpServer? _mcpServer;
#endif

    public App()
    {
        // Earliest possible crash handling
        var exePath = Environment.ProcessPath;
        var crashLogDir = exePath != null ? Path.GetDirectoryName(exePath) : Environment.CurrentDirectory;
        var crashLogPath = Path.Combine(crashLogDir ?? ".", "ytubefetch-crash.log");

        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                LogService.Error("Unhandled UI exception", args.Exception);
            }
            catch { }
            try
            {
                File.AppendAllText(crashLogPath,
                    $"UI CRASH at {DateTime.Now}:\n{args.Exception}\n\n");
            }
            catch { }
            MessageBox.Show($"An error occurred:\n{args.Exception.Message}", "YTubeFetch Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var msg = args.ExceptionObject is Exception ex
                ? $"{ex}" : args.ExceptionObject?.ToString() ?? "unknown";
            try { File.AppendAllText(crashLogPath, $"DOMAIN CRASH at {DateTime.Now}:\n{msg}\n\n"); } catch { }
            try { LogService.Error("Unhandled domain exception: " + msg); } catch { }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            try { LogService.Error("Unobserved task exception", args.Exception); } catch { }
            args.SetObserved();
        };
    }

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        try
        {
            LogService.Log("Application_Startup: initializing theme");
            ThemeService.Initialize();

            LogService.Log("Application_Startup: creating MainWindow");
            var window = new MainWindow();
            window.Show();

            ThemeService.ApplyDarkTitleBar(window);
            LogService.Log("Application_Startup: MainWindow shown");

#if ENABLE_MCP
            try
            {
                _mcpServer = new McpServer(window, window.DownloadService);
                _mcpServer.Start();
                LogService.Log("MCP server started");
            }
            catch (Exception mcpEx)
            {
                LogService.Error("MCP server failed to start", mcpEx);
                // Do NOT crash the app — MCP is debug-only tooling
            }
#endif
        }
        catch (Exception ex)
        {
            LogService.Error("Failed to create/show MainWindow", ex);
            MessageBox.Show($"YTubeFetch failed to start:\n{ex.Message}\n\nSee ytubefetch.log for details.",
                "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void Application_Exit(object sender, ExitEventArgs e)
    {
#if ENABLE_MCP
        try
        {
            _mcpServer?.Stop();
            _mcpServer?.Dispose();
            _mcpServer = null;
            LogService.Log("MCP server stopped on application exit");
        }
        catch { }
#endif
    }
}
