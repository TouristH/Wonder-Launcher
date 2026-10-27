// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

namespace Wonder.Core;

/// <summary>Shared business limits; storage format and credential validation remain with their owners.</summary>
public static class AccountLimits
{
    public const int MaximumAccounts = 256;
    public const int MaximumProfileIdLength = 128;
    public const int MaximumUsernameLength = 16;

    public static void ValidateAccountCount(int count)
    {
        if (count > MaximumAccounts)
            throw new AccountLimitException($"账户数量不能超过 {MaximumAccounts} 个。");
    }

    public static void ValidateProfileIdLength(string? id)
    {
        if (id?.Length > MaximumProfileIdLength)
            throw new AccountLimitException($"账户标识不能超过 {MaximumProfileIdLength} 个字符。");
    }

    public static void ValidateUsernameLength(string? username)
    {
        if (username?.Length > MaximumUsernameLength)
            throw new AccountLimitException($"游戏用户名不能超过 {MaximumUsernameLength} 个字符。");
    }
}

/// <summary>Only fixed business-limit diagnostics are safe to display; account values are never included.</summary>
public sealed class AccountLimitException : Exception
{
    internal AccountLimitException(string message) : base(message) { }
}
