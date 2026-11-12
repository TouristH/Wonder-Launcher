// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wonder.Extensibility;

public sealed record PluginReportFile(string Path, string Sha256);
public sealed record PluginReport(int SchemaVersion, string AnalyzerVersion, PluginReportFile[] Files,
    IReadOnlyList<string> Findings, IReadOnlyList<string> Limitations);

/// <summary>Deterministic publisher report; a registry must regenerate it from the submitted package.</summary>
public static class PluginPackageReport
{
    public const string FileName = "permissions.json";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true, AllowDuplicateProperties = false, PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static PluginReport Generate(string directory)
    {
        var files = PluginAnalyzer.EnumerateFiles(directory)
            .Where(path => !string.Equals(Path.GetRelativePath(directory, path), FileName, StringComparison.Ordinal))
            .Select(path =>
            {
                using var stream = File.OpenRead(path);
                return new PluginReportFile(Path.GetRelativePath(directory, path).Replace('\\', '/'),
                    Convert.ToHexStringLower(SHA256.HashData(stream)));
            }).ToArray();
        var analysis = PluginAnalyzer.Analyze(directory);
        return new(1, PluginAnalyzer.Version, files, analysis.Findings, analysis.Limitations);
    }

    public static void Write(string directory) => File.WriteAllText(Path.Combine(directory, FileName), Serialize(Generate(directory)));
    public static string Serialize(PluginReport report) => JsonSerializer.Serialize(report, Json) + "\n";

    public static void Verify(string directory)
    {
        foreach (var name in new[] { "plugin.json", "LICENSE", "RISK.md", FileName })
        {
            var path = Path.Combine(directory, name);
            if (!File.Exists(path) || new FileInfo(path).Length is <= 0 or > 1024 * 1024)
                throw new InvalidDataException($"发布包必须包含非空且不超过 1 MiB 的 {name}。");
        }
        // Parse strictly, then compare the contents rather than JSON whitespace/property ordering.
        var supplied = JsonSerializer.Deserialize<PluginReport>(File.ReadAllText(Path.Combine(directory, FileName)), Json)
            ?? throw new InvalidDataException("权限报告为空。");
        if (Serialize(supplied) != Serialize(Generate(directory)))
            throw new InvalidDataException("权限报告与实际文件或当前分析器不一致，请重新生成 permissions.json。");
    }
}
