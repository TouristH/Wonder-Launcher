// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Wonder.Application;
using Wonder.Core;

namespace Wonder.Infrastructure;

/// <summary>Windows CurrentUser DPAPI protects the entire vault, including account metadata.</summary>
public sealed class WindowsAccountStore(string path) : IAccountStore
{
    private const int MaximumBytes = 4 * 1024 * 1024;
    private static readonly byte[] Header = Encoding.ASCII.GetBytes("WonderAccounts1\r\n");
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        AllowDuplicateProperties = false, MaxDepth = 24
    };

    public AccountVault Load()
    {
        if (!File.Exists(path)) return new();
        byte[]? plaintext = null;
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length is <= 16 or > MaximumBytes) throw InvalidVault();
            var encrypted = new byte[checked((int)file.Length)]; file.ReadExactly(encrypted);
            if (!encrypted.AsSpan().StartsWith(Header)) throw InvalidVault();
            plaintext = Crypt(encrypted.AsSpan(Header.Length).ToArray(), protect: false);
            var vault = JsonSerializer.Deserialize<AccountVault>(plaintext, Options) ?? throw InvalidVault();
            Validate(vault);
            return vault;
        }
        catch (Exception ex) when (ex is JsonException or CryptographicException or ArgumentException)
        { throw InvalidVault(); }
        finally { if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext); }
    }

    public void Save(AccountVault vault)
    {
        Validate(vault);
        // A new service instance must not silently overwrite a corrupt or other-user vault.
        if (File.Exists(path)) _ = Load();
        byte[]? plaintext = null;
        string? temporary = null;
        try
        {
            plaintext = JsonSerializer.SerializeToUtf8Bytes(vault, Options);
            if (plaintext.Length > MaximumBytes - 1024) throw InvalidVault();
            var encrypted = Crypt(plaintext, protect: true);
            var target = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                file.Write(Header); file.Write(encrypted); file.Flush(flushToDisk: true);
            }
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
            if (temporary is not null && File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void Validate(AccountVault vault)
    {
        if (vault is null || vault.Version != 1 || vault.Accounts is null || vault.Accounts.Count > 1024 ||
            !Guid.TryParse(vault.MicrosoftClientId, out _) || vault.Servers is null || vault.Servers.Count > 256) throw InvalidVault();
        var serverIds = new HashSet<string>(StringComparer.Ordinal);
        var serverUrls = new HashSet<string>(StringComparer.Ordinal);
        foreach (var server in vault.Servers)
        {
            if (server is null || !SafeText(server.Id, 128) || !serverIds.Add(server.Id) || !SafeText(server.Name, 120)) throw InvalidVault();
            try
            {
                if (server.ApiUrl != YggdrasilAuthenticator.NormalizeServerUrl(server.ApiUrl) || !serverUrls.Add(server.ApiUrl)) throw InvalidVault();
            }
            catch (AccountAuthenticationException) { throw InvalidVault(); }
            foreach (var link in new[] { server.Homepage, server.RegistrationUrl })
                if (link is not null && (!SafeText(link, 2048) || !Uri.TryCreate(link, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0)) throw InvalidVault();
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in vault.Accounts)
        {
            if (entry?.Profile is not { } profile || entry.Credentials is not { } credentials ||
                !SafeText(profile.Id, 256) || !ids.Add(profile.Id) || !Enum.IsDefined(profile.Kind) ||
                !SafeText(profile.Username, 16) || !ValidUuid(profile.Uuid)) throw InvalidVault();
            if (profile.Kind == AccountKind.Microsoft && (!Guid.TryParse(profile.ClientId, out _) || profile.ServerUrl is not null)) throw InvalidVault();
            if (profile.Kind == AccountKind.Yggdrasil)
            {
                try { if (profile.ServerUrl != YggdrasilAuthenticator.NormalizeServerUrl(profile.ServerUrl!)) throw InvalidVault(); }
                catch (AccountAuthenticationException) { throw InvalidVault(); }
            }
            if (profile.Kind == AccountKind.Offline && (!string.IsNullOrEmpty(credentials.AccessToken) || !string.IsNullOrEmpty(credentials.RefreshToken))) throw InvalidVault();
            if (profile.Kind == AccountKind.Offline && !OfflineDefaultSkins.IsValid(profile.Username, profile.Uuid, profile.OfflineSkinId)) throw InvalidVault();
            if (profile.Kind != AccountKind.Offline && profile.OfflineSkinId is not null) throw InvalidVault();
            if (!Bounded(credentials.AccessToken, 65536) || !Bounded(credentials.RefreshToken, 65536) ||
                !Bounded(credentials.ClientToken, 65536) || !Bounded(credentials.Xuid, 128)) throw InvalidVault();
            if (profile.SkinVariant is not ("classic" or "slim") || profile.Capes is null || profile.Capes.Count > 128) throw InvalidVault();
            try
            {
                string? TextureUrl(string? url) => profile.Kind == AccountKind.Yggdrasil
                    ? AccountTextureClient.ThirdPartyTextureUrl(url) : MicrosoftAuthenticator.OfficialTextureUrl(url);
                if (profile.SkinUrl is { } skin && (skin.Length > 2048 || TextureUrl(skin) != skin)) throw InvalidVault();
                var capeIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var cape in profile.Capes)
                    if (cape is null || !Guid.TryParse(cape.Id, out _) || !capeIds.Add(cape.Id) || !SafeText(cape.Name, 80) ||
                        !SafeText(cape.Url, 2048) || TextureUrl(cape.Url) != cape.Url) throw InvalidVault();
                if (profile.ActiveCapeId is not null && !capeIds.Contains(profile.ActiveCapeId)) throw InvalidVault();
            }
            catch (AccountAuthenticationException) { throw InvalidVault(); }
        }
        if (vault.SelectedAccountId is not null && !ids.Contains(vault.SelectedAccountId)) throw InvalidVault();
        if (vault.AuthlibInjectorPath is { } agent && (!SafeText(agent, 32767) || !Path.IsPathFullyQualified(agent))) throw InvalidVault();
        if (vault.AuthlibInjectorSha256 is { } hash && (hash.Length != 64 || !hash.All(char.IsAsciiHexDigit))) throw InvalidVault();
        if ((vault.AuthlibInjectorPath is null) != (vault.AuthlibInjectorSha256 is null)) throw InvalidVault();
    }

    private static bool Bounded(string? value, int max) => value is not null && value.Length <= max && !value.Any(char.IsControl);
    private static bool SafeText(string? value, int max) => !string.IsNullOrWhiteSpace(value) && Bounded(value, max);
    private static bool ValidUuid(string? value) => value is { Length: 32 } && value.All(char.IsAsciiHexDigit);
    private static InvalidDataException InvalidVault() => new("账户库无法读取或格式无效，原文件已保留。请使用原 Windows 用户，或先备份文件再重新登录。");

    private static byte[] Crypt(byte[] value, bool protect)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("账户凭据保护需要 Windows 当前用户的数据保护服务。");
        var input = new Blob { Length = value.Length, Data = Marshal.AllocHGlobal(value.Length) };
        var output = new Blob();
        try
        {
            Marshal.Copy(value, 0, input.Data, value.Length);
            // CRYPTPROTECT_UI_FORBIDDEN, intentionally no CRYPTPROTECT_LOCAL_MACHINE.
            var success = protect
                ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!success || output.Length < 1 || output.Length > MaximumBytes) throw new CryptographicException("Windows 无法保护或解密账户凭据。");
            var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
        }
        finally
        {
            if (input.Data != IntPtr.Zero)
            {
                Marshal.Copy(new byte[input.Length], 0, input.Data, input.Length); Marshal.FreeHGlobal(input.Data);
            }
            if (output.Data != IntPtr.Zero)
            {
                if (output.Length is > 0 and <= MaximumBytes) Marshal.Copy(new byte[output.Length], 0, output.Data, output.Length);
                _ = LocalFree(output.Data);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Blob { public int Length; public IntPtr Data; }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
