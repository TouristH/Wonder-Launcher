// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

namespace Wonder.Core;

public static class InstanceIcon
{
    public static string DefaultKey(GameInstance instance)
    {
        // NeoForge metadata may also contain Forge compatibility libraries.
        foreach (var (name, key) in new[] { ("NeoForge", "neoforge"), ("Fabric", "fabric"), ("Forge", "forge") })
            if (instance.ModLoaders.Any(loader => loader.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) return key;
        return "minecraft";
    }
}
