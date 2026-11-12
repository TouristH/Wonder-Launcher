// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Wonder.Application;
using Wonder.Core;

namespace Wonder.Infrastructure;

/// <summary>
/// Bounded multi-file and HTTP Range transfers with verified, resumable temporary files.
/// Independently implemented; no PCL, HMCL, or Axolotl source is incorporated.
/// </summary>
public sealed class DownloadEngine : IDownloadEngine, IDisposable
{
    private const int BufferSize = 64 * 1024;
    private const long SegmentThreshold = 2 * 1024 * 1024;
    private const int MaximumMetadataBytes = 32 * 1024 * 1024;
    private const long MaximumFileBytes = 8L * 1024 * 1024 * 1024;
    private const int MaximumRedirects = 5;
    private readonly TimeSpan _requestTimeout;
    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private readonly SemaphoreSlim _connections = new(32);
    private readonly SemaphoreSlim[] _destinations = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1)).ToArray();
    private bool _disposed;

    public DownloadEngine() : this(null) { }

    internal DownloadEngine(HttpClient? http, TimeSpan? requestTimeout = null)
    {
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(30);
        if (_requestTimeout <= TimeSpan.Zero || _requestTimeout > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(requestTimeout), "下载超时必须大于零且不超过五分钟。");
        _ownsClient = http is null;
        if (http is not null) { _http = http; return; }
        var handler = new TextureConnectionPolicy().CreateHandler(allowPrivateNetwork: false);
        handler.MaxConnectionsPerServer = 32;
        handler.PooledConnectionLifetime = TimeSpan.FromMinutes(5);
        _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public async Task<string> GetStringAsync(string url, DownloadOptions options, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); options.Validate();
        var source = DownloadSourceResolver.RequireUrl(url);
        if (source.Host == DownloadSourceResolver.MirrorHost)
            throw new InvalidDataException("下载元数据必须来自官方端点。");
        Exception? last = null;
        for (var attempt = 0; attempt <= options.RetryCount; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await _connections.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(_requestTimeout);
                    using var response = await SendFollowingRedirectsAsync(source, null, metadata: true, timeout.Token).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    if (response.Content.Headers.ContentLength > MaximumMetadataBytes) throw new InvalidDataException("下载元数据超过大小限制。");
                    await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                    using var memory = new MemoryStream();
                    var buffer = new byte[BufferSize];
                    int count;
                    while ((count = await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) != 0)
                    {
                        if (memory.Length + count > MaximumMetadataBytes) throw new InvalidDataException("下载元数据超过大小限制。");
                        memory.Write(buffer, 0, count);
                    }
                    return Encoding.UTF8.GetString(memory.GetBuffer(), 0, checked((int)memory.Length)).TrimStart('\uFEFF');
                }
                finally { _connections.Release(); }
            }
            catch (Exception ex) when (IsRetryable(ex, ct)) { last = ex; }
            if (attempt < options.RetryCount) await RetryDelay(attempt, ct).ConfigureAwait(false);
        }
        throw Failure(last!);
    }

    public async Task DownloadAsync(DownloadFile file, DownloadOptions options, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); options.Validate(); Validate(file);
        var destination = Path.GetFullPath(file.Destination);
        var mutex = _destinations[(StringComparer.OrdinalIgnoreCase.GetHashCode(destination) & int.MaxValue) % _destinations.Length];
        await mutex.WaitAsync(ct).ConfigureAwait(false);
        try { await DownloadCoreAsync(file with { Destination = destination }, options, progress, ct).ConfigureAwait(false); }
        finally { mutex.Release(); }
    }

    private async Task DownloadCoreAsync(DownloadFile file, DownloadOptions options, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var sources = DownloadSourceResolver.Resolve(file.Url, options.Source);
        var tracker = new TransferProgress(Path.GetFileName(file.Destination), file.Size, progress);
        if (File.Exists(file.Destination) && await IsValidAsync(file.Destination, file, ct).ConfigureAwait(false))
        { tracker.Finish(new FileInfo(file.Destination).Length); return; }

        Directory.CreateDirectory(Path.GetDirectoryName(file.Destination)!);
        // The identity binds persisted parts to exact content, not merely a destination filename.
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{file.Url}\n{file.HashAlgorithm}\n{file.Hash}\n{file.Size}")))[..20];
        var temporary = file.Destination + ".wonder-download-" + identity;
        var part = temporary + ".part";
        var segments = GetSegments(file, options, temporary);
        var segmented = segments.Length > 1;
        Exception? last = null;

        for (var attempt = 0; attempt <= options.RetryCount; attempt++)
        {
            foreach (var source in sources)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    // A completed but not yet committed part can survive cancellation during verification.
                    if (!(File.Exists(part) && await IsValidAsync(part, file, ct).ConfigureAwait(false)))
                    {
                        if (segmented)
                        {
                            try { await DownloadSegmentsAsync(source, file, segments, part, tracker, ct).ConfigureAwait(false); }
                            catch (RangeNotSupportedException)
                            {
                                DeleteParts(part, segments); segmented = false;
                                await DownloadSingleAsync(source, file, part, tracker, ct).ConfigureAwait(false);
                            }
                        }
                        else await DownloadSingleAsync(source, file, part, tracker, ct).ConfigureAwait(false);
                    }
                    if (!await IsValidAsync(part, file, ct).ConfigureAwait(false))
                    {
                        DeleteParts(part, segments);
                        throw new InvalidDataException("下载文件的长度或校验值不符，请重试或切换下载源。");
                    }
                    ct.ThrowIfCancellationRequested();
                    var length = new FileInfo(part).Length;
                    // Same-directory rename commits only after every byte has been verified.
                    File.Move(part, file.Destination, overwrite: true);
                    foreach (var segment in segments) File.Delete(segment.Path);
                    tracker.Finish(length); return;
                }
                catch (Exception ex) when (IsRetryable(ex, ct)) { last = ex; }
            }
            if (attempt < options.RetryCount) await RetryDelay(attempt, ct).ConfigureAwait(false);
        }
        throw Failure(last!);
    }

    public async Task DownloadManyAsync(IReadOnlyList<DownloadFile> files, DownloadOptions options, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); options.Validate();
        if (files.Count == 0) { progress?.Report(new("下载完成")); return; }
        // Exact duplicates are cheap to coalesce, conflicting writes are caller errors.
        var unique = new Dictionary<string, DownloadFile>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var file in files)
        {
            Validate(file); var path = Path.GetFullPath(file.Destination);
            if (unique.TryGetValue(path, out var previous) && previous != file) throw new ArgumentException("多个下载指向同一目标文件。");
            unique[path] = file;
        }
        var values = unique.Values.ToArray(); var received = new long[values.Length]; var rates = new double[values.Length]; var completed = 0;
        long? total = values.All(x => x.Size.HasValue) ? values.Sum(x => x.Size!.Value) : null;
        var watch = Stopwatch.StartNew(); var gate = new object(); long lastReport = 0;
        void Report(int index, DownloadProgress value, bool finish = false)
        {
            lock (gate)
            {
                received[index] = value.BytesReceived;
                rates[index] = finish ? 0 : value.BytesPerSecond;
                if (finish) completed++;
                if (!finish && watch.ElapsedMilliseconds - lastReport < 100) return;
                lastReport = watch.ElapsedMilliseconds;
                var sum = received.Sum();
                progress?.Report(new(completed == values.Length ? "下载完成" : $"下载文件 {completed}/{values.Length}", sum, total,
                    completed, values.Length, rates.Sum()));
            }
        }
        // Parallel.ForEachAsync cancels its own workers on failure and retains the initiating
        // exception. Cancelling an external token here would hide it behind TaskCanceledException.
        await Parallel.ForEachAsync(Enumerable.Range(0, values.Length), new ParallelOptions { MaxDegreeOfParallelism = options.ConcurrentFiles, CancellationToken = ct }, async (index, token) =>
        {
            DownloadProgress? last = null;
            await DownloadAsync(values[index], options, new CallbackProgress(value => { last = value; Report(index, value); }), token).ConfigureAwait(false);
            Report(index, last ?? new("下载完成"), true);
        }).ConfigureAwait(false);
    }

    private async Task DownloadSingleAsync(string source, DownloadFile file, string part, TransferProgress tracker, CancellationToken ct)
    {
        var offset = File.Exists(part) ? new FileInfo(part).Length : 0;
        if (offset >= (file.Size ?? MaximumFileBytes)) offset = 0;
        tracker.Reset(offset);
        await _connections.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var response = await SendAsync(source, offset > 0 ? new RangeHeaderValue(offset, null) : null, ct).ConfigureAwait(false);
            // A server may drop its Range support between attempts. Restart from the full response.
            if (offset > 0 && response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            { File.Delete(part); throw new IOException("服务器拒绝续传，已重置临时文件。"); }
            response.EnsureSuccessStatusCode();
            if (response.StatusCode == HttpStatusCode.PartialContent)
            {
                var range = response.Content.Headers.ContentRange;
                if (offset == 0 || range?.Unit != "bytes" || range.From != offset || range.To is null ||
                    range.Length is null || range.To != range.Length - 1 || file.Size is { } expected && range.Length != expected)
                { File.Delete(part); throw new InvalidDataException("服务器返回了无效的续传范围。"); }
            }
            else { offset = 0; tracker.Reset(0); }
            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength > MaximumFileBytes - offset)
                throw new InvalidDataException("下载响应超过文件大小限制。");
            var expectedSize = file.Size ?? (contentLength is { } bodyLength ? offset + bodyLength : null);
            if (file.Size is { } size && contentLength is { } content && offset + content != size)
                throw new InvalidDataException("下载响应长度与文件清单不符。");
            await using var target = new FileStream(part, offset > 0 ? FileMode.Open : FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous);
            target.Position = offset;
            await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var read = await CopyAsync(input, target, (expectedSize ?? MaximumFileBytes) - offset, tracker.Add, ct).ConfigureAwait(false);
            if (expectedSize is { } expectedLength && offset + read != expectedLength) throw new IOException("下载连接提前结束，临时文件已保留供续传。");
            await target.FlushAsync(ct).ConfigureAwait(false);
        }
        finally { _connections.Release(); }
    }

    private async Task DownloadSegmentsAsync(string source, DownloadFile file, Segment[] segments, string part, TransferProgress tracker, CancellationToken ct)
    {
        var existing = segments.Sum(segment => Math.Min(segment.Length, File.Exists(segment.Path) ? new FileInfo(segment.Path).Length : 0));
        tracker.Reset(existing);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var tasks = segments.Select(async segment =>
        {
            try
            {
                var offset = File.Exists(segment.Path) ? new FileInfo(segment.Path).Length : 0;
                if (offset == segment.Length) return;
                if (offset > segment.Length) { File.Delete(segment.Path); offset = 0; }
                await _connections.WaitAsync(linked.Token).ConfigureAwait(false);
                try
                {
                    using var response = await SendAsync(source, new RangeHeaderValue(segment.Start + offset, segment.End), linked.Token).ConfigureAwait(false);
                    if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.RequestedRangeNotSatisfiable) throw new RangeNotSupportedException();
                    response.EnsureSuccessStatusCode();
                    var range = response.Content.Headers.ContentRange;
                    if (response.StatusCode != HttpStatusCode.PartialContent || range?.Unit != "bytes" || range.From != segment.Start + offset ||
                        range.To != segment.End || range.Length != file.Size ||
                        response.Content.Headers.ContentLength is { } contentLength && contentLength != segment.Length - offset)
                        throw new RangeNotSupportedException();
                    await using var target = new FileStream(segment.Path, offset == 0 ? FileMode.Create : FileMode.Open, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous);
                    target.Position = offset;
                    await using var input = await response.Content.ReadAsStreamAsync(linked.Token).ConfigureAwait(false);
                    var read = await CopyAsync(input, target, segment.Length - offset, tracker.Add, linked.Token).ConfigureAwait(false);
                    if (offset + read != segment.Length) throw new IOException("分段下载连接提前结束。");
                    await target.FlushAsync(linked.Token).ConfigureAwait(false);
                }
                finally { _connections.Release(); }
            }
            catch { await linked.CancelAsync().ConfigureAwait(false); throw; }
        }).ToArray();
        try { await Task.WhenAll(tasks).ConfigureAwait(false); }
        catch
        {
            ct.ThrowIfCancellationRequested();
            // Preserve the initiating error rather than a sibling's cancellation.
            if (tasks.Any(task => task.Exception?.Flatten().InnerExceptions.Any(e => e is RangeNotSupportedException) == true)) throw new RangeNotSupportedException();
            var failure = tasks.SelectMany(task => task.Exception?.Flatten().InnerExceptions ?? Enumerable.Empty<Exception>()).FirstOrDefault(e => e is not OperationCanceledException);
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            throw;
        }
        await using var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous);
        foreach (var segment in segments)
        {
            await using var input = new FileStream(segment.Path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (await CopyAsync(input, output, segment.Length, null, ct).ConfigureAwait(false) != segment.Length)
                throw new IOException("分段文件长度不符。");
        }
        await output.FlushAsync(ct).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendAsync(string source, RangeHeaderValue? range, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(_requestTimeout);
        try { return await SendFollowingRedirectsAsync(DownloadSourceResolver.RequireUrl(source), range, metadata: false, timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TimeoutException("下载服务器响应超时。"); }
    }

    private async Task<HttpResponseMessage> SendFollowingRedirectsAsync(Uri source, RangeHeaderValue? range, bool metadata, CancellationToken ct)
    {
        var current = source;
        for (var hop = 0; ; hop++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.UserAgent.ParseAdd("WonderLauncher/" + LauncherBuildInfo.Version);
            request.Headers.AcceptEncoding.ParseAdd("identity");
            request.Headers.Range = range;
            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308)) return response;
            using (response)
            {
                if (hop == MaximumRedirects || response.Headers.Location is not { } location ||
                    !Uri.TryCreate(current, location, out var target))
                    throw new InvalidDataException("下载重定向无效或次数过多。");
                var next = DownloadSourceResolver.RequireUrl(target.OriginalString);
                if (!DownloadSourceResolver.IsAllowedRedirect(source, next, metadata))
                    throw new InvalidDataException("下载重定向指向未经允许的主机。");
                current = next;
            }
        }
    }

    private async Task<long> CopyAsync(Stream input, Stream output, long maximum, Action<int>? progress, CancellationToken ct)
    {
        var buffer = new byte[BufferSize]; long total = 0;
        while (true)
        {
            int count;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeout.CancelAfter(_requestTimeout);
                try { count = await input.ReadAsync(buffer, timeout.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TimeoutException("下载连接长时间没有返回数据。"); }
            }
            if (count == 0) return total;
            if (count > maximum - total) throw new InvalidDataException("服务器返回的数据超过文件大小限制。");
            await output.WriteAsync(buffer.AsMemory(0, count), ct).ConfigureAwait(false);
            total += count; progress?.Invoke(count);
        }
    }

    private static Segment[] GetSegments(DownloadFile file, DownloadOptions options, string temporary)
    {
        if (file.Size is null or < SegmentThreshold || options.ConnectionsPerFile == 1) return [];
        var count = (int)Math.Min(options.ConnectionsPerFile, file.Size.Value / (512 * 1024));
        var length = (file.Size.Value + count - 1) / count;
        return Enumerable.Range(0, count).Select(index =>
        {
            var start = index * length; var end = Math.Min(file.Size.Value - 1, start + length - 1);
            return new Segment(start, end, temporary + $".segment-{start}-{end}");
        }).ToArray();
    }

    private static async Task<bool> IsValidAsync(string path, DownloadFile file, CancellationToken ct)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > MaximumFileBytes || file.Size is { } size && info.Length != size) return false;
        if (file.Hash is null) return false;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using HashAlgorithm algorithm = file.HashAlgorithm.Replace("-", "", StringComparison.Ordinal).ToUpperInvariant() switch
        {
            "MD5" => MD5.Create(), "SHA1" => SHA1.Create(), "SHA256" => SHA256.Create(), "SHA512" => SHA512.Create(),
            _ => throw new ArgumentException("不支持的下载校验算法。")
        };
        var hash = await algorithm.ComputeHashAsync(stream, ct).ConfigureAwait(false);
        return CryptographicOperations.FixedTimeEquals(hash, Convert.FromHexString(file.Hash));
    }

    private static void Validate(DownloadFile file)
    {
        ArgumentNullException.ThrowIfNull(file); DownloadSourceResolver.Resolve(file.Url, DownloadSource.Official);
        if (!Path.IsPathFullyQualified(file.Destination)) throw new ArgumentException("下载目标必须为完整路径。");
        if (file.Size is < 0 or > MaximumFileBytes) throw new InvalidDataException("下载文件大小超出限制。");
        ValidateHash(file);
    }

    internal static void ValidateHash(DownloadFile file)
    {
        if (file.Hash is null) throw new InvalidDataException("下载文件缺少完整性校验值。");
        var length = file.HashAlgorithm.Replace("-", "", StringComparison.Ordinal).ToUpperInvariant() switch { "MD5" => 32, "SHA1" => 40, "SHA256" => 64, "SHA512" => 128, _ => 0 };
        if (length == 0 || file.Hash.Length != length || file.Hash.Any(c => !Uri.IsHexDigit(c))) throw new InvalidDataException("下载校验值或算法无效。");
    }
    private static void DeleteParts(string part, Segment[] segments) { File.Delete(part); foreach (var segment in segments) File.Delete(segment.Path); }
    private static bool IsRetryable(Exception ex, CancellationToken ct) => !ct.IsCancellationRequested && ex is HttpRequestException or IOException or InvalidDataException or TimeoutException or OperationCanceledException;
    private static Exception Failure(Exception last) => last is OperationCanceledException ? new TimeoutException("下载请求超时，请重试或切换下载源。", last) : last;
    private static Task RetryDelay(int attempt, CancellationToken ct) => Task.Delay(TimeSpan.FromMilliseconds(Math.Min(4000, 250 * (1 << attempt))), ct);
    public void Dispose() { if (_disposed) return; _disposed = true; if (_ownsClient) _http.Dispose(); }
    private sealed record Segment(long Start, long End, string Path) { public long Length => End - Start + 1; }
    private sealed class RangeNotSupportedException : IOException;
    private sealed class CallbackProgress(Action<DownloadProgress> report) : IProgress<DownloadProgress> { public void Report(DownloadProgress value) => report(value); }

    private sealed class TransferProgress(string name, long? total, IProgress<DownloadProgress>? progress)
    {
        private readonly object _gate = new();
        private readonly Stopwatch _watch = Stopwatch.StartNew();
        private long _received, _network, _reportedAt;
        public void Reset(long value) { lock (_gate) { _received = value; Publish(); } }
        public void Add(int count)
        {
            lock (_gate)
            {
                _received += count; _network += count;
                if (_watch.ElapsedMilliseconds - _reportedAt >= 100) Publish();
            }
        }
        public void Finish(long length) { lock (_gate) { _received = length; Publish(true); } }
        private void Publish(bool completed = false)
        {
            _reportedAt = _watch.ElapsedMilliseconds;
            progress?.Report(new(completed ? "下载完成" : "下载 " + name, _received, total, completed ? 1 : 0, 1, _network / Math.Max(.1, _watch.Elapsed.TotalSeconds)));
        }
    }
}
