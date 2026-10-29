// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using System.Security.Cryptography;
using System.Text;

namespace Wonder.Core;

/// <summary>Java-compatible name UUIDs. Callers validate player names; hashing preserves the original text.</summary>
public static class OfflineIdentity
{
    public static string CreateUuid(string username) => NameUuid("OfflinePlayer:" + username);

    internal static string NameUuid(string value)
    {
        // MD5 is required by Java's nameUUIDFromBytes format, not used for credential protection.
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(value));
        bytes[6] = (byte)((bytes[6] & 0x0f) | 0x30);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return Convert.ToHexStringLower(bytes);
    }
}
