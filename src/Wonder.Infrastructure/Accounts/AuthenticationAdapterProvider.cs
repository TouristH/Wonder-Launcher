// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using System.Security.Cryptography;
using Wonder.Application;
using Wonder.Core;

namespace Wonder.Infrastructure;

public sealed class AuthenticationAdapterProvider(string directory, HttpClient? httpClient = null, string? expectedSha256 = null) : IAuthenticationAdapterProvider
{
    public const string Version = "1.2.8";
    public const string OfficialSha256 = "9C7F4343E6C82034958FFB48C14A2CB0C85928BE7283103CE17DA00C6D5A7B10";
    private const string Download = "https://authlib-injector.yushi.moe/artifact/56/authlib-injector-1.2.8.jar";
    private static readonly HttpClient DefaultHttp = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(60) };
    private readonly SemaphoreSlim _gate = new(1, 1);
    public async Task<AuthenticationAdapter> EnsureAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        string? temporary = null;
        try
        {
            var hash = expectedSha256 ?? OfficialSha256;
            if (hash.Length != 64 || !hash.All(char.IsAsciiHexDigit))
                throw new AccountAuthenticationException("第三方登录组件的固定校验值无效。");
            var path = Path.Combine(Path.GetFullPath(directory), "authlib-injector-" + Version + ".jar");
            if (File.Exists(path))
            {
                await using var cached = File.OpenRead(path);
                if (cached.Length is > 0 and <= 64 * 1024 * 1024 && Convert.ToHexString(await SHA256.HashDataAsync(cached, ct)).Equals(hash, StringComparison.OrdinalIgnoreCase)) return new(path, hash);
            }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            using var response = await (httpClient ?? DefaultHttp).GetAsync(Download, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode != System.Net.HttpStatusCode.OK) throw new AccountAuthenticationException("第三方登录组件下载失败，请检查网络后重试。");
            var bytes = await AccountHttp.ReadBoundedAsync(response.Content, 64 * 1024 * 1024, timeout.Token).ConfigureAwait(false);
            if (bytes.Length == 0 || !Convert.ToHexString(SHA256.HashData(bytes)).Equals(hash, StringComparison.OrdinalIgnoreCase))
                throw new AccountAuthenticationException("第三方登录组件校验失败，已阻止使用此下载文件。");
            ct.ThrowIfCancellationRequested(); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllBytesAsync(temporary, bytes, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested(); File.Move(temporary, path, true);
            return new(path, hash);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new AccountAuthenticationException("第三方登录组件下载超时，请重试。"); }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException)
        { throw new AccountAuthenticationException("无法准备第三方登录组件，请检查网络和数据目录权限。"); }
        finally
        {
            try { if (temporary is not null && File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* A failed cleanup must not keep the operation gate locked. */ }
            _gate.Release();
        }
    }
}
