// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using System.Text.Json;
using System.Runtime.CompilerServices;
using Wonder.Application;
using Wonder.Core;

namespace Wonder.Desktop;

internal static class AccountAvatarLoader
{
    private static readonly ConditionalWeakTable<AccountService, Cache> Caches = new();
    public static Cache For(AccountService service) => Caches.GetValue(service, _ => new Cache());

    public static async Task LoadAsync(AccountSkinPreview preview, AccountProfile profile, GameInstance? instance,
        IReadOnlyList<GameInstance>? alternatives = null, Cache? cache = null)
    {
        var key = Key(profile, instance, alternatives);
        var retentionKey = RetentionKey(profile);
        if (cache?.Get(key, retentionKey) is { } cached) preview.RestoreAvatar(cached);
        var retain = preview.Avatar is not null;
        Task loading;
        if (profile.Kind != AccountKind.Offline) loading = preview.LoadAsync(profile, retainAvatar: retain);
        else
        {
            var skin = profile.OfflineSkinId is { } id ? OfflineDefaultSkins.Get(id) : OfflineDefaultSkins.ForUuid(profile.Uuid);
            loading = preview.LoadDefaultAsync(instance, skin.TextureName, skin.Variant, skin.IsLegacy, alternatives, retainAvatar: retain);
        }
        var revision = preview.TextureRevision;
        await loading;
        if (preview.TextureRevision == revision && preview.LoadedAvatar is { } loaded) cache?.Put(key, retentionKey, loaded);
    }

    // Refreshing an appearance may retain its last pixels; another identity or changed permission must not.
    public static string RetentionKey(AccountProfile? profile) => JsonSerializer.Serialize(new object?[]
    {
        profile?.Id, profile?.Kind, profile?.ServerUrl, profile?.Uuid, profile?.OfflineSkinId,
        profile?.Kind != AccountKind.Offline && profile?.SkinUrl is null, profile?.AllowedPrivateTextureOrigins
    });

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

    internal sealed class Cache
    {
        private const int MaximumEntries = 32, MaximumBytes = 16 * 1024 * 1024;
        private readonly LinkedList<(string Key, string RetentionKey, AccountSkinPreview.AvatarSnapshot Snapshot)> _recent = new();
        private readonly Dictionary<string, LinkedListNode<(string Key, string RetentionKey, AccountSkinPreview.AvatarSnapshot Snapshot)>> _entries = new(StringComparer.Ordinal);
        private int _bytes;

        public AccountSkinPreview.AvatarSnapshot? Get(string key, string? retentionKey = null)
        {
            if (!_entries.TryGetValue(key, out var node))
            {
                // A failed refresh may change the request key without replacing the last usable image.
                // Only the same identity and texture permission may reuse that image in a new view.
                if (retentionKey is null) return null;
                node = _recent.Last;
                while (node is not null && node.Value.RetentionKey != retentionKey) node = node.Previous;
                if (node is null) return null;
            }
            _recent.Remove(node); _recent.AddLast(node);
            return node.Value.Snapshot;
        }

        public void Put(string key, string retentionKey, AccountSkinPreview.AvatarSnapshot snapshot)
        {
            if (_entries.Remove(key, out var previous)) { _recent.Remove(previous); _bytes -= previous.Value.Snapshot.Png.Length; }
            if (snapshot.Png.Length > MaximumBytes) return;
            var node = _recent.AddLast((key, retentionKey, snapshot)); _entries.Add(key, node); _bytes += snapshot.Png.Length;
            while (_entries.Count > MaximumEntries || _bytes > MaximumBytes)
            {
                var first = _recent.First!; _recent.RemoveFirst(); _entries.Remove(first.Value.Key); _bytes -= first.Value.Snapshot.Png.Length;
            }
        }
    }
}
