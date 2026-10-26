// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Wonder.Core;

public sealed record OfflineDefaultSkin(string Id, string Name, string TextureName, string Variant, string Description, bool IsLegacy, int Index);

/// <summary>Default appearance is selected by the client's Java UUID hash; it is not a texture injection.</summary>
public static class OfflineDefaultSkins
{
    private static readonly string[] Names = ["Alex", "Ari", "Efe", "Kai", "Makena", "Noor", "Steve", "Sunny", "Zuri"];
    public static IReadOnlyList<OfflineDefaultSkin> Choices { get; } = Build();
    private static IReadOnlyList<OfflineDefaultSkin> Build()
    {
        var choices = new List<OfflineDefaultSkin>();
        foreach (var variant in new[] { "slim", "classic" })
            for (var i = 0; i < Names.Length; i++)
            {
                var name = Names[i]; var index = i + (variant == "slim" ? 0 : 9);
                choices.Add(new(name.ToLowerInvariant() + "-" + variant, name + (variant == "slim" ? " · 纤细" : " · 经典"), name.ToLowerInvariant(), variant,
                    "适用于 Java 1.19.3+ 默认皮肤规则；旧版 1.8–1.19.2 将显示 " + (index % 2 == 0 ? "Steve" : "Alex") + "。", false, index));
            }
        choices.Add(new("legacy-steve", "Steve · 旧版 1.8–1.19.2", "steve", "classic", "针对旧版 Java 的 UUID 奇偶规则。1.19.3+ 会按新规则选择其他默认外观。", true, 0));
        choices.Add(new("legacy-alex", "Alex · 旧版 1.8–1.19.2", "alex", "slim", "针对旧版 Java 的 UUID 奇偶规则。1.19.3+ 会按新规则选择其他默认外观。", true, 1));
        return choices.AsReadOnly();
    }
    public static OfflineDefaultSkin Get(string id) => Choices.FirstOrDefault(x => x.Id == id) ?? throw new ArgumentException("默认皮肤选项无效。");
    public static OfflineDefaultSkin ForUuid(string uuid) => Choices[((JavaHash(uuid) % 18) + 18) % 18];
    public static string LegacySkinName(string uuid) => (JavaHash(uuid) & 1) == 0 ? "Steve" : "Alex";
    public static int JavaHash(string uuid)
    {
        if (!Guid.TryParseExact(uuid, "N", out _)) throw new ArgumentException("离线 UUID 无效。");
        var bytes = Convert.FromHexString(uuid);
        return BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(0, 4)) ^ BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(4, 4)) ^
            BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(8, 4)) ^ BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(12, 4));
    }
    public static string CreateUuid(string username, string? skinId)
    {
        if (username.Length is < 3 or > 16 || !username.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')) throw new ArgumentException("离线玩家名无效。");
        if (skinId is null) return NameUuid("OfflinePlayer:" + username);
        var skin = Get(skinId);
        for (var attempt = 0; attempt < 65536; attempt++)
        {
            var candidate = NameUuid("WonderOfflineSkin:" + username + ":" + skinId + ":" + attempt.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var hash = JavaHash(candidate);
            if (skin.IsLegacy ? (hash & 1) == skin.Index : ((hash % 18) + 18) % 18 == skin.Index) return candidate;
        }
        throw new InvalidOperationException("无法生成默认皮肤身份。");
    }
    public static bool IsValid(string username, string uuid, string? skinId)
    {
        try { return uuid == CreateUuid(username, skinId); } catch (ArgumentException) { return false; }
    }
    private static string NameUuid(string value)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(value));
        bytes[6] = (byte)((bytes[6] & 15) | 48); bytes[8] = (byte)((bytes[8] & 63) | 128);
        return Convert.ToHexStringLower(bytes);
    }
}
