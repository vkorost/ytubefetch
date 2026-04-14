using System.IO;
using System.Net.Http;
using System.Windows.Media.Imaging;

namespace YTubeFetch.Services;

public static class ThumbnailService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly Dictionary<string, BitmapImage?> _cache = new();

    public static async Task<BitmapImage?> GetThumbnailAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return null;

        if (_cache.TryGetValue(url, out var cached))
            return cached;

        try
        {
            var bytes = await _http.GetByteArrayAsync(url);
            var bmp = new BitmapImage();
            using (var ms = new MemoryStream(bytes))
            {
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 64;
                bmp.StreamSource = ms;
                bmp.EndInit();
            }
            bmp.Freeze();
            _cache[url] = bmp;
            return bmp;
        }
        catch (Exception ex)
        {
            LogService.Log($"Thumbnail download failed: {ex.Message}");
            _cache[url] = null;
            return null;
        }
    }
}
