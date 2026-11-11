// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using System.Net;
using System.Net.Http.Headers;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Wonder.Core;

namespace Wonder.Desktop;

internal sealed class ResourceIconView : Border
{
    private const int MaximumIconBytes = 2 * 1024 * 1024;
    private const int CacheCapacityBytes = 16 * 1024 * 1024;
    private static readonly HttpClient Http = new(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false });
    private static readonly SemaphoreSlim Requests = new(4);
    private static readonly Dictionary<string, byte[]> Cache = new(StringComparer.Ordinal);
    private static int _cacheBytes;
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock _fallback = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Palette.Accent };
    private string? _url;
    private Bitmap? _bitmap;
    private CancellationTokenSource? _loading;
    private bool _attached;

    public ResourceIconView(double size)
    {
        Width = Height = size;
        CornerRadius = new CornerRadius(8);
        ClipToBounds = true;
        Background = Palette.Active;
        _fallback.FontSize = size * .34;
        Child = new Grid { Children = { _fallback, _image } };
        IsHitTestVisible = false;
        AttachedToVisualTree += (_, _) => { _attached = true; Refresh(); };
        DetachedFromVisualTree += (_, _) => { _attached = false; Refresh(); };
    }

    public void SetProject(ResourceProject? project)
    {
        _fallback.Text = project?.Kind switch { DownloadResourceKind.Modpack => "包", DownloadResourceKind.ResourcePack => "纹", DownloadResourceKind.Shader => "光", _ => "模" };
        if (_url == project?.IconUrl) return;
        _url = project?.IconUrl;
        Refresh();
    }

    private void Refresh()
    {
        _loading?.Cancel(); _loading = null;
        _image.Source = null;
        _bitmap?.Dispose(); _bitmap = null;
        _fallback.IsVisible = true;
        if (_attached && TryGetIconUri(_url, out var uri)) _ = LoadAsync(uri);
    }

    internal static bool TryGetIconUri(string? url, out Uri uri)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var parsed) && parsed.Scheme == "https" && parsed.IsDefaultPort && parsed.UserInfo.Length == 0 &&
            parsed.Host is "cdn.modrinth.com" or "cdn-raw.modrinth.com")
        { uri = parsed; return true; }
        uri = null!; return false;
    }

    private async Task LoadAsync(Uri uri)
    {
        var request = _loading = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        try
        {
            var bytes = await ReadIconAsync(uri, request.Token);
            if (bytes is null || request.IsCancellationRequested) return;
            using var encoded = SkiaSharp.SKData.CreateCopy(bytes);
            using var codec = SkiaSharp.SKCodec.Create(encoded);
            if (codec is null || codec.Info.Width is <= 0 or > 2048 || codec.Info.Height is <= 0 or > 2048) return;
            // The codec can consume or close its input stream while reading WebP headers.
            using var input = new MemoryStream(bytes, writable: false);
            _bitmap = Bitmap.DecodeToWidth(input, Math.Min(144, codec.Info.Width));
            _image.Source = _bitmap;
            _fallback.IsVisible = false;
        }
        catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException or InvalidOperationException or ArgumentException or NotSupportedException)
        { /* The category mark remains visible when an icon cannot load. */ }
        finally
        {
            if (ReferenceEquals(_loading, request)) _loading = null;
            request.Dispose();
        }
    }

    private static async Task<byte[]?> ReadIconAsync(Uri uri, CancellationToken ct)
    {
        var key = uri.AbsoluteUri;
        lock (Cache) if (Cache.TryGetValue(key, out var existing)) return existing;
        await Requests.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            lock (Cache) if (Cache.TryGetValue(key, out var existing)) return existing;
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("WonderLauncher", LauncherBuildInfo.Version));
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("(+https://github.com/TouristH/Wonder-Launcher)"));
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentLength > MaximumIconBytes) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var data = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                if (data.Length + count > MaximumIconBytes) return null;
                data.Write(buffer, 0, count);
            }
            if (data.Length == 0) return null;
            var bytes = data.ToArray();
            lock (Cache)
            {
                if (Cache.TryGetValue(key, out var existing)) return existing;
                while (_cacheBytes + bytes.Length > CacheCapacityBytes || Cache.Count >= 128)
                {
                    var oldest = Cache.First();
                    Cache.Remove(oldest.Key);
                    _cacheBytes -= oldest.Value.Length;
                }
                Cache.Add(key, bytes);
                _cacheBytes += bytes.Length;
            }
            return bytes;
        }
        finally { Requests.Release(); }
    }
}
