// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Wonder.Core;

namespace Wonder.Infrastructure;

public sealed partial class MinecraftDownloadCatalog
{
    private const string OptiFineHost = "https://optifine.net";
    private const string FabricApiProject = "P7dR8mSH";
    private const string QuiltApiProject = "qvIfYCYJ";
    private static readonly TimeSpan OptiFineParseTimeout = TimeSpan.FromSeconds(2);
    // SHA-256 and exact lengths recorded from the official HTTPS release downloads.
    private static readonly Dictionary<string, (string Sha256, int Size)> OptiFineReleases = new(StringComparer.Ordinal)
    {
        ["OptiFine_1.8.9_HD_U_M5.jar"] = ("2739760A70FCA6AE5C7DC817BCC6283F18F55E38FA23778DA7F387A6B724B2C4", 2585014),
        ["OptiFine_1.12.2_HD_U_G5.jar"] = ("3B0006797771FEB97F2D0D2908AE7C9A78CEFB2E730AF1C2E85D08A839BA271E", 2669107),
        ["OptiFine_1.16.5_HD_U_G8.jar"] = ("3C76BC90ED44BCE567CEE7C20EB2C4361926F23A86E5367D596F6E624D0B494F", 6046996),
        ["OptiFine_1.20.1_HD_U_I6.jar"] = ("0B67CB670AEDF2E55A982F3D52B6D53E46791A0C984AA1D2EE58100FC9BFC650", 7145205),
        ["OptiFine_1.21.11_HD_U_J9.jar"] = ("63A60C48B3370920E96D4C32570D7154D17B3A86654C4F1D1DF418BE668D3903", 8045116)
    };

    public async Task<IReadOnlyList<GameAddonVersion>> GetGameAddonsAsync(GameAddonKind kind, string loader, string minecraftVersion,
        string? loaderVersion, DownloadOptions options, CancellationToken ct = default)
    {
        options.Validate();
        StrictSegment(minecraftVersion);
        loader = loader.ToLowerInvariant();
        if (kind == GameAddonKind.OptiFine)
        {
            if (loader != "vanilla" && (loader != "forge" || string.IsNullOrWhiteSpace(loaderVersion))) return [];
            if (!OptiFineReleases.Keys.Any(file => file.StartsWith($"OptiFine_{minecraftVersion}_", StringComparison.Ordinal))) return [];
            var page = await engine.GetStringAsync(OptiFineHost + "/downloads", options with { Source = DownloadSource.Official }, ct);
            return ReadOptiFineVersions(page, minecraftVersion, loader, loaderVersion);
        }

        var project = AddonProject(kind, loader);
        if (project is null) return [];
        var versions = await GetResourceVersionsAsync(project, minecraftVersion, loader, options, ct);
        return versions.Where(version => IsInstallableApi(version, project, minecraftVersion, loader))
            .OrderByDescending(version => version.ReleaseType == "release")
            .Select(version => new GameAddonVersion(version.Id, version.VersionNumber, kind, version.ReleaseType == "release")).ToArray();
    }

    private static string? AddonProject(GameAddonKind kind, string loader) => (kind, loader) switch
    {
        (GameAddonKind.FabricApi, "fabric") => FabricApiProject,
        (GameAddonKind.QuiltApi, "quilt") => QuiltApiProject,
        _ => null
    };

    private static bool IsInstallableApi(ResourceVersion version, string project, string minecraftVersion, string loader) =>
        version.ProjectId == project && version.GameVersions.Contains(minecraftVersion) && version.Loaders.Contains(loader) &&
        version.Files.Count > 0 && version.Files.All(file => !string.IsNullOrWhiteSpace(file.Hash) && file.Destination.EndsWith(".jar", StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<GameAddonVersion> ReadOptiFineVersions(string html, string minecraftVersion, string loader, string? loaderVersion)
    {
        var result = new List<GameAddonVersion>();
        var filenamePattern = @"\A(?:preview_)?OptiFine_" + Regex.Escape(minecraftVersion) + @"_[A-Za-z0-9_]+\.jar\z";
        foreach (Match row in Regex.Matches(html, @"<tr\b[^>]*>.*?</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline, OptiFineParseTimeout))
        {
            var mirror = Regex.Match(row.Value, "<td\\s+class=['\"]colMirror['\"]>\\s*<a\\s+href=['\"](?<url>[^'\"]+)['\"]", RegexOptions.IgnoreCase, OptiFineParseTimeout);
            if (!mirror.Success || !TryOptiFineLink(mirror.Groups["url"].Value, "/adloadx", out var query)) continue;
            if (!query.TryGetValue("f", out var file) || !Regex.IsMatch(file, filenamePattern, RegexOptions.CultureInvariant, OptiFineParseTimeout) ||
                !OptiFineReleases.ContainsKey(file)) continue;
            if (loader == "forge")
            {
                var forge = Regex.Match(row.Value, "<td\\s+class=['\"]colForge['\"]>\\s*Forge\\s+(?<version>[^<]+)</td>", RegexOptions.IgnoreCase, OptiFineParseTimeout);
                if (!forge.Success || !IsListedForgeVersion(forge.Groups["version"].Value.Trim(), minecraftVersion, loaderVersion!)) continue;
            }
            var preview = file.StartsWith("preview_", StringComparison.Ordinal);
            var start = (preview ? "preview_OptiFine_" : "OptiFine_").Length + minecraftVersion.Length + 1;
            var label = "OptiFine " + file[start..^4].Replace('_', ' ');
            result.Add(new(file, label, GameAddonKind.OptiFine, !preview));
        }
        return result.DistinctBy(version => version.Id).OrderByDescending(version => version.Stable).ToArray();
    }

    private static bool IsListedForgeVersion(string listed, string minecraftVersion, string loaderVersion)
    {
        var prefix = minecraftVersion + "-";
        if (loaderVersion.StartsWith(prefix, StringComparison.Ordinal)) loaderVersion = loaderVersion[prefix.Length..];
        var version = loaderVersion.Split('-')[0];
        if (listed.StartsWith('#')) return version.Split('.')[^1] == listed[1..] && listed[1..].All(char.IsAsciiDigit);
        return listed == version;
    }

    private static bool TryOptiFineLink(string link, string path, out Dictionary<string, string> query)
    {
        query = new(StringComparer.Ordinal);
        if (!Uri.TryCreate(new Uri(OptiFineHost + "/"), WebUtility.HtmlDecode(link), out var uri) ||
            uri.Scheme is not ("http" or "https") || uri.Host != "optifine.net" || !uri.IsDefaultPort ||
            uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || uri.AbsolutePath != path) return false;
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pieces = part.Split('=', 2);
            if (pieces.Length != 2 || !query.TryAdd(pieces[0], Uri.UnescapeDataString(pieces[1]))) return false;
        }
        return true;
    }

    private async Task<string> InstallGameAddons(GameInstallRequest request, string version, string root, string javaPath,
        DownloadOptions options, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        request = request with { Loader = request.Loader.ToLowerInvariant() };
        if (request.FabricApiVersion is { } fabric) await InstallApiAddon(GameAddonKind.FabricApi, fabric, request, root, options, progress, ct);
        if (request.QuiltApiVersion is { } quilt) await InstallApiAddon(GameAddonKind.QuiltApi, quilt, request, root, options, progress, ct);
        if (request.OptiFineVersion is not { } optiFine) return version;
        if (!OptiFineReleases.TryGetValue(optiFine, out var trusted))
            throw new InvalidDataException("所选 OptiFine 版本没有内置 SHA-256，无法自动安装。");

        var matches = await GetGameAddonsAsync(GameAddonKind.OptiFine, request.Loader, request.MinecraftVersion, request.LoaderVersion, options, ct);
        if (matches.All(item => item.Id != optiFine)) throw new InvalidDataException("所选 OptiFine 与当前游戏或加载器版本不兼容，请重新选择。");
        var url = await GetOptiFineDownloadUrl(optiFine, options, ct);
        var isForge = request.Loader == "forge";
        var installer = SafePath(root, (isForge ? "mods/" : "installer/") + optiFine);
        await engine.DownloadAsync(new(url, installer, trusted.Sha256, "SHA256", trusted.Size), options with { Source = DownloadSource.Official }, progress, ct);
        using var archive = ZipFile.OpenRead(installer);
        if (archive.GetEntry("optifine/Installer.class") is null) throw new InvalidDataException("OptiFine 下载文件缺少官方安装器。");
        if (isForge)
        {
            if (archive.GetEntry("optifine/OptiFineForgeTweaker.class") is null &&
                archive.GetEntry("META-INF/services/cpw.mods.modlauncher.api.ITransformationService") is null)
                throw new InvalidDataException("OptiFine 下载文件缺少 Forge 加载入口。");
            return version;
        }
        return await InstallStandaloneOptiFine(archive, installer, optiFine, version, root, javaPath, options, progress, ct);
    }

    private async Task InstallApiAddon(GameAddonKind kind, string id, GameInstallRequest request, string root,
        DownloadOptions options, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var name = kind == GameAddonKind.FabricApi ? "Fabric API" : "Quilted Fabric API / Quilt Standard Libraries";
        var project = AddonProject(kind, request.Loader) ?? throw new InvalidDataException($"{name} 与当前加载器不兼容。");
        var versions = await GetResourceVersionsAsync(project, request.MinecraftVersion, request.Loader, options, ct);
        var version = versions.FirstOrDefault(item => item.Id == id && IsInstallableApi(item, project, request.MinecraftVersion, request.Loader))
            ?? throw new InvalidDataException($"找不到所选 {name} 版本，请重新选择。");
        var resource = new ResourceProject(project, name, "", DownloadResourceKind.Mod);
        await InstallResourceAsync(new(resource, version, root, request.MinecraftVersion, request.Loader), options, progress, ct);
    }

    private async Task<string> GetOptiFineDownloadUrl(string file, DownloadOptions options, CancellationToken ct)
    {
        var escaped = Escape(file);
        var page = await engine.GetStringAsync(OptiFineHost + "/adloadx?f=" + escaped, options with { Source = DownloadSource.Official }, ct);
        foreach (Match link in Regex.Matches(page, "href\\s*=\\s*['\"](?<url>[^'\"]+)['\"]", RegexOptions.IgnoreCase, OptiFineParseTimeout))
        {
            if (!TryOptiFineLink(link.Groups["url"].Value, "/downloadx", out var query) ||
                !query.TryGetValue("f", out var filename) || filename != file || !query.TryGetValue("x", out var token) ||
                !Regex.IsMatch(token, @"\A[A-Za-z0-9_-]{1,128}\z", RegexOptions.CultureInvariant, OptiFineParseTimeout)) continue;
            return OptiFineHost + "/downloadx?f=" + escaped + "&x=" + token;
        }
        throw new InvalidDataException("OptiFine 官网未返回对应的下载链接，请重试。");
    }

    private async Task<string> InstallStandaloneOptiFine(ZipArchive archive, string installer, string filename, string minecraft,
        string root, string javaPath, DownloadOptions options, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        if (archive.GetEntry("optifine/Patcher.class") is null || archive.GetEntry("optifine/OptiFineTweaker.class") is null)
            throw new InvalidDataException("此 OptiFine 安装包没有原版补丁入口。");
        var build = filename[(filename.StartsWith("preview_", StringComparison.Ordinal) ? "preview_OptiFine_" : "OptiFine_").Length..^4];
        var coordinate = "optifine:OptiFine:" + build;
        var library = SafePath(root, "libraries/" + LaunchArguments.Maven(coordinate));
        var wrapper = await InstallOptiFineLaunchWrapper(archive, root, options, progress, ct);
        var vanillaJar = SafePath(root, "versions/" + minecraft + "/" + minecraft + ".jar");
        Directory.CreateDirectory(Path.GetDirectoryName(library)!);
        var patched = library + ".tmp";
        try
        {
            File.Delete(patched);
            progress?.Report(new("应用 OptiFine 客户端补丁"));
            await RunOptiFinePatcher(javaPath, root, ["-cp", installer, "optifine.Patcher", vanillaJar, installer, patched], ct);
            using (var output = ZipFile.OpenRead(patched))
                if (output.GetEntry("optifine/OptiFineTweaker.class") is null) throw new InvalidDataException("OptiFine 补丁输出缺少启动入口。");
            File.Move(patched, library, true);
        }
        finally { File.Delete(patched); }

        var id = StrictSegment(minecraft + "-OptiFine-" + build[(minecraft.Length + 1)..]);
        var profile = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["id"] = id, ["inheritsFrom"] = minecraft, ["mainClass"] = "net.minecraft.launchwrapper.Launch",
            ["+tweakers"] = new[] { "optifine.OptiFineTweaker" }, ["libraries"] = new[] { new { name = wrapper }, new { name = coordinate } }
        });
        await WriteJson(SafePath(root, "versions/" + id + "/" + id + ".json"), profile, ct);
        return id;
    }

    private async Task<string> InstallOptiFineLaunchWrapper(ZipArchive archive, string root, DownloadOptions options,
        IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        if (archive.GetEntry("launchwrapper-of.txt") is { } descriptor)
        {
            using var reader = new StreamReader(new MemoryStream(await ArchiveEntryContent.ReadAsync(descriptor, 256, ct)));
            var version = (await reader.ReadToEndAsync(ct)).Trim();
            if (!Regex.IsMatch(version, @"\A\d+(\.\d+)+\z", RegexOptions.CultureInvariant, OptiFineParseTimeout))
                throw new InvalidDataException("OptiFine 启动库版本描述无效。");
            var entry = archive.GetEntry($"launchwrapper-of-{version}.jar") ?? throw new InvalidDataException("OptiFine 安装包缺少启动库。");
            var name = "optifine:launchwrapper-of:" + version;
            await ExtractInstallerEntry(entry, SafePath(root, "libraries/" + LaunchArguments.Maven(name)), ct);
            return name;
        }
        const string standard = "net.minecraft:launchwrapper:1.12";
        var path = LaunchArguments.Maven(standard);
        await engine.DownloadAsync(new("https://libraries.minecraft.net/" + path, SafePath(root, "libraries/" + path),
            "111e7bea9c968cdb3d06ef4632bf7ff0824d0f36"), options, progress, ct);
        return standard;
    }

    private static async Task RunOptiFinePatcher(string java, string root, string[] arguments, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        using var process = new Process { StartInfo = JavaRuntime.StartInfo(java, root, arguments) };
        foreach (var name in new[] { "_JAVA_OPTIONS", "JAVA_TOOL_OPTIONS", "JDK_JAVA_OPTIONS", "CLASSPATH" }) process.StartInfo.Environment.Remove(name);
        process.Start();
        var stdout = Drain(process.StandardOutput, timeout.Token);
        var stderr = Drain(process.StandardError, timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); await Task.WhenAll(stdout, stderr); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            try { await Task.WhenAll(stdout, stderr); } catch (OperationCanceledException) { }
            ct.ThrowIfCancellationRequested();
            throw new TimeoutException("OptiFine 客户端补丁处理超时。");
        }
        if (process.ExitCode != 0) throw new InvalidDataException("OptiFine 客户端补丁失败：" + await stderr + "\n" + await stdout);
    }
}
