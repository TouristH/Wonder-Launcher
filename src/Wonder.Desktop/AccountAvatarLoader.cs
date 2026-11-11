// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using System.Text.Json;
using Wonder.Core;

namespace Wonder.Desktop;

internal static class AccountAvatarLoader
{
    public static Task LoadAsync(AccountSkinPreview preview, AccountProfile profile, GameInstance? instance, IReadOnlyList<GameInstance>? alternatives = null)
    {
        if (profile.Kind != AccountKind.Offline) return preview.LoadAsync(profile);
        var skin = profile.OfflineSkinId is { } id ? OfflineDefaultSkins.Get(id) : OfflineDefaultSkins.ForUuid(profile.Uuid);
        return preview.LoadDefaultAsync(instance, skin.TextureName, skin.Variant, skin.IsLegacy, alternatives);
    }

    public static string Key(AccountProfile? profile, GameInstance? instance, IReadOnlyList<GameInstance>? alternatives = null) => JsonSerializer.Serialize(new object?[]
    {
        profile?.Id, profile?.Kind, profile?.ServerUrl, profile?.Uuid, profile?.SkinUrl, profile?.SkinVariant, profile?.OfflineSkinId, profile?.LastValidatedAt,
        profile?.Kind == AccountKind.Offline ? InstanceKey(instance, alternatives) : null,
        profile?.AllowedPrivateTextureOrigins
    });

    public static string InstanceKey(GameInstance? instance, IReadOnlyList<GameInstance>? alternatives = null) =>
        JsonSerializer.Serialize(new[] { Fields(instance) }.Concat((alternatives ?? []).Take(32).Select(Fields)));

    private static string?[] Fields(GameInstance? instance) =>
        [instance?.Id, instance?.GameVersion, instance?.VersionId, instance?.VersionsDirectory, instance?.LibraryDirectory];
}
