// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using System.IO.Compression;
using Wonder.Core;

namespace Wonder.Infrastructure;

/// <summary>Read a named default texture from the selected, already installed client; no game resources are bundled.</summary>
public static class DefaultSkinTextureReader
{
    public static byte[]? TryRead(GameInstance? instance, string textureName, string variant, bool legacy)
    {
        if (instance is null) return null;
        if (textureName is not ("alex" or "ari" or "efe" or "kai" or "makena" or "noor" or "steve" or "sunny" or "zuri") || variant is not ("classic" or "slim"))
            throw new ArgumentException("默认皮肤无效。");
        instance.Validate();
        static bool Segment(string value) => value.Length is > 0 and < 201 && value is not ("." or "..") && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');
        var candidates = new List<string>();
        foreach (var version in new[] { instance.GameVersion, instance.VersionId }.Where(Segment).Distinct(StringComparer.Ordinal))
        {
            candidates.Add(Path.Combine(instance.VersionsDirectory, version, version + ".jar"));
            candidates.Add(Path.Combine(instance.LibraryDirectory, "com", "mojang", "minecraft", version, "minecraft-" + version + "-client.jar"));
        }
        var modern = "assets/minecraft/textures/entity/player/" + (variant == "slim" ? "slim" : "wide") + "/" + textureName + ".png";
        var old = "assets/minecraft/textures/entity/" + textureName + ".png";
        foreach (var path in candidates.Distinct(WonderPath.Comparer))
        {
            try
            {
                if (!File.Exists(path)) continue;
                using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (file.Length is <= 0 or > 1024L * 1024 * 1024) continue;
                using var zip = new ZipArchive(file, ZipArchiveMode.Read);
                var entry = legacy ? zip.GetEntry(old) ?? zip.GetEntry(modern) : zip.GetEntry(modern);
                if (entry is null || entry.Length is < 45 or > 1024 * 1024) continue;
                using var stream = entry.Open(); using var bytes = new MemoryStream(); var chunk = new byte[8192]; int read;
                while ((read = stream.Read(chunk)) > 0)
                { if (bytes.Length + read > 1024 * 1024) throw new InvalidDataException(); bytes.Write(chunk, 0, read); }
                var result = bytes.ToArray(); MinecraftSkinImage.Validate(result); return result;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or Wonder.Application.AccountAuthenticationException) { }
        }
        return null;
    }
}
