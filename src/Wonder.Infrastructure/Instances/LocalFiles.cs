// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Text.Json;

namespace Wonder.Infrastructure;

internal static class LocalFiles
{
    public static JsonElement Json(string path)
    {
        if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new InvalidDataException("配置文件过大：" + path);
        using var document = JsonDocument.Parse(File.ReadAllBytes(path), new() { MaxDepth = 64, AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        return document.RootElement.Clone();
    }
    public static JsonElement Get(this JsonElement value, string key) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var property) ? property : default;
    public static string? Text(this JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    public static int? Number(this JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number) && number == Math.Truncate(number) && number >= int.MinValue && number <= int.MaxValue ? (int)number : int.TryParse(value.Text(), out var parsed) ? parsed : null;
    public static bool? Boolean(this JsonElement value) => value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : bool.TryParse(value.Text(), out var result) ? result : null;
    public static IEnumerable<JsonElement> Items(this JsonElement value) => value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];
    public static Dictionary<string, string> Ini(string path, char separator = '=')
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return result;
        if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("配置文件过大：" + path);
        foreach (var line in File.ReadLines(path))
        {
            var text = line.Trim();
            if (text.Length == 0 || text[0] is '#' or ';' or '[') continue;
            var index = text.IndexOf(separator);
            if (index > 0) result[text[..index].Trim()] = text[(index + 1)..].Trim();
        }
        return result;
    }
    public static string Under(string root, string relative)
    {
        var path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("配置路径越出所属目录：" + relative);
        return path;
    }
    public static string Segment(string value)
    {
        if (value.Length is 0 or > 240 || value is "." or ".." || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || value.Contains('/') || value.Contains('\\'))
            throw new InvalidDataException("无效的版本或组件标识。");
        return value;
    }
    public static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
    public static int? Positive(int? value) => value > 0 ? value : null;
}
