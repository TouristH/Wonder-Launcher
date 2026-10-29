// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Wonder.Application;

namespace Wonder.Infrastructure;

public static class AccountStoreFactory
{
    public static IAccountStore Create(string dataDirectory)
    {
        if (OperatingSystem.IsWindows()) return new WindowsAccountStore(Path.Combine(dataDirectory, "accounts.dat"));
        if (OperatingSystem.IsLinux()) return new KeyringAccountStore(Path.Combine(dataDirectory, "accounts.linux.dat"), new LinuxSecretStore());
        if (OperatingSystem.IsMacOS()) return new KeyringAccountStore(Path.Combine(dataDirectory, "accounts.macos.dat"), new MacOSKeychainStore());
        throw new PlatformNotSupportedException("当前平台尚未提供账户凭据保护服务。");
    }
}
