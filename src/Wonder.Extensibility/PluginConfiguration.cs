// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using System.Text.Json;
using System.Text.Encodings.Web;

namespace Wonder.Extensibility;

public sealed record PluginConfigurationSchema
{
    public int Version { get; init; } = 1;
    public PluginConfigurationField[] Fields { get; init; } = [];
}

public sealed record PluginConfigurationField
{
    public string Key { get; init; } = "";
    public string Label { get; init; } = "";
    public string Description { get; init; } = "";
    public string Type { get; init; } = "string";
    public JsonElement Default { get; init; }
    public decimal? Minimum { get; init; }
    public decimal? Maximum { get; init; }
    public int? MaxLength { get; init; }
    public string[]? Options { get; init; }
}

public static class PluginConfiguration
{
    private static readonly JsonSerializerOptions ReadableJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string CreateDefaults(PluginConfigurationSchema schema)
    {
        ValidateSchema(schema);
        return JsonSerializer.Serialize(schema.Fields.ToDictionary(field => field.Key, field => field.Default), ReadableJson) + "\n";
    }

    internal static string AddMissingDefaults(PluginConfigurationSchema schema, string json)
    {
        ValidateSchema(schema);
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowDuplicateProperties = false });
            if (document.RootElement.ValueKind != JsonValueKind.Object) return json;
            var values = document.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);
            var changed = false;
            foreach (var field in schema.Fields)
                changed |= values.TryAdd(field.Key, field.Default);
            return changed ? JsonSerializer.Serialize(values, ReadableJson) + "\n" : json;
        }
        catch (JsonException) { return json; }
    }

    public static void ValidateSchema(PluginConfigurationSchema schema)
    {
        if (schema is not { Version: 1, Fields: { Length: > 0 and <= 64 } })
            throw new InvalidDataException("插件配置声明的版本或字段数量无效。");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in schema.Fields)
        {
            if (field is null || string.IsNullOrWhiteSpace(field.Key) || field.Key.Length > 80 ||
                !field.Key.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.') ||
                !keys.Add(field.Key) || string.IsNullOrWhiteSpace(field.Label) || field.Label.Length > 256 ||
                field.Description is null || field.Description.Length > 2048 ||
                field.Type is not ("string" or "boolean" or "integer" or "number" or "choice") ||
                field.Minimum > field.Maximum || field.MaxLength is < 0 or > 65536 ||
                field.Type is not ("number" or "integer") && (field.Minimum is not null || field.Maximum is not null) ||
                field.Type != "string" && field.MaxLength is not null ||
                field.Type == "choice" && (field.Options is not { Length: > 0 and <= 128 } options ||
                    options.Any(option => option is null || option.Length > 256) || options.Distinct(StringComparer.Ordinal).Count() != options.Length) ||
                field.Type != "choice" && field.Options is not null)
                throw new InvalidDataException("插件配置字段声明无效：" + field?.Key);
            ValidateValue(field, field.Default);
        }
    }

    public static void Validate(PluginConfigurationSchema schema, string json)
    {
        ValidateSchema(schema);
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowDuplicateProperties = false });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("插件配置必须是 JSON 对象。");
            foreach (var field in schema.Fields)
            {
                if (!document.RootElement.TryGetProperty(field.Key, out var value))
                    throw new InvalidDataException("插件配置缺少字段：" + field.Label + "（" + field.Key + "）。");
                ValidateValue(field, value);
            }
        }
        catch (JsonException error) { throw new InvalidDataException("插件配置不是有效 JSON。", error); }
    }

    private static void ValidateValue(PluginConfigurationField field, JsonElement value)
    {
        var valid = field.Type switch
        {
            "string" => value.ValueKind == JsonValueKind.String && value.GetString()!.Length <= (field.MaxLength ?? 65536),
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "choice" => value.ValueKind == JsonValueKind.String && field.Options!.Contains(value.GetString(), StringComparer.Ordinal),
            "integer" or "number" => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number) &&
                (field.Type != "integer" || decimal.Truncate(number) == number) &&
                (field.Minimum is null || number >= field.Minimum) && (field.Maximum is null || number <= field.Maximum),
            _ => false
        };
        if (!valid) throw new InvalidDataException("插件配置字段的类型、范围或选项无效：" + field.Label + "（" + field.Key + "）。");
    }

    internal static bool Equivalent(PluginConfigurationSchema? left, PluginConfigurationSchema? right) =>
        JsonSerializer.Serialize(left, ReadableJson) == JsonSerializer.Serialize(right, ReadableJson);
}
