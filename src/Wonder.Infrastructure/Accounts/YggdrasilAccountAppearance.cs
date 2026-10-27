// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Wonder.Application;
using Wonder.Core;

namespace Wonder.Infrastructure;

public sealed partial class YggdrasilAuthenticator
{
    public async Task<AccountProfile> ReadAppearanceAsync(AccountProfile profile, CancellationToken ct = default)
    {
        var account = await ReadAccountAppearanceAsync(new(profile, new()), ct).ConfigureAwait(false);
        return account.Profile;
    }

    // Profile properties are public appearance metadata, independent of a successful authentication.
    private async Task<AuthenticatedAccount> ReadAccountAppearanceAsync(AuthenticatedAccount account, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(15));
            var endpoint = new Uri(new Uri(account.Profile.ServerUrl!), "sessionserver/session/minecraft/profile/" + account.Profile.Uuid);
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            // No password, access/client token or Authorization header belongs on this public GET.
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK) return account;
            var bytes = await AccountHttp.ReadBoundedAsync(response.Content, 256 * 1024, timeout.Token).ConfigureAwait(false);
            using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16, AllowDuplicateProperties = false });
            var profile = ReadProfile(json.RootElement);
            if (!string.Equals(profile.Id, account.Profile.Uuid, StringComparison.OrdinalIgnoreCase)) throw Malformed();
            var appearance = ReadThirdPartyAppearance(json.RootElement, account.Profile);
            ct.ThrowIfCancellationRequested();
            return new(appearance, account.Credentials);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return account; }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or JsonException or FormatException or
            InvalidOperationException or KeyNotFoundException or AccountAuthenticationException)
        {
            // Keep the rotated session and last usable appearance; never expose a server response as an error.
            return account;
        }
    }

    private static AccountProfile ReadThirdPartyAppearance(JsonElement root, AccountProfile profile)
    {
        string? encoded = null;
        if (root.TryGetProperty("properties", out var properties))
        {
            if (properties.ValueKind != JsonValueKind.Array || properties.GetArrayLength() > 64) throw Malformed();
            foreach (var property in properties.EnumerateArray())
            {
                if (String(property, "name") != "textures") continue;
                if (encoded is not null) throw Malformed();
                encoded = String(property, "value");
                if (encoded.Length is 0 or > 64 * 1024) throw Malformed();
            }
        }
        var cleared = profile with { SkinUrl = null, SkinVariant = "classic", Capes = [], ActiveCapeId = null };
        if (encoded is null) return cleared;
        var decoded = Convert.FromBase64String(encoded);
        using var json = JsonDocument.Parse(decoded, new JsonDocumentOptions { MaxDepth = 16, AllowDuplicateProperties = false });
        var payload = json.RootElement;
        if (!string.Equals(String(payload, "profileId"), profile.Uuid, StringComparison.OrdinalIgnoreCase) ||
            !payload.TryGetProperty("textures", out var textures) || textures.ValueKind != JsonValueKind.Object) throw Malformed();
        string? skinUrl = null; var variant = "classic";
        if (textures.TryGetProperty("SKIN", out var skin))
        {
            skinUrl = AccountTextureClient.ThirdPartyTextureUrl(String(skin, "url"));
            if (skin.TryGetProperty("metadata", out var metadata))
            {
                if (metadata.ValueKind != JsonValueKind.Object) throw Malformed();
                if (metadata.TryGetProperty("model", out var model))
                {
                    if (model.ValueKind != JsonValueKind.String) throw Malformed();
                    variant = model.GetString() == "slim" ? "slim" : "classic";
                }
            }
        }
        if (!textures.TryGetProperty("CAPE", out var cape)) return cleared with { SkinUrl = skinUrl, SkinVariant = variant };
        var capeUrl = AccountTextureClient.ThirdPartyTextureUrl(String(cape, "url"))!;
        // Yggdrasil exposes one currently worn cape, not an owned-cape list or a cape identifier.
        var capeHash = SHA256.HashData(Encoding.UTF8.GetBytes(profile.ServerUrl + "\n" + profile.Uuid + "\n" + capeUrl));
        var capeId = new Guid(capeHash.AsSpan(0, 16)).ToString();
        return cleared with { SkinUrl = skinUrl, SkinVariant = variant, Capes = [new(capeId, "当前披风", capeUrl)], ActiveCapeId = capeId };
    }
}
