// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using static Wonder.Infrastructure.LocalFiles;

namespace Wonder.Infrastructure;

internal static class LaunchArguments
{
    // Parse a list of arguments, never a shell command. Preserve Windows backslashes.
    public static List<string> Split(string? text)
    {
        var result = new List<string>(); var value = new StringBuilder(); char quote = '\0'; var started = false;
        text ??= "";
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\\')
            {
                var start = i; while (i + 1 < text.Length && text[i + 1] == '\\') i++;
                var count = i - start + 1;
                if (i + 1 < text.Length && text[i + 1] == '"' && quote != '\'')
                {
                    value.Append('\\', count / 2); i++;
                    if (count % 2 == 1) value.Append('"'); else quote = quote == '"' ? '\0' : '"';
                }
                else value.Append('\\', count);
                started = true;
            }
            else if (c is '\'' or '"' && (quote == '\0' || quote == c)) { quote = quote == c ? '\0' : c; started = true; }
            else if (char.IsWhiteSpace(c) && quote == '\0') { if (started) { result.Add(value.ToString()); value.Clear(); started = false; } }
            else { value.Append(c); started = true; }
        }
        if (quote != '\0') throw new InvalidDataException("参数的引号没有闭合。");
        if (started) result.Add(value.ToString());
        return result;
    }
    public static bool Allowed(JsonElement item, string architecture, IReadOnlyDictionary<string, bool> features)
    {
        var rules = item.Get("rules");
        if (rules.ValueKind == JsonValueKind.Undefined) return true;
        var allowed = false;
        var osName = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "osx" : "linux";
        foreach (var rule in rules.Items())
        {
            var os = rule.Get("os");
            if (os.Get("name").Text() is { } name && name != osName) continue;
            if (os.Get("arch").Text() is { } arch && !Matches(architecture, arch)) continue;
            if (os.Get("version").Text() is { } version && !Matches(Environment.OSVersion.Version.ToString(), version)) continue;
            var expected = rule.Get("features");
            if (expected.ValueKind == JsonValueKind.Object && expected.EnumerateObject().Any(p => features.GetValueOrDefault(p.Name) != p.Value.Boolean())) continue;
            allowed = rule.Get("action").Text() switch { "allow" => true, "disallow" => false, _ => throw new InvalidDataException("未知的启动规则。") };
        }
        return allowed;
    }
    private static bool Matches(string text, string regex)
    {
        try { return Regex.IsMatch(text, regex, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)); }
        catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException) { throw new InvalidDataException("启动规则中的表达式无效。", ex); }
    }
    public static IEnumerable<string> Values(JsonElement array, string architecture, IReadOnlyDictionary<string, bool> features)
    {
        foreach (var entry in array.Items())
        {
            if (entry.Text() is { } text) { yield return text; continue; }
            if (!Allowed(entry, architecture, features)) continue;
            var value = entry.Get("value");
            if (value.Text() is { } single) yield return single;
            else foreach (var part in value.Items()) yield return part.Text() ?? throw new InvalidDataException("启动参数必须是文本。");
        }
    }
    public static string Expand(string value, IReadOnlyDictionary<string, string> variables) => Regex.Replace(value, @"\$\{([^}]+)\}", match =>
        variables.TryGetValue(match.Groups[1].Value, out var replacement) ? replacement : throw new InvalidDataException("无法解析启动参数：" + match.Value));
    public static string Maven(string coordinate, string? classifier = null)
    {
        var extensionParts = coordinate.Split('@');
        var parts = extensionParts[0].Split(':');
        if (parts.Length is < 3 or > 4) throw new InvalidDataException("无效的依赖库坐标：" + coordinate);
        foreach (var part in parts) Segment(part);
        classifier ??= parts.Length == 4 ? parts[3] : null;
        if (classifier is not null) Segment(classifier);
        var extension = extensionParts.Length == 1 ? "jar" : Segment(extensionParts[1]);
        return parts[0].Replace('.', '/') + "/" + parts[1] + "/" + parts[2] + "/" + parts[1] + "-" + parts[2] + (classifier is null ? "" : "-" + classifier) + "." + extension;
    }
    public static string LibraryKey(JsonElement lib)
    {
        var name = lib.Get("name").Text() ?? throw new InvalidDataException("依赖库缺少坐标。");
        var parts = name.Split(':');
        return parts.Length < 3 ? name : parts[0] + ":" + parts[1] + ":" + (parts.Length > 3 ? parts[3] : "");
    }
}
