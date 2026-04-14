#if ENABLE_MCP
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace YTubeFetch.Mcp;

/// <summary>
/// Captures the WPF visual tree as a PNG encoded in base64.
/// Must be called on the UI thread.
/// </summary>
internal static class ScreenshotCapture
{
    /// <summary>
    /// Captures the full MainWindow or an optional named child element.
    /// Returns a ScreenshotResponse with a data URI.
    /// </summary>
    public static ScreenshotResponse Capture(Window mainWindow, string? componentName = null)
    {
        FrameworkElement target = mainWindow;

        if (!string.IsNullOrWhiteSpace(componentName))
        {
            var found = TreeWalker.FindByName(mainWindow, componentName);
            if (found != null)
                target = found;
        }

        // RenderTargetBitmap works with device-independent pixels.
        // Use the element's ActualWidth/ActualHeight.
        double dpi = 96.0;
        int pixelWidth = (int)Math.Ceiling(target.ActualWidth);
        int pixelHeight = (int)Math.Ceiling(target.ActualHeight);

        if (pixelWidth <= 0 || pixelHeight <= 0)
            throw new InvalidOperationException(
                $"Element '{componentName ?? "MainWindow"}' has zero size ({pixelWidth}x{pixelHeight}).");

        var rtb = new RenderTargetBitmap(pixelWidth, pixelHeight, dpi, dpi, PixelFormats.Pbgra32);
        rtb.Render(target);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));

        using var ms = new MemoryStream();
        encoder.Save(ms);
        string base64 = Convert.ToBase64String(ms.ToArray());

        return new ScreenshotResponse(
            Image: $"data:image/png;base64,{base64}",
            Width: pixelWidth,
            Height: pixelHeight);
    }
}
#endif
