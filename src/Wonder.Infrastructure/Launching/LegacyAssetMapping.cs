// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using System.Security.Cryptography;
using System.Text.Json;
using Wonder.Core;
using static Wonder.Infrastructure.LocalFiles;

namespace Wonder.Infrastructure;

internal static class LegacyAssetMapping
{
    public static string Prepare(string assets, string id, string gameDirectory, JsonElement index, CancellationToken ct)
    {
        var targets = new List<string>();
        if (index.Get("virtual").Boolean() == true) targets.Add(Path.Combine(assets, "virtual", Segment(id)));
        if (index.Get("map_to_resources").Boolean() == true) targets.Add(Path.Combine(gameDirectory, "resources"));
        if (targets.Count == 0) return assets;
        var mappings = new List<(string Source, string Destination, string Hash)>();
        foreach (var item in index.Get("objects").EnumerateObject())
        {
            ct.ThrowIfCancellationRequested();
            if (Path.IsPathRooted(item.Name) || item.Name.Contains('\\') || item.Name.Contains(':') || item.Name.Split('/').Any(x => x is "" or "." or ".."))
                throw new InvalidDataException("旧式资源路径无效。");
            var hash = item.Value.Get("hash").Text()!;
            foreach (var target in targets)
            {
                var destination = Under(target, item.Name);
                for (var path = destination; path is not null; path = Path.GetDirectoryName(path))
                    if (Path.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException("资源映射目录包含符号链接，未写入。");
                mappings.Add((Path.Combine(assets, "objects", hash[..2], hash), destination, hash));
            }
        }
        foreach (var item in mappings)
        {
            ct.ThrowIfCancellationRequested();
            if (File.Exists(item.Destination))
            {
                if (!Matches(item.Destination, item.Hash)) throw new IOException("资源映射位置已有不同文件，未覆盖：" + item.Destination);
                continue;
            }
            if (!Matches(item.Source, item.Hash)) throw new InvalidDataException("旧式资源校验失败，请重新下载：" + item.Hash);
            Directory.CreateDirectory(Path.GetDirectoryName(item.Destination)!);
            var temporary = item.Destination + ".wonder-" + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.Copy(item.Source, temporary, false); ct.ThrowIfCancellationRequested(); File.Move(temporary, item.Destination, false); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        return targets[0];
    }
    private static bool Matches(string path, string hash)
    { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA1.HashData(stream)).Equals(hash, StringComparison.OrdinalIgnoreCase); }
}
