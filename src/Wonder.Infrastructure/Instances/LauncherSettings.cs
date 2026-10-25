// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Text.Json;
using Wonder.Core;
using static Wonder.Infrastructure.LocalFiles;

namespace Wonder.Infrastructure;

internal static class LauncherSettings
{
    public static (InstanceSettings, string) Hmcl(JsonElement json, bool modern, string root, string version, List<string> warnings)
    {
        var game = root;
        if (modern)
        {
            var overrides = json.Get("overrideProperties").Items().Select(x => x.Text()).ToHashSet(StringComparer.Ordinal);
            JsonElement preset = default;
            if (json.Get("parent").Text() is { } parent)
            {
                foreach (var basePath in new[] { root, Path.GetDirectoryName(root)! })
                    foreach (var relative in new[] { "hmcl/config/game-settings.json", ".hmcl/config/game-settings.json", "config/game-settings.json" })
                    {
                        var path = Path.Combine(basePath, relative);
                        if (File.Exists(path)) preset = Json(path).Get("presets").Items().FirstOrDefault(x => x.Get("id").Text() == parent, preset);
                    }
                if (preset.ValueKind == JsonValueKind.Undefined) warnings.Add("未找到 HMCL 父预设，继承项使用 Wonder 默认值；仅导入明确覆盖的项目。");
            }
            JsonElement Value(string key) => overrides.Contains(key) ? json.Get(key) : preset.Get(key);
            game = InstanceDiscovery.ResolveDirectory(Value("runningDirectory").Text(), root) ?? root;
            if (overrides.Contains("runningDirectory")) game = InstanceDiscovery.ResolveDirectory(Value("runningDirectory").Text(), root) ?? version;
            var javaType = Value("javaType").Text();
            if (javaType is "DETECTED" or "VERSION") warnings.Add("HMCL 的 Java 版本选择策略未复制，请检查 Java 路径。");
            var windowType = Value("windowType").Text();
            return (new()
            {
                JavaPath = javaType == "CUSTOM" ? NonEmpty(Value("customJavaPath").Text()) : null, JvmArguments = Value("jvmOptions").Text(), GameArguments = Value("gameArguments").Text(),
                MinimumMemoryMb = Positive(Value("minMemory").Number()), MaximumMemoryMb = Value("autoMemory").Boolean() == false ? Positive(Value("maxMemory").Number()) : null,
                WindowWidth = Positive(Value("width").Number()), WindowHeight = Positive(Value("height").Number()), FullScreen = windowType == "FULLSCREEN" ? true : windowType == "WINDOWED" ? false : null
            }, game);
        }
        if (json.Get("usesGlobal").Boolean() == true)
        {
            JsonElement inherited = default;
            foreach (var path in new[] { Path.Combine(root, "hmcl.json"), Path.Combine(Path.GetDirectoryName(root)!, "hmcl.json") })
            {
                if (!File.Exists(path)) continue;
                var profiles = Json(path).Get("profiles");
                if (profiles.ValueKind != JsonValueKind.Object) continue;
                foreach (var property in profiles.EnumerateObject())
                {
                    var directory = InstanceDiscovery.ResolveDirectory(property.Value.Get("gameDir").Text(), Path.GetDirectoryName(path)!);
                    if (string.Equals(Path.TrimEndingDirectorySeparator(directory ?? ""), Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase))
                        inherited = property.Value.Get("global");
                }
            }
            if (inherited.ValueKind == JsonValueKind.Object) json = inherited;
            else { warnings.Add("HMCL 使用全局配置，但未找到与此游戏目录匹配的 global 设置；继承项使用 Wonder 默认值。"); return (new(), game); }
        }
        if (json.Get("gameDirType").Number() == 1) game = version;
        else if (json.Get("gameDirType").Number() == 2) game = InstanceDiscovery.ResolveDirectory(json.Get("gameDir").Text(), root) ?? root;
        var automatic = json.Get("autoMemory").Boolean() != false;
        if (automatic) warnings.Add("HMCL 自动内存策略未复制；Wonder 使用默认内存，可手动覆盖。");
        var javaMode = json.Get("javaVersionType");
        var customJava = javaMode.Number() == 4 || javaMode.Text()?.Equals("CUSTOM", StringComparison.OrdinalIgnoreCase) == true || json.Get("java").Text() == "Custom";
        var detectedJava = javaMode.Number() == 3 || javaMode.Text()?.Equals("DETECTED", StringComparison.OrdinalIgnoreCase) == true;
        if (javaMode.Number() == 2 || javaMode.Text()?.Equals("VERSION", StringComparison.OrdinalIgnoreCase) == true) warnings.Add("HMCL 的 Java 版本选择策略未复制，请检查 Java 路径。");
        return (new()
        {
            JavaPath = NonEmpty(customJava ? json.Get("javaDir").Text() : detectedJava ? json.Get("defaultJavaPath").Text() : null), JvmArguments = json.Get("javaArgs").Text(), GameArguments = json.Get("minecraftArgs").Text(),
            MinimumMemoryMb = Positive(json.Get("minMemory").Number()), MaximumMemoryMb = automatic ? null : Positive(json.Get("maxMemory").Number()),
            WindowWidth = Positive(json.Get("width").Number()), WindowHeight = Positive(json.Get("height").Number()), FullScreen = json.Get("fullscreen").Boolean()
        }, game);
    }

    public static (InstanceSettings, string) Pcl(string root, string version, List<string> warnings)
    {
        var local = Ini(Path.Combine(version, "PCL", "Setup.ini"), ':');
        var global = Ini(Path.Combine(root, "PCL", "Setup.ini"), ':');
        if (global.Count == 0) global = Ini(Path.Combine(Path.GetDirectoryName(root)!, "PCL", "Setup.ini"), ':');
        string? Value(string key, string? globalKey = null) => NonEmpty(local.GetValueOrDefault(key)) ?? (globalKey is null ? null : NonEmpty(global.GetValueOrDefault(globalKey)));
        int? Integer(string? value) => int.TryParse(value, out var number) ? number : null;
        var ramType = Integer(Value("VersionRamType")) ?? 2;
        var slider = ramType == 1 ? Integer(Value("VersionRamCustom")) : ramType == 2 && Integer(global.GetValueOrDefault("LaunchRamType")) == 1 ? Integer(global.GetValueOrDefault("LaunchRamCustom")) : null;
        int? memory = slider is >= 0 and <= 50 ? (int)Math.Round(1024 * (slider <= 12 ? .1 * slider.Value + .3 : slider <= 25 ? .5 * (slider.Value - 12) + 1.5 : slider <= 33 ? slider.Value - 25 + 8 : 2 * (slider.Value - 33) + 16)) : null;
        if (memory is null) warnings.Add("PCL 自动或全局内存策略未完整还原；Wonder 使用默认内存，可手动覆盖。");
        var game = root;
        if (bool.TryParse(Value("VersionArgumentIndieV2"), out var isolate)) game = isolate ? version : root;
        else if (Integer(Value("VersionArgumentIndie")) is { } old && old is 1 or 2) game = old == 1 ? version : root;
        else warnings.Add("PCL 自动隔离策略未明确；当前使用游戏根目录，请检查游戏目录。");
        string? java = null;
        var jsonPath = Path.Combine(version, "PCL", "config.json");
        if (File.Exists(jsonPath))
        {
            var folder = Json(jsonPath).Get("InstanceForcedJava").Get("Folder").Text();
            if (NonEmpty(folder) is not null) java = Path.Combine(folder!, "java.exe");
        }
        var selectedJava = Value("VersionArgumentJavaSelect", "LaunchArgumentJavaSelect");
        if (java is null && selectedJava?.StartsWith('{') == true)
        {
            using var selected = JsonDocument.Parse(selectedJava);
            var folder = selected.RootElement.Get("Path").Text();
            if (folder is not null) java = folder.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? folder : Path.Combine(folder, "java.exe");
        }
        if (global.Count == 0) warnings.Add("未找到 PCL 可读取的全局 Setup.ini；注册表内的全局项未导入。");
        return (new()
        {
            JavaPath = java, MaximumMemoryMb = memory, JvmArguments = Value("VersionAdvanceJvm", "LaunchAdvanceJvm"),
            GameArguments = Value("VersionAdvanceGame", "LaunchAdvanceGame"),
            WindowWidth = Positive(Integer(global.GetValueOrDefault("LaunchArgumentWindowWidth"))), WindowHeight = Positive(Integer(global.GetValueOrDefault("LaunchArgumentWindowHeight")))
        }, game);
    }
}
