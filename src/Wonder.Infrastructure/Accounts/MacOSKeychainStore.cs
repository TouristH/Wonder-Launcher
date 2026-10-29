// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Runtime.InteropServices;
using System.Text;
using Wonder.Application;
using Wonder.Core;

namespace Wonder.Infrastructure;

/// <summary>Generic-password items in the user's native macOS Keychain. No secret-bearing subprocess.</summary>
internal sealed class MacOSKeychainStore : IAccountKeyStore
{
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    private static readonly byte[] Service = Encoding.UTF8.GetBytes("org.wonderlauncher.account-vault");

    public byte[]? ReadKey(string id)
    {
        if (!OperatingSystem.IsMacOS()) throw Unavailable();
        var account = Encoding.UTF8.GetBytes(id);
        var status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)Service.Length, Service,
            (uint)account.Length, account, out var length, out var data, IntPtr.Zero);
        if (status == -25300) return null; // errSecItemNotFound; never create a replacement while loading.
        if (status != 0) throw Unavailable();
        try
        {
            if (length != 32 || data == IntPtr.Zero) throw Unavailable();
            var key = new byte[32]; Marshal.Copy(data, key, 0, key.Length); return key;
        }
        finally { if (data != IntPtr.Zero) _ = SecKeychainItemFreeContent(IntPtr.Zero, data); }
    }

    public void StoreKey(string id, byte[] key)
    {
        if (!OperatingSystem.IsMacOS() || key.Length != 32) throw Unavailable();
        var account = Encoding.UTF8.GetBytes(id);
        // The system may ask to unlock the login keychain or grant this application access.
        var status = SecKeychainAddGenericPassword(IntPtr.Zero, (uint)Service.Length, Service,
            (uint)account.Length, account, (uint)key.Length, key, IntPtr.Zero);
        if (status != 0) throw Unavailable();
    }

    private static AccountAuthenticationException Unavailable() => new("macOS 钥匙串不可用或访问被拒绝。请解锁原用户的登录钥匙串，并在系统提示中允许 Wonder 访问后重试；不会将凭据改存为明文。");

    [DllImport(Security)]
    private static extern int SecKeychainFindGenericPassword(IntPtr keychain, uint serviceLength, byte[] service,
        uint accountLength, byte[] account, out uint passwordLength, out IntPtr passwordData, IntPtr item);
    [DllImport(Security)]
    private static extern int SecKeychainAddGenericPassword(IntPtr keychain, uint serviceLength, byte[] service,
        uint accountLength, byte[] account, uint passwordLength, byte[] passwordData, IntPtr item);
    [DllImport(Security)]
    private static extern int SecKeychainItemFreeContent(IntPtr attributes, IntPtr data);
}
