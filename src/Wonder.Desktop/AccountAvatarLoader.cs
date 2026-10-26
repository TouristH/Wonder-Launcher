// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using System.Text.Json;
using Wonder.Core;

namespace Wonder.Desktop;

internal static class AccountAvatarLoader
{
    public static Task LoadAsync(AccountSkinPreview preview, AccountProfile profile, GameInstance? instance)
    {
        if (profile.Kind != AccountKind.Offline) return preview.LoadAsync(profile);
        var skin = profile.OfflineSkinId is { } id ? OfflineDefaultSkins.Get(id) : OfflineDefaultSkins.ForUuid(profile.Uuid);
        return preview.LoadDefaultAsync(instance, skin.TextureName, skin.Variant, skin.IsLegacy);
    }

    public static string Key(AccountProfile? profile, GameInstance? instance) => JsonSerializer.Serialize(new object?[]
    {
        profile?.Id, profile?.Kind, profile?.Uuid, profile?.SkinUrl, profile?.SkinVariant, profile?.OfflineSkinId, profile?.LastValidatedAt,
        profile?.Kind == AccountKind.Offline ? InstanceKey(instance) : null
    });

    public static string InstanceKey(GameInstance? instance) => JsonSerializer.Serialize(new[]
    { instance?.Id, instance?.GameVersion, instance?.VersionId, instance?.VersionsDirectory, instance?.LibraryDirectory });
}
