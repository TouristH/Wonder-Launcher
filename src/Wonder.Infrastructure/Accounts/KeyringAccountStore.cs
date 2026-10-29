// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Wonder.Application;
using Wonder.Core;

namespace Wonder.Infrastructure;

/// <summary>AES-GCM protects the vault; its random key lives only in the operating system key store.</summary>
internal sealed class KeyringAccountStore : IAccountStore
{
    private static readonly byte[] Header = Encoding.ASCII.GetBytes("WonderKeyringAccounts1\r\n");
    private static readonly byte[] PreviousLinuxHeader = Encoding.ASCII.GetBytes("WonderLinuxAccounts1\r\n");
    private const int IdSize = 16, NonceSize = 12, TagSize = 16;
    private readonly string _path;
    private readonly IAccountKeyStore _secrets;

    internal KeyringAccountStore(string path, IAccountKeyStore secrets)
    { _path = Path.GetFullPath(path); _secrets = secrets; }

    public AccountVault Load()
    {
        if (!File.Exists(_path)) return new();
        var envelope = ReadEnvelope();
        byte[]? key = null, plaintext = null;
        try
        {
            key = ExistingKey(envelope.Id);
            plaintext = new byte[envelope.Data.Length - NonceSize - TagSize];
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(envelope.Data.AsSpan(0, NonceSize), envelope.Data.AsSpan(NonceSize + TagSize),
                envelope.Data.AsSpan(NonceSize, TagSize), plaintext, envelope.AssociatedData);
            return AccountVaultSerializer.Deserialize(plaintext);
        }
        catch (Exception ex) when (ex is JsonException or CryptographicException or ArgumentException)
        { throw InvalidVault(); }
        finally
        {
            if (key is not null) CryptographicOperations.ZeroMemory(key);
            if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public void Save(AccountVault vault)
    {
        AccountVaultSerializer.Validate(vault);
        var exists = File.Exists(_path);
        // Never replace a corrupt vault, a Windows vault, or a vault whose original key is unavailable.
        if (exists) _ = Load();
        var id = exists ? ReadEnvelope().Id : Guid.NewGuid();
        byte[]? key = null, plaintext = null;
        string? temporary = null;
        try
        {
            plaintext = AccountVaultSerializer.Serialize(vault);
            if (plaintext.Length > AccountVaultSerializer.MaximumBytes - 1024) throw InvalidVault();
            key = exists ? ExistingKey(id) : RandomNumberGenerator.GetBytes(32);
            if (!exists) _secrets.StoreKey(id.ToString("N"), key);
            var associatedData = Header.Concat(id.ToByteArray()).ToArray();
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var ciphertext = new byte[plaintext.Length]; var tag = new byte[TagSize];
            using (var aes = new AesGcm(key, TagSize)) aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None,
                Options = FileOptions.WriteThrough
            };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var file = new FileStream(temporary, options))
            {
                file.Write(associatedData); file.Write(nonce); file.Write(tag); file.Write(ciphertext);
                file.Flush(flushToDisk: true);
            }
            File.Move(temporary, _path, overwrite: exists);
        }
        finally
        {
            if (key is not null) CryptographicOperations.ZeroMemory(key);
            if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
            if (temporary is not null && File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private (Guid Id, byte[] AssociatedData, byte[] Data) ReadEnvelope()
    {
        using var file = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length <= PreviousLinuxHeader.Length + IdSize + NonceSize + TagSize ||
            file.Length > AccountVaultSerializer.MaximumBytes) throw InvalidVault();
        var probe = new byte[Header.Length]; file.ReadExactly(probe);
        var header = probe.AsSpan().StartsWith(Header) ? Header :
            probe.AsSpan().StartsWith(PreviousLinuxHeader) ? PreviousLinuxHeader : throw InvalidVault();
        var prefixSize = header.Length + IdSize;
        if (file.Length <= prefixSize + NonceSize + TagSize || file.Length > AccountVaultSerializer.MaximumBytes) throw InvalidVault();
        file.Position = 0;
        // Authenticate the exact original prefix; upgrading only happens on a later successful Save.
        var prefix = new byte[prefixSize]; file.ReadExactly(prefix);
        var id = new Guid(prefix.AsSpan(header.Length));
        if (id == Guid.Empty) throw InvalidVault();
        var data = new byte[checked((int)file.Length - prefixSize)]; file.ReadExactly(data);
        return (id, prefix, data);
    }

    private byte[] ExistingKey(Guid id)
    {
        var key = _secrets.ReadKey(id.ToString("N"));
        if (key is { Length: 32 }) return key;
        if (key is not null) CryptographicOperations.ZeroMemory(key);
        throw new AccountAuthenticationException("账户密钥不可用。请解锁原系统用户的密钥环后重试；原账户文件已保留。复制账户文件不会复制系统密钥。");
    }

    private static InvalidDataException InvalidVault() => new("账户库无法读取、解密或格式无效，原文件已保留。请检查原系统用户的密钥环；其他平台的账户文件不能直接解密。");
}
