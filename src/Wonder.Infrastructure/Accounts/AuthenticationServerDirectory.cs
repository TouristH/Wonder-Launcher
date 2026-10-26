// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Wonder.Application;
using Wonder.Core;

namespace Wonder.Infrastructure;

public sealed class AuthenticationServerDirectory(HttpClient? httpClient = null) : IAuthenticationServerDirectory
{
    private static readonly HttpClient DefaultHttp = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(20) };
    public async Task<AuthenticationServer> DiscoverAsync(string address, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(address) || address.Length > 4096 || address.Any(char.IsControl))
            throw new AccountAuthenticationException("请填写有效的 HTTPS 认证服务器地址。");
        address = address.Trim();
        const string prefix = "authlib-injector:yggdrasil-server:";
        if (address.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) address = Uri.UnescapeDataString(address[prefix.Length..]);
        if (!address.Contains("://", StringComparison.Ordinal)) address = "https://" + address;
        var url = YggdrasilAuthenticator.NormalizeServerUrl(address);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            for (var attempt = 0; attempt < 5; attempt++)
            {
                using var response = await (httpClient ?? DefaultHttp).GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                string? location = response.Headers.TryGetValues("X-Authlib-Injector-API-Location", out var locations) ? locations.SingleOrDefault() : null;
                if (location is null && (int)response.StatusCode is >= 300 and < 400) location = response.Headers.Location?.ToString();
                if (location is not null)
                {
                    var target = YggdrasilAuthenticator.NormalizeServerUrl(new Uri(new Uri(url), location).AbsoluteUri);
                    if (target != url) { url = target; continue; }
                }
                if (response.StatusCode != HttpStatusCode.OK) throw new AccountAuthenticationException("未找到可用的认证服务器，请核对网址。");
                var bytes = await AccountHttp.ReadBoundedAsync(response.Content, 256 * 1024, timeout.Token).ConfigureAwait(false);
                using var json = JsonDocument.Parse(bytes, new() { MaxDepth = 16, AllowDuplicateProperties = false });
                var root = json.RootElement;
                if (!root.TryGetProperty("meta", out var meta) || meta.ValueKind != JsonValueKind.Object ||
                    !meta.TryGetProperty("serverName", out var field) || field.ValueKind != JsonValueKind.String)
                    throw new AccountAuthenticationException("该网址未提供 Yggdrasil 服务器信息，请填写其认证 API 地址。");
                var name = field.GetString()!;
                if (string.IsNullOrWhiteSpace(name) || name.Length > 120 || name.Any(char.IsControl)) throw new JsonException();
                string? Link(string key)
                {
                    if (!meta.TryGetProperty("links", out var links) || links.ValueKind != JsonValueKind.Object ||
                        !links.TryGetProperty(key, out var link) || link.ValueKind != JsonValueKind.String) return null;
                    return Uri.TryCreate(link.GetString(), UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.UserInfo.Length == 0 ? uri.AbsoluteUri : null;
                }
                return new(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..32], name, url, Link("homepage"), Link("register"));
            }
            throw new AccountAuthenticationException("认证服务器地址跳转次数过多。");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new AccountAuthenticationException("发现服务器超时，请重试。"); }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException or InvalidDataException or InvalidOperationException or UriFormatException)
        { throw new AccountAuthenticationException("无法安全读取认证服务器信息，请检查网址、网络和 HTTPS 证书。"); }
    }
}

internal static class AccountHttp
{
    public static async Task<byte[]> ReadBoundedAsync(HttpContent content, int limit, CancellationToken ct)
    {
        if (content.Headers.ContentLength > limit) throw new InvalidDataException("响应过大。");
        await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var buffer = new MemoryStream(); var chunk = new byte[8192]; int read;
        while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
        { if (buffer.Length + read > limit) throw new InvalidDataException("响应过大。"); buffer.Write(chunk, 0, read); }
        return buffer.ToArray();
    }
}
