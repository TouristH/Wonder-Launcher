// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using System.IO.Compression;
using System.Text.Json;
using Wonder.Core;
using static Wonder.Infrastructure.LocalFiles;

namespace Wonder.Infrastructure;

/// <summary>Find a named default texture in registered game clients before using the official preview source.</summary>
public static class DefaultSkinTextureReader
{
    public static byte[]? TryRead(GameInstance? instance, string textureName, string variant, bool legacy) =>
        TryRead(instance, [], textureName, variant, legacy)?.Bytes;

    public static (byte[] Bytes, bool SelectedInstance)? TryRead(GameInstance? instance, IReadOnlyList<GameInstance> alternatives,
        string textureName, string variant, bool legacy, CancellationToken ct = default)
    {
        var modern = "assets/minecraft/textures/entity/player/" + ModernEntry(textureName, variant);
        var old = (textureName, variant) switch
        {
            ("steve", "classic") => new[] { "assets/minecraft/textures/entity/steve.png", "mob/char.png" },
            ("alex", "slim") => new[] { "assets/minecraft/textures/entity/alex.png" },
            _ => Array.Empty<string>()
        };
        var entries = legacy ? old.Append(modern).ToArray() : old.Prepend(modern).ToArray();
        var clients = new List<(string Path, bool Selected)>();
        var paths = new HashSet<string>(WonderPath.Comparer);
        if (instance is not null) AddClient(instance, true);
        foreach (var alternative in alternatives.Take(32))
            if (alternative.Id != instance?.Id) AddClient(alternative, false);

        foreach (var (path, selected) in clients)
        {
            ct.ThrowIfCancellationRequested();
            if (ReadEntries(path, entries, ct) is { } bytes) return (bytes, selected);
        }
        return null;

        void AddClient(GameInstance client, bool selected)
        {
            ct.ThrowIfCancellationRequested();
            try { client.Validate(); }
            catch (ArgumentException) { return; }
            foreach (var path in ClientArchives(client))
                if (paths.Add(path)) clients.Add((path, selected));
        }
    }

    public static Task<byte[]?> ReadOfficialAsync(string textureName, string variant, CancellationToken ct = default) =>
        OfficialDefaultSkinTextureSource.Shared.GetAsync(ModernEntry(textureName, variant), ct);

    public static byte[]? TryReadCachedOfficial(string textureName, string variant) =>
        OfficialDefaultSkinTextureSource.Shared.TryRead(ModernEntry(textureName, variant));

    private static string ModernEntry(string textureName, string variant)
    {
        if (textureName is not ("alex" or "ari" or "efe" or "kai" or "makena" or "noor" or "steve" or "sunny" or "zuri") || variant is not ("classic" or "slim"))
            throw new ArgumentException("默认皮肤无效。");
        return (variant == "slim" ? "slim/" : "wide/") + textureName + ".png";
    }

    private static IEnumerable<string> ClientArchives(GameInstance instance)
    {
        static bool IsVersion(string? value) => value is { Length: > 0 and < 201 } && value is not ("." or "..") && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');
        var versions = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>(new[] { instance.GameVersion, instance.VersionId }.Where(IsVersion));
        while (versions.Count < 16 && pending.TryDequeue(out var version))
        {
            if (!versions.Add(version)) continue;
            var versionRoot = Path.Combine(instance.VersionsDirectory, version);
            yield return Path.Combine(versionRoot, version + ".jar");
            yield return Path.Combine(instance.LibraryDirectory, "com", "mojang", "minecraft", version, "minecraft-" + version + "-client.jar");
            var metadata = ReadMetadata(Path.Combine(versionRoot, version + ".json"));
            foreach (var key in new[] { "jar", "inheritsFrom" })
                if (metadata.Get(key).Text() is { } inherited && IsVersion(inherited)) pending.Enqueue(inherited);
        }
        if (instance.Source is not (InstanceSource.Prism or InstanceSource.MultiMc)) yield break;
        foreach (var metadataPath in instance.MetadataPaths.Take(16))
        {
            var jar = ReadMetadata(metadataPath).Get("mainJar");
            if (jar.Get("name").Text() is not { } name) continue;
            string? path = null;
            try { path = Under(instance.LibraryDirectory, LaunchArguments.Maven(name)); }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException) { }
            if (path is not null) yield return path;
        }
    }

    private static JsonElement ReadMetadata(string path)
    {
        try { return File.Exists(path) ? Json(path) : default; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException) { return default; }
    }

    private static byte[]? ReadEntries(string path, IReadOnlyList<string> entries, CancellationToken ct)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (file.Length is <= 0 or > 1024L * 1024 * 1024) return null;
            using var zip = new ZipArchive(file, ZipArchiveMode.Read);
            foreach (var name in entries)
            {
                var entry = zip.GetEntry(name);
                if (entry is null || entry.Length is < 45 or > 1024 * 1024) continue;
                using var stream = entry.Open();
                var bytes = new byte[checked((int)entry.Length)];
                ct.ThrowIfCancellationRequested();
                stream.ReadExactly(bytes);
                if (stream.ReadByte() != -1) continue;
                try { MinecraftSkinImage.Validate(bytes); return bytes; }
                catch (Wonder.Application.AccountAuthenticationException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException) { }
        return null;
    }
}
