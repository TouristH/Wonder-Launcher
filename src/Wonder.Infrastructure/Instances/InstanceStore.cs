// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Text.Json;
using Wonder.Application;
using Wonder.Core;

namespace Wonder.Infrastructure;

public sealed class InstanceStore(string path) : IInstanceStore
{
    private const int MaximumBytes = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        IgnoreReadOnlyProperties = true, AllowDuplicateProperties = false, MaxDepth = 32
    };
    public InstanceCatalog Load()
    {
        if (!File.Exists(path)) return new();
        try
        {
            if (new FileInfo(path).Length > MaximumBytes) throw new InvalidDataException("实例索引超过大小限制。");
            var catalog = JsonSerializer.Deserialize<InstanceCatalog>(File.ReadAllBytes(path), Options) ?? throw new InvalidDataException("实例索引为空。");
            catalog.Validate();
            for (var index = 0; index < catalog.Instances.Count; index++)
            {
                var instance = catalog.Instances[index];
                if (string.IsNullOrWhiteSpace(instance.GameVersion) || instance.ModLoaders.Count == 0)
                    catalog.Instances[index] = instance with
                    {
                        GameVersion = string.IsNullOrWhiteSpace(instance.GameVersion) ? InstanceVersionMetadata.Read(instance) : instance.GameVersion,
                        ModLoaders = instance.ModLoaders.Count == 0 ? InstanceVersionMetadata.ReadModLoaders(instance) : instance.ModLoaders
                    };
            }
            return catalog;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException) { throw new InvalidDataException("实例索引格式无效。", ex); }
    }
    public void Save(InstanceCatalog catalog)
    {
        catalog.Validate();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(catalog, Options);
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("实例索引超过大小限制。");
        var target = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, target, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
