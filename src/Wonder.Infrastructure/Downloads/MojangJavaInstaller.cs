// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using System.Runtime.InteropServices;
using System.Text.Json;
using Wonder.Application;
using Wonder.Core;
using static Wonder.Infrastructure.LocalFiles;

namespace Wonder.Infrastructure;

public static class MojangJavaInstaller
{
    private const string IndexUrl = "https://piston-meta.mojang.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json";

    public static async Task<string> InstallAsync(IDownloadEngine engine, JsonElement versionMetadata, string gameRoot,
        DownloadOptions options, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var component = versionMetadata.Get("javaVersion").Get("component").Text() ?? "jre-legacy";
        var arch = RuntimeInformation.ProcessArchitecture;
        var platform = OperatingSystem.IsWindows() ? arch switch { Architecture.X64 => "windows-x64", Architecture.X86 => "windows-x86", Architecture.Arm64 => "windows-arm64", _ => "" }
            : OperatingSystem.IsMacOS() ? arch == Architecture.Arm64 ? "mac-os-arm64" : "mac-os"
            : arch == Architecture.X64 ? "linux" : arch == Architecture.X86 ? "linux-i386" : "";
        progress?.Report(new("获取适配的 Java 运行环境"));
        var official = options with { Source = DownloadSource.Official };
        using var index = JsonDocument.Parse(await engine.GetStringAsync(IndexUrl, official, ct));
        var candidate = index.RootElement.Get(platform).Get(component).Items().FirstOrDefault();
        var descriptor = candidate.Get("manifest");
        var url = descriptor.Get("url").Text();
        if (url is null) throw new InvalidDataException($"Mojang 未提供当前平台所需的 {component}，请指定已安装的 Java。");
        var manifestUrl = DownloadSourceResolver.RequireUrl(url, "piston-meta.mojang.com", "launchermeta.mojang.com");
        var manifestText = await engine.GetStringAsync(manifestUrl.AbsoluteUri, official, ct);
        MinecraftDownloadCatalog.VerifyMetadata(manifestText, descriptor.Get("sha1").Text(), Size(descriptor));
        using var manifest = JsonDocument.Parse(manifestText);
        var files = manifest.RootElement.Get("files");
        if (files.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Java 运行环境文件列表无效。");
        var root = Path.Combine(gameRoot, "runtime");
        var downloads = new List<DownloadFile>();
        var executables = new List<string>();
        var links = new List<(string Path, string Target)>();
        var directories = new List<string>();
        foreach (var file in files.EnumerateObject())
        {
            var value = file.Value;
            var linkTarget = value.Get("type").Text() == "link" ? value.Get("target").Text() : null;
            var destination = SafeRuntimePath(root, file.Name, linkTarget);
            switch (value.Get("type").Text())
            {
                case "directory": directories.Add(destination); break;
                case "file":
                    var raw = value.Get("downloads").Get("raw");
                    var algorithm = raw.Get("sha256").Text() is null ? "SHA1" : "SHA256";
                    var hash = raw.Get(algorithm.ToLowerInvariant()).Text();
                    if (hash is null || hash.Length != (algorithm == "SHA256" ? 64 : 40) || !hash.All(char.IsAsciiHexDigit))
                        throw new InvalidDataException("Java 文件缺少有效的完整性校验值。");
                    var address = DownloadSourceResolver.RequireUrl(raw.Get("url").Text(), "piston-data.mojang.com", "launcher.mojang.com");
                    downloads.Add(new(address.AbsoluteUri, destination, hash, algorithm, Size(raw)));
                    if (value.Get("executable").Boolean() == true) executables.Add(destination);
                    break;
                case "link":
                    var target = value.Get("target").Text() ?? throw new InvalidDataException("Java 链接目标为空。");
                    var resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(destination)!, target));
                    if (!IsWithin(root, resolved)) throw new InvalidDataException("Java 链接目标越界。");
                    links.Add((destination, target)); break;
                default: throw new InvalidDataException("不支持的 Java 文件类型。");
            }
        }
        foreach (var directory in directories) Directory.CreateDirectory(directory);
        // Mojang currently publishes SHA-1 for these files. Only SHA-256 files may use mirrors.
        foreach (var group in downloads.GroupBy(x => x.HashAlgorithm))
            await engine.DownloadManyAsync(group.ToArray(), group.Key == "SHA256" ? options : official, progress, ct);
        if (!OperatingSystem.IsWindows())
        {
            foreach (var path in executables) File.SetUnixFileMode(path, File.GetUnixFileMode(path) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            foreach (var link in links)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(link.Path)!);
                if (File.Exists(link.Path) || Directory.Exists(link.Path)) continue;
                var resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(link.Path)!, link.Target));
                if (Directory.Exists(resolved)) Directory.CreateSymbolicLink(link.Path, link.Target);
                else File.CreateSymbolicLink(link.Path, link.Target);
            }
        }
        var name = OperatingSystem.IsWindows() ? "java.exe" : "java";
        return downloads.Select(x => x.Destination).FirstOrDefault(x => Path.GetFileName(x) == name && Path.GetFileName(Path.GetDirectoryName(x)) == "bin")
            ?? throw new InvalidDataException("Java 运行环境缺少可执行文件。");
    }

    private static long Size(JsonElement value) => value.Get("size") is { ValueKind: JsonValueKind.Number } node && node.TryGetInt64(out var size) && size >= 0
        ? size : throw new InvalidDataException("Java 下载清单缺少有效的文件大小。");

    private static bool IsWithin(string root, string path) => Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, WonderPath.Comparison);

    private static string SafeRuntimePath(string root, string relative, string? expectedLinkTarget = null)
    {
        if (Path.IsPathRooted(relative) || relative.Contains('\\') || relative.Contains(':') || relative.Split('/').Any(x => x is "" or "." or ".." || x.Any(char.IsControl)))
            throw new InvalidDataException("Java 文件路径越界。");
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!IsWithin(root, path)) throw new InvalidDataException("Java 文件路径越界。");
        // Never write through a pre-existing symlink in a resumed staging directory.
        var fullRoot = Path.GetFullPath(root);
        for (var current = path; current is not null && (WonderPath.Comparer.Equals(fullRoot, current) || IsWithin(root, current)); current = Path.GetDirectoryName(current))
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(current); }
            catch (FileNotFoundException) { continue; }
            catch (DirectoryNotFoundException) { continue; }
            if ((attributes & FileAttributes.ReparsePoint) == 0) continue;
            // A resumed macOS/Linux runtime may already contain links from this very manifest.
            // Permit only that exact declared link; reject redirected parent directories.
            var actualTarget = new FileInfo(current).LinkTarget;
            if (current == path && expectedLinkTarget is not null && actualTarget is not null)
            {
                var parent = Path.GetDirectoryName(current)!;
                var expected = Path.GetFullPath(Path.Combine(parent, expectedLinkTarget));
                if (IsWithin(root, expected) && WonderPath.Comparer.Equals(expected, Path.GetFullPath(Path.Combine(parent, actualTarget)))) continue;
            }
            throw new InvalidDataException("Java 安装目录包含不匹配的符号链接。");
        }
        return path;
    }
}
