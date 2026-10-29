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
    private const int MaximumBytes = AccountVaultSerializer.MaximumBytes;
    private static readonly byte[] Header = Encoding.ASCII.GetBytes("WonderAccounts1\r\n");
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
            var vault = AccountVaultSerializer.Deserialize(plaintext);
            return vault;
        }
        catch (Exception ex) when (ex is JsonException or CryptographicException or ArgumentException)
        { throw InvalidVault(); }
        finally { if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext); }
    }

    public void Save(AccountVault vault)
    {
        AccountVaultSerializer.Validate(vault);
        // A new service instance must not silently overwrite a corrupt or other-user vault.
        if (File.Exists(path)) _ = Load();
        byte[]? plaintext = null;
        string? temporary = null;
        try
        {
            plaintext = AccountVaultSerializer.Serialize(vault);
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
