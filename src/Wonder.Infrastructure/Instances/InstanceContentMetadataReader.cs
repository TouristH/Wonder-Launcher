// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Wonder.Core;

namespace Wonder.Infrastructure;

internal sealed record InstanceContentMetadata(string? Title = null, string? Description = null,
    IReadOnlyList<InstanceContentDetail>? Details = null, string? IconPath = null);

internal static class InstanceContentMetadataReader
{
    internal static InstanceContentMetadata Read(InstanceContentKind kind, Func<string, Stream?> open, Func<string, bool> exists) => kind switch
    {
        InstanceContentKind.Mod => ReadMod(open, exists),
        InstanceContentKind.ResourcePack => ReadPack(open) with { IconPath = "pack.png" },
        InstanceContentKind.ShaderPack => ReadShader(open, exists),
        InstanceContentKind.World => InstanceWorldMetadata.Read(open),
        _ => new()
    };

    private static InstanceContentMetadata ReadMod(Func<string, Stream?> open, Func<string, bool> exists)
    {
        if (ReadJson(open, "fabric.mod.json") is { ValueKind: JsonValueKind.Object } fabric)
        {
            var id = Text(fabric, "id");
            var dependencies = Property(fabric, "depends");
            var contact = Property(fabric, "contact");
            return new(Text(fabric, "name") ?? id, Text(fabric, "description"),
                Details(("加载器", "Fabric"), ("模组 ID", id), ("版本", Text(fabric, "version")),
                    ("作者", Join(Array(fabric, "authors").Select(author => Text(author) ?? Text(author, "name")))),
                    ("许可证", TextList(Property(fabric, "license"))), ("运行端", Text(fabric, "environment")),
                    ("依赖", Join(Properties(dependencies).Select(dependency => LabelValue(dependency.Name, TextList(dependency.Value))))),
                    ("主页", Text(contact, "homepage")), ("源码", Text(contact, "sources"))),
                FindIcon(exists, Icon(Property(fabric, "icon")), id));
        }

        if (Property(ReadJson(open, "quilt.mod.json") ?? default, "quilt_loader") is { ValueKind: JsonValueKind.Object } quilt)
        {
            var id = Text(quilt, "id");
            var metadata = Property(quilt, "metadata");
            var contact = Property(metadata, "contact");
            return new(Text(metadata, "name") ?? id, Text(metadata, "description"),
                Details(("加载器", "Quilt"), ("模组 ID", id), ("版本", Text(quilt, "version")), ("项目组", Text(quilt, "group")),
                    ("贡献者", Join(Properties(Property(metadata, "contributors")).Select(author => author.Name))),
                    ("许可证", Join(Items(Property(metadata, "license")).Select(license => Text(license) ?? Text(license, "id")))),
                    ("依赖", Join(Array(quilt, "depends").Select(dependency =>
                        Text(dependency) ?? LabelValue(Text(dependency, "id"), TextList(Property(dependency, "versions")))))),
                    ("主页", Text(contact, "homepage")), ("源码", Text(contact, "sources"))),
                FindIcon(exists, Icon(Property(metadata, "icon")), id));
        }

        var forge = ReadForge(open, exists, "META-INF/neoforge.mods.toml", "NeoForge")
            ?? ReadForge(open, exists, "META-INF/mods.toml", "Forge");
        if (forge is not null) return forge;

        var legacy = ReadJson(open, "mcmod.info");
        if (legacy is { } document)
        {
            var entries = document.ValueKind == JsonValueKind.Array ? Items(document) : Array(document, "modList");
            var mod = entries.FirstOrDefault(entry => entry.ValueKind == JsonValueKind.Object);
            if (mod.ValueKind != JsonValueKind.Object && document.ValueKind == JsonValueKind.Object)
                mod = document;
            if (mod.ValueKind == JsonValueKind.Object)
            {
                var id = Text(mod, "modid");
                return new(Text(mod, "name") ?? id, Text(mod, "description"),
                    Details(("加载器", "Forge"), ("模组 ID", id), ("版本", Text(mod, "version")),
                        ("Minecraft", Text(mod, "mcversion")), ("作者", TextList(Property(mod, "authorList"))),
                        ("依赖", TextList(Property(mod, "dependencies"))), ("主页", Text(mod, "url"))),
                    FindIcon(exists, Text(mod, "logoFile"), id));
            }
        }
        return new(IconPath: FindIcon(exists, null));
    }

    private static InstanceContentMetadata? ReadForge(Func<string, Stream?> open, Func<string, bool> exists, string path, string loader)
    {
        using var stream = open(path);
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        var global = new Dictionary<string, string>(StringComparer.Ordinal);
        var mods = new List<Dictionary<string, string>>();
        var dependencies = new List<(string ModId, Dictionary<string, string> Fields)>();
        Dictionary<string, string>? fields = global;
        while (reader.ReadLine() is { } line)
        {
            var text = line.TrimStart();
            if (text.StartsWith('['))
            {
                var section = text.Split('#', 2)[0].Trim();
                fields = null;
                if (section == "[[mods]]")
                {
                    fields = new(StringComparer.Ordinal);
                    mods.Add(fields);
                }
                else if (section.StartsWith("[[dependencies.", StringComparison.Ordinal) && section.EndsWith("]]", StringComparison.Ordinal))
                {
                    fields = new(StringComparer.Ordinal);
                    dependencies.Add((section[15..^2].Trim('"', '\''), fields));
                }
                continue;
            }

            var separator = text.IndexOf('=');
            if (separator < 1 || text.StartsWith('#')) continue;
            // Read only string values used by mod metadata. Consume multiline strings even outside known sections.
            var value = ReadTomlString(reader, text[(separator + 1)..].TrimStart());
            if (value is not null) fields?.TryAdd(text[..separator].Trim().Trim('"', '\''), value);
        }

        var primary = mods.FirstOrDefault() ?? global;
        var id = primary.GetValueOrDefault("modId");
        var version = primary.GetValueOrDefault("version");
        if (version == "${file.jarVersion}")
        {
            using var manifest = open("META-INF/MANIFEST.MF");
            if (manifest is not null)
            {
                using var contents = new StreamReader(manifest);
                while (contents.ReadLine() is { Length: > 0 } line)
                {
                    if (!line.StartsWith("Implementation-Version:", StringComparison.OrdinalIgnoreCase)) continue;
                    version = line["Implementation-Version:".Length..].Trim();
                    break;
                }
            }
        }

        return new(Clean(primary.GetValueOrDefault("displayName"), 512) ?? Clean(id, 512), Clean(primary.GetValueOrDefault("description"), 4096),
            Details(("加载器", loader), ("加载器要求", global.GetValueOrDefault("loaderVersion")),
                ("模组 ID", id), ("版本", version), ("作者", primary.GetValueOrDefault("authors")),
                ("许可证", global.GetValueOrDefault("license")),
                ("包含模组", mods.Count > 1 ? Join(mods.Select(mod => mod.GetValueOrDefault("modId"))) : null),
                ("依赖", Join(dependencies.Where(dependency => dependency.ModId == id).Select(dependency =>
                    LabelValue(dependency.Fields.GetValueOrDefault("modId"), dependency.Fields.GetValueOrDefault("versionRange"))))),
                ("主页", primary.GetValueOrDefault("displayURL")), ("问题反馈", global.GetValueOrDefault("issueTrackerURL"))),
            FindIcon(exists, primary.GetValueOrDefault("logoFile") ?? global.GetValueOrDefault("logoFile"), id));
    }

    private static string? ReadTomlString(StreamReader reader, string source)
    {
        if (source.Length == 0 || source[0] is not ('"' or '\'')) return null;
        var quote = source[0];
        var triple = new string(quote, 3);
        var multiline = source.StartsWith(triple, StringComparison.Ordinal);
        var delimiter = multiline ? triple : quote.ToString();
        var line = source[delimiter.Length..];
        var value = new StringBuilder();
        do
        {
            for (var index = 0; index < line.Length; index++)
            {
                if (line.AsSpan(index).StartsWith(delimiter, StringComparison.Ordinal))
                    return value.ToString();
                var character = line[index];
                if (quote == '"' && character == '\\')
                {
                    if (++index == line.Length) break;
                    character = line[index] switch
                    {
                        'n' => '\n', 'r' => '\r', 't' => '\t', 'b' => '\b', 'f' => '\f', _ => line[index]
                    };
                    if (line[index] is 'u' or 'U')
                    {
                        var digits = line[index] == 'u' ? 4 : 8;
                        if (index + digits < line.Length &&
                            int.TryParse(line.AsSpan(index + 1, digits), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var codepoint) &&
                            Rune.TryCreate(codepoint, out var rune))
                        {
                            value.Append(rune.ToString());
                            index += digits;
                            continue;
                        }
                    }
                }
                value.Append(character);
            }
            if (!multiline) return null;
            if (value.Length > 0 && !(quote == '"' && line.EndsWith('\\'))) value.Append('\n');
            line = reader.ReadLine();
        } while (line is not null);
        return null;
    }

    private static InstanceContentMetadata ReadShader(Func<string, Stream?> open, Func<string, bool> exists)
    {
        var pack = ReadPack(open);
        using var properties = open("shaders/shaders.properties");
        var details = new List<InstanceContentDetail>(pack.Details ?? []);
        if (properties is not null)
        {
            using var reader = new StreamReader(properties);
            var profiles = new List<string>();
            while (reader.ReadLine() is { } line && profiles.Count < 20)
            {
                var text = line.TrimStart();
                var separator = text.IndexOf('=');
                if (separator > 8 && text.StartsWith("profile.", StringComparison.Ordinal))
                    profiles.Add(text[8..separator].Trim());
            }
            if (Join(profiles) is { } label) details.Add(new("光影预设", label));
        }
        return pack with
        {
            Details = details,
            IconPath = FindIcon(exists, null, null, "preview.png", "thumbnail.png", "preview.jpg", "preview.jpeg", "preview.webp",
                "logo.png", "shaders/icon.png", "shaders/preview.png")
        };
    }

    private static InstanceContentMetadata ReadPack(Func<string, Stream?> open)
    {
        var root = ReadJson(open, "pack.mcmeta") ?? default;
        var pack = Property(root, "pack");
        var modernRange = Range(VersionParts(Property(pack, "min_format")), VersionParts(Property(pack, "max_format")));
        var supported = Property(pack, "supported_formats");
        var legacyRange = supported.ValueKind switch
        {
            JsonValueKind.Array => string.Join(" – ", Items(supported).Select(Number).Where(value => value is not null).Take(2)),
            JsonValueKind.Object => Range(Number(Property(supported, "min_inclusive")), Number(Property(supported, "max_inclusive"))),
            _ => Number(supported)
        };
        var description = new StringBuilder();
        AppendMinecraftText(Property(pack, "description"), description);
        return new(Text(pack, "name") ?? Text(root, "name"), Clean(description.ToString(), 4096), Details(
            ("资源包格式", Number(Property(pack, "pack_format"))), ("兼容格式", modernRange ?? legacyRange)));
    }

    private static string? VersionParts(JsonElement format) =>
        format.ValueKind == JsonValueKind.Array
            ? string.Join(".", Items(format).Select(Number).Where(value => value is not null).Take(2))
            : Number(format);

    private static string? Range(string? minimum, string? maximum) => (Clean(minimum), Clean(maximum)) switch
    {
        ({ } from, { } to) when from != to => $"{from} – {to}",
        ({ } value, _) => value,
        (_, var value) => value
    };

    private static void AppendMinecraftText(JsonElement part, StringBuilder result)
    {
        if (result.Length >= 4096) return;
        if (part.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in part.EnumerateArray()) AppendMinecraftText(child, result);
        }
        else if (part.ValueKind == JsonValueKind.Object)
        {
            Append(StringValue(Property(part, "text")) ?? StringValue(Property(part, "fallback")) ??
                StringValue(Property(part, "translate")) ?? StringValue(Property(part, "keybind")), result);
            AppendMinecraftText(Property(part, "extra"), result);
        }
        else
        {
            Append(StringValue(part) ?? Number(part), result);
        }
    }

    private static void Append(string? value, StringBuilder result)
    {
        if (value is not null) result.Append(value.AsSpan(0, Math.Min(value.Length, 4096 - result.Length)));
    }

    private static JsonElement? ReadJson(Func<string, Stream?> open, string path)
    {
        using var stream = open(path);
        if (stream is null) return null;
        try
        {
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            return document.RootElement.Clone();
        }
        catch (JsonException error)
        {
            throw new InvalidDataException($"无法读取 {path} 中的资源信息。", error);
        }
    }

    private static string? Icon(JsonElement icon) => Text(icon) ?? Properties(icon)
        .Select(entry => (Size: int.TryParse(entry.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var size) ? size : 0,
            Path: Text(entry.Value)))
        .Where(entry => entry.Size > 0 && entry.Path is not null)
        .OrderBy(entry => entry.Size < 64)
        .ThenBy(entry => Math.Abs(entry.Size - 64))
        .Select(entry => entry.Path)
        .FirstOrDefault();

    private static string? FindIcon(Func<string, bool> exists, string? path, string? modId = null, params string[] alternatives)
    {
        if (path?.StartsWith('/') == true) path = path[1..]; // Some mod loaders treat the initial slash as the JAR root.
        foreach (var candidate in new[] { path, modId is null ? null : $"assets/{modId}/icon.png", "pack.png", "icon.png" }.Concat(alternatives))
        {
            if (string.IsNullOrEmpty(candidate) || candidate.Contains('\\') || candidate.Contains(':') ||
                candidate.Any(char.IsControl) || candidate.Split('/').Any(part => part is "" or "." or "..")) continue;
            if (exists(candidate)) return candidate;
        }
        return null;
    }

    private static IReadOnlyList<InstanceContentDetail> Details(params (string Label, string? Value)[] fields) =>
        fields.Select(field => (field.Label, Value: Clean(field.Value))).Where(field => field.Value is not null)
            .Select(field => new InstanceContentDetail(field.Label, field.Value!)).ToArray();

    private static string? Clean(string? value, int limit = 1024)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = Regex.Replace(value, "§[0-9a-fk-or]", "", RegexOptions.IgnoreCase).Trim();
        return text.Length > limit ? text[..limit] + "…" : text.Length == 0 ? null : text;
    }

    private static string? LabelValue(string? name, string? value) => name is not null && value is not null ? $"{name} {value}" : name;
    private static string? Join(IEnumerable<string?> values) => Clean(string.Join("、", values.Where(value => !string.IsNullOrWhiteSpace(value)).Take(20)));
    private static string? TextList(JsonElement value) => Join(Items(value).Select(Text));
    private static JsonElement Property(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) ? property : default;
    private static IEnumerable<JsonElement> Items(JsonElement value) =>
        value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : value.ValueKind == JsonValueKind.Undefined ? [] : [value];
    private static IEnumerable<JsonElement> Array(JsonElement value, string name) => Items(Property(value, name));
    private static IEnumerable<JsonProperty> Properties(JsonElement value) =>
        value.ValueKind == JsonValueKind.Object ? value.EnumerateObject() : [];
    private static string? StringValue(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static string? Text(JsonElement value) => Clean(StringValue(value), 4096);
    private static string? Text(JsonElement value, string name) => Text(Property(value, name));
    private static string? Number(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
        ? number.ToString(CultureInfo.InvariantCulture) : null;
}
