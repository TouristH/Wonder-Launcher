// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Wonder.Application;
using Wonder.Core;

namespace Wonder.Infrastructure;

public sealed partial class MicrosoftAuthenticator
{
    public Task<AccountProfile> UploadSkinAsync(AccountProfile profile, AccountCredentials credentials, byte[] png, string variant, CancellationToken ct = default)
    {
        MinecraftSkinImage.Validate(png);
        if (variant is not ("classic" or "slim")) throw Failure("请选择经典或纤细皮肤模型。");
        var content = new MultipartFormDataContent();
        content.Add(new StringContent(variant), "variant");
        var file = new ByteArrayContent(png); file.Headers.ContentType = new("image/png"); content.Add(file, "file", "skin.png");
        return ChangeAppearance(profile, credentials, HttpMethod.Post, "skins", content, ct);
    }
    public Task<AccountProfile> ResetSkinAsync(AccountProfile profile, AccountCredentials credentials, CancellationToken ct = default) =>
        ChangeAppearance(profile, credentials, HttpMethod.Delete, "skins/active", null, ct);
    public Task<AccountProfile> SetCapeAsync(AccountProfile profile, AccountCredentials credentials, string? capeId, CancellationToken ct = default)
    {
        if (capeId is not null && !profile.Capes.Any(x => x.Id == capeId)) throw Failure("只能选择此账户已拥有的披风。");
        return ChangeAppearance(profile, credentials, capeId is null ? HttpMethod.Delete : HttpMethod.Put,
            "capes/active", capeId is null ? null : JsonContent.Create(new { capeId }), ct);
    }
    private async Task<AccountProfile> ChangeAppearance(AccountProfile profile, AccountCredentials credentials, HttpMethod method, string path, HttpContent? content, CancellationToken ct)
    {
        using (content)
        {
            if (profile.Kind != AccountKind.Microsoft || profile.ServerUrl is not null || credentials.ExpiresAt <= DateTimeOffset.UtcNow)
                throw Failure("请先重新登录微软账户再修改外观。");
            ValidateSecret(credentials.AccessToken);
            try
            {
                using var request = new HttpRequestMessage(method, "https://api.minecraftservices.com/minecraft/profile/" + path) { Content = content };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(30));
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.NoContent))
                    throw Failure("Minecraft 外观修改未确认，请检查账户状态后刷新资料。未向其他站点转发凭据。");
                var data = await GetJson("https://api.minecraftservices.com/minecraft/profile", credentials.AccessToken, "Minecraft 外观读取", ct).ConfigureAwait(false);
                var uuid = RequiredText(data, "id", 32); var name = RequiredText(data, "name", 16);
                if (!string.Equals(uuid, profile.Uuid, StringComparison.OrdinalIgnoreCase) || name.Length < 3 || !name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
                    throw Failure("外观服务返回的身份不匹配，请重新登录。");
                return ReadAppearance(data, profile with { Username = name });
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw Failure("外观操作超时。远端可能已接受修改，请刷新账户核对。"); }
            catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or InvalidOperationException)
            { throw Failure("外观操作未确认，请检查网络并刷新账户核对。"); }
        }
    }
    private static AccountProfile ReadAppearance(JsonElement data, AccountProfile profile)
    {
        string? skinUrl = null; var variant = "classic"; var capes = new List<AccountCape>(); string? activeCape = null;
        if (data.TryGetProperty("skins", out var skins))
        {
            if (skins.ValueKind != JsonValueKind.Array) throw Failure("皮肤资料无效。");
            if (skins.GetArrayLength() > 64) throw Failure("皮肤资料过大。");
            foreach (var skin in skins.EnumerateArray())
                if (OptionalText(skin, "state", 16) == "ACTIVE")
                { skinUrl = OfficialTextureUrl(OptionalText(skin, "url", 2048)); variant = OptionalText(skin, "variant", 16)?.ToLowerInvariant() == "slim" ? "slim" : "classic"; break; }
        }
        if (data.TryGetProperty("capes", out var available))
        {
            if (available.ValueKind != JsonValueKind.Array) throw Failure("披风资料无效。");
            if (available.GetArrayLength() > 128) throw Failure("披风资料过大。");
            foreach (var cape in available.EnumerateArray())
            {
                var id = RequiredText(cape, "id", 64); var url = OfficialTextureUrl(RequiredText(cape, "url", 2048));
                if (!Guid.TryParse(id, out _) || capes.Any(x => x.Id == id) || url is null) throw Failure("披风资料无效。");
                capes.Add(new(id, OptionalText(cape, "alias", 80) ?? "Minecraft 披风", url));
                if (OptionalText(cape, "state", 16) == "ACTIVE") activeCape = id;
            }
        }
        return profile with { SkinUrl = skinUrl, SkinVariant = variant, Capes = capes.AsReadOnly(), ActiveCapeId = activeCape, LastValidatedAt = DateTimeOffset.UtcNow };
    }
    public static string? OfficialTextureUrl(string? value)
    {
        if (value is null) return null;
        if (value.Length > 2048 || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.Host != "textures.minecraft.net" ||
            !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || !uri.AbsolutePath.StartsWith("/texture/", StringComparison.Ordinal))
            throw Failure("Minecraft 返回了不受信任的纹理地址。");
        return new UriBuilder(uri) { Scheme = "https", Port = -1 }.Uri.AbsoluteUri;
    }
}

public static class MinecraftSkinImage
{
    internal const int MaximumStandardBytes = 1024 * 1024;
    internal const int MaximumHighDefinitionBytes = 16 * 1024 * 1024;
    private const int MaximumHighDefinitionWidth = 4096;

    public static void Validate(byte[] png) => ValidatePreview(png, cape: false, allowHighDefinition: false);

    public static void ValidatePreview(byte[] png, bool cape, bool allowHighDefinition)
    {
        var maximumBytes = allowHighDefinition ? MaximumHighDefinitionBytes : MaximumStandardBytes;
        if (png is null || png.Length < 45 || png.Length > maximumBytes ||
            !png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
            BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(8, 4)) != 13 || !png.AsSpan(12, 4).SequenceEqual("IHDR"u8))
            throw InvalidSkin();
        var width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4));
        if (width < 64 || width > (allowHighDefinition ? MaximumHighDefinitionWidth : 64) || width % 64 != 0 ||
            (height != width / 2 && (cape || height != width)))
            throw InvalidSkin();
        // Bound the decoded pixel dimensions before the desktop decoder sees remote data.
        // HD textures retain the standard atlas proportions and scale each UV by width / 64.
        var offset = 8; var sawData = false;
        while (offset <= png.Length - 12)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset, 4));
            if (length < 0 || length > png.Length - offset - 12) throw InvalidSkin();
            var type = png.AsSpan(offset + 4, 4);
            if (offset != 8 && type.SequenceEqual("IHDR"u8)) throw InvalidSkin();
            var expected = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + 8 + length, 4));
            uint crc = uint.MaxValue;
            foreach (var value in png.AsSpan(offset + 4, length + 4))
            {
                crc ^= value;
                for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320u : 0);
            }
            if (~crc != expected) throw InvalidSkin();
            if (type.SequenceEqual("IDAT"u8) && length > 0) sawData = true;
            offset += length + 12;
            if (type.SequenceEqual("IEND"u8))
            { if (length != 0 || !sawData || offset != png.Length) throw InvalidSkin(); return; }
        }
        throw InvalidSkin();

        AccountAuthenticationException InvalidSkin() => new(allowHighDefinition
            ? "请选择完整的 PNG 纹理，宽度为 64 的整数倍且不超过 4096，皮肤比例为 1:1 或 2:1、披风比例为 2:1，文件不超过 16 MiB。"
            : cape ? "请选择完整的 64×32 PNG 披风，文件不超过 1 MiB。"
            : "请选择完整的 64×64 或 64×32 PNG 皮肤，文件不超过 1 MiB。");
    }
}

public sealed class AccountTextureClient(HttpClient? httpClient = null)
{
    // Injected clients are trusted transports for controlled hosts/tests; normal callers use the policy below.
    private readonly TextureConnectionPolicy? _connections;
    internal AccountTextureClient(TextureConnectionPolicy connections) : this((HttpClient?)null) => _connections = connections;
    private static readonly HttpClient DefaultHttp = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly TextureConnectionPolicy ThirdPartyConnections = new();
    private static readonly HttpClient PublicThirdPartyHttp = new(ThirdPartyConnections.CreateHandler(allowPrivateNetwork: false)) { Timeout = TimeSpan.FromSeconds(15) };
    public Task<byte[]> DownloadAsync(string url, CancellationToken ct = default) =>
        DownloadTextureAsync(MicrosoftAuthenticator.OfficialTextureUrl(url)!, MinecraftSkinImage.MaximumStandardBytes, httpClient ?? DefaultHttp, ct);

    public Task<byte[]> DownloadThirdPartyAsync(string url, CancellationToken ct = default) =>
        DownloadThirdPartyAsync(url, [], ct);

    public async Task<byte[]> DownloadThirdPartyAsync(string url, IReadOnlyList<string> allowedPrivateTextureOrigins, CancellationToken ct = default)
    {
        var normalized = ThirdPartyTextureUrl(url)!;
        var origin = TextureOrigin.FromUrl(normalized);
        var allowPrivate = allowedPrivateTextureOrigins.Contains(origin, StringComparer.Ordinal);
        TextureConnectionPolicy.CheckLiteralTarget(new Uri(normalized), allowPrivate);
        // A private connection must never enter a pool later used by an unconsenting account.
        using var consentedHttp = httpClient is null && (allowPrivate || _connections is not null)
            ? new HttpClient((_connections ?? ThirdPartyConnections).CreateHandler(allowPrivate)) { Timeout = TimeSpan.FromSeconds(15) }
            : null;
        return await DownloadTextureAsync(normalized, MinecraftSkinImage.MaximumHighDefinitionBytes,
            httpClient ?? consentedHttp ?? PublicThirdPartyHttp, ct).ConfigureAwait(false);
    }

    public static string? ThirdPartyTextureUrl(string? value)
    {
        if (value is null) return null;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048 || value.Any(char.IsControl) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrEmpty(uri.Host) || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new AccountAuthenticationException("第三方服务返回了不安全的纹理地址。");
        return uri.AbsoluteUri;
    }

    private static async Task<byte[]> DownloadTextureAsync(string url, int maximumBytes, HttpClient client, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var request = new HttpRequestMessage(HttpMethod.Get, url); // Never attach an account token to texture requests.
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK) throw new AccountAuthenticationException("纹理暂时无法加载。");
            return await AccountHttp.ReadBoundedAsync(response.Content, maximumBytes, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new AccountAuthenticationException("纹理读取超时，请重试。"); }
        catch (HttpRequestException ex) when (ex.InnerException is AccountAuthenticationException)
        { throw TextureConnectionPolicy.Blocked(); }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException)
        { throw new AccountAuthenticationException("纹理暂时无法加载。"); }
    }
}
