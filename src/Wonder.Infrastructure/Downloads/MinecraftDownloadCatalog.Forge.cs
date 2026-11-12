// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Wonder.Core;
using static Wonder.Infrastructure.LocalFiles;

namespace Wonder.Infrastructure;

public sealed partial class MinecraftDownloadCatalog
{
    // Interpret the official installer format. No launcher-specific installer helper is bundled.
    private async Task<string> InstallForgeArchive(string installer, string root, string java, JsonElement minecraft,
        DownloadOptions options, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        using var archive = ZipFile.OpenRead(installer);
        var install = await ReadArchiveJson(archive, "install_profile.json", ct);
        var version = install.Get("versionInfo");
        var legacy = version.ValueKind == JsonValueKind.Object;
        if (!legacy) version = await ReadArchiveJson(archive, (install.Get("json").Text() ?? "/version.json").TrimStart('/'), ct);
        var id = StrictSegment(version.Get("id").Text() ?? "");
        var minecraftId = minecraft.Get("id").Text()!;
        if (version.Get("inheritsFrom").Text() != minecraftId || version.Get("mainClass").Text() is null)
            throw new InvalidDataException("Forge / NeoForge 安装描述与 Minecraft 版本不匹配。");
        var remainingBytes = 1024L * 1024 * 1024;
        Task ExtractForgeEntry(ZipArchiveEntry entry, string destination)
        {
            if (entry.Length > remainingBytes) throw new InvalidDataException("加载器内嵌文件过大。");
            remainingBytes -= entry.Length;
            return ExtractInstallerEntry(entry, destination, ct);
        }
        foreach (var entry in archive.Entries.Where(x => x.FullName.StartsWith("maven/", StringComparison.Ordinal) && !x.FullName.EndsWith('/')))
            await ExtractForgeEntry(entry, SafePath(Path.Combine(root, "libraries"), entry.FullName[6..]));
        string? legacyLibrary = null;
        if (legacy)
        {
            var content = install.Get("install");
            var coordinate = content.Get("path").Text() ?? throw new InvalidDataException("旧式 Forge 缺少安装坐标。");
            var embedded = content.Get("filePath").Text() ?? throw new InvalidDataException("旧式 Forge 缺少内嵌文件。");
            var entry = archive.GetEntry(embedded) ?? throw new InvalidDataException("旧式 Forge 内嵌文件不存在。");
            legacyLibrary = SafePath(Path.Combine(root, "libraries"), LaunchArguments.Maven(coordinate));
            await ExtractForgeEntry(entry, legacyLibrary);
        }
        else
        {
            if (install.Get("minecraft").Text() != minecraftId) throw new InvalidDataException("加载器安装器目标版本不匹配。");
            await InstallLibraries(install, root, options, progress, ct, deferGenerated: true);
            await InstallLibraries(version, root, options, progress, ct, deferGenerated: true);
            var libraryRoot = Path.Combine(root, "libraries");
            var variables = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["SIDE"] = "client", ["ROOT"] = root, ["INSTALLER"] = installer,
                ["LIBRARY_DIR"] = libraryRoot, ["MINECRAFT_VERSION"] = minecraftId,
                ["MINECRAFT_JAR"] = Path.Combine(root, "versions", minecraftId, minecraftId + ".jar")
            };
            string Library(string coordinate) => SafePath(libraryRoot, LaunchArguments.Maven(coordinate));
            var data = install.Get("data");
            if (data.ValueKind == JsonValueKind.Object)
                foreach (var item in data.EnumerateObject())
                {
                    var value = item.Value.Get("client").Text() ?? throw new InvalidDataException("安装数据缺少客户端值。");
                    if (value.StartsWith('[') && value.EndsWith(']')) value = Library(value[1..^1]);
                    else if (value.StartsWith('\'') && value.EndsWith('\'')) value = value[1..^1];
                    else if (value.StartsWith('/'))
                    {
                        var entry = archive.GetEntry(value[1..]) ?? throw new InvalidDataException("安装器缺少数据文件。");
                        var destination = SafePath(Path.Combine(root, "installer", "data"), value[1..]);
                        await ExtractForgeEntry(entry, destination); value = destination;
                    }
                    variables[item.Name] = value;
                }
            string Expand(string value)
            {
                for (var depth = 0; depth < 16 && value.Contains('{'); depth++)
                {
                    var replaced = Regex.Replace(value, @"\{([^{}]+)\}", match => variables.TryGetValue(match.Groups[1].Value, out var replacement)
                        ? replacement : throw new InvalidDataException("无法解析加载器安装变量：" + match.Value));
                    if (replaced == value) break;
                    value = replaced;
                }
                if (value.Contains('{')) throw new InvalidDataException("加载器安装变量存在循环。");
                return value.StartsWith('[') && value.EndsWith(']') ? Library(value[1..^1]) : value;
            }
            var processors = install.Get("processors").Items().Where(p => p.Get("sides").ValueKind != JsonValueKind.Array || p.Get("sides").Items().Any(s => s.Text() == "client")).ToArray();
            if (processors.Length > 128) throw new InvalidDataException("加载器安装步骤过多。");
            for (var i = 0; i < processors.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                var processor = processors[i];
                progress?.Report(new($"处理加载器客户端文件 {i + 1}/{processors.Length}", CompletedFiles: i, TotalFiles: processors.Length));
                var outputs = processor.Get("outputs").ValueKind == JsonValueKind.Object
                    ? processor.Get("outputs").EnumerateObject().Select(x => (Path: Expand(x.Name), Hash: Expand(x.Value.Text() ?? ""))).ToArray() : [];
                foreach (var output in outputs)
                {
                    EnsureWithin(root, output.Path);
                    if (output.Hash.Length != 40 || !output.Hash.All(char.IsAsciiHexDigit)) throw new InvalidDataException("加载器输出校验无效。");
                }
                if (outputs.Length > 0 && (await Task.WhenAll(outputs.Select(x => File.Exists(x.Path) ? MatchesHash(new("", x.Path, x.Hash), ct) : Task.FromResult(false)))).All(x => x)) continue;
                var arguments = processor.Get("args").Items().Select(x => Expand(x.Text() ?? throw new InvalidDataException("安装参数必须为文本。"))).ToArray();
                // Fetch mappings through Wonder so source selection applies to this install phase as well.
                var task = Array.IndexOf(arguments, "--task");
                if (task >= 0 && task + 1 < arguments.Length && arguments[task + 1] == "DOWNLOAD_MOJMAPS")
                {
                    var outputIndex = Array.IndexOf(arguments, "--output");
                    if (outputIndex < 0 || outputIndex + 1 == arguments.Length) throw new InvalidDataException("映射下载缺少输出路径。");
                    var outputPath = arguments[outputIndex + 1]; EnsureWithin(root, outputPath);
                    await engine.DownloadAsync(Descriptor(minecraft.Get("downloads").Get("client_mappings"), outputPath), options, progress, ct);
                }
                else
                {
                    var jar = Library(processor.Get("jar").Text() ?? "");
                    using var jarArchive = ZipFile.OpenRead(jar);
                    var manifest = jarArchive.GetEntry("META-INF/MANIFEST.MF") ?? throw new InvalidDataException("安装处理器缺少清单。");
                    using var reader = new StreamReader(new MemoryStream(await ArchiveEntryContent.ReadAsync(manifest, 1024 * 1024, ct)));
                    var text = (await reader.ReadToEndAsync(ct)).Replace("\r\n", "\n").Replace("\n ", "");
                    var main = text.Split('\n').FirstOrDefault(x => x.StartsWith("Main-Class: ", StringComparison.OrdinalIgnoreCase))?[12..].Trim();
                    if (string.IsNullOrWhiteSpace(main) || main.Any(char.IsWhiteSpace) || main.StartsWith('-')) throw new InvalidDataException("安装处理器主类无效。");
                    var classPath = processor.Get("classpath").Items().Select(x => Library(x.Text() ?? "")).Prepend(jar).ToArray();
                    if (classPath.Any(x => !File.Exists(x))) throw new FileNotFoundException("安装处理器缺少依赖库。");
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromMinutes(10));
                    using var process = new Process { StartInfo = JavaRuntime.StartInfo(java, root, new[] { "-cp", string.Join(Path.PathSeparator, classPath), main }.Concat(arguments)) };
                    process.Start(); var stdout = Drain(process.StandardOutput, timeout.Token); var stderr = Drain(process.StandardError, timeout.Token);
                    try { await process.WaitForExitAsync(timeout.Token); await Task.WhenAll(stdout, stderr); }
                    catch (OperationCanceledException)
                    {
                        if (!process.HasExited) process.Kill(true);
                        try { await Task.WhenAll(stdout, stderr); } catch (OperationCanceledException) { }
                        if (!ct.IsCancellationRequested) throw new IOException("加载器安装处理超时，可重试。");
                        throw;
                    }
                    if (process.ExitCode != 0) throw new InvalidDataException("加载器客户端处理失败：" + (await stderr) + "\n" + (await stdout));
                }
                foreach (var output in outputs)
                    if (!File.Exists(output.Path) || !await MatchesHash(new("", output.Path, output.Hash), ct)) throw new InvalidDataException("加载器处理结果校验失败：" + Path.GetFileName(output.Path));
            }
        }
        await InstallLibraries(version, root, options, progress, ct, embeddedLibrary: legacyLibrary);
        await WriteJson(Path.Combine(root, "versions", id, id + ".json"), version.GetRawText(), ct);
        return id;
    }

    private static async Task<JsonElement> ReadArchiveJson(ZipArchive archive, string name, CancellationToken ct)
    {
        var entry = archive.GetEntry(name) ?? throw new InvalidDataException("加载器安装器缺少 " + name);
        using var stream = new MemoryStream(await ArchiveEntryContent.ReadAsync(entry, 16 * 1024 * 1024, ct));
        using var doc = JsonDocument.Parse(stream, new() { MaxDepth = 64 });
        return doc.RootElement.Clone();
    }
    private static Task ExtractInstallerEntry(ZipArchiveEntry entry, string destination, CancellationToken ct)
    {
        if (entry.Length > 512L * 1024 * 1024 || ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000) throw new InvalidDataException("加载器内嵌文件无效。");
        return ArchiveEntryContent.ExtractAsync(entry, destination, ct);
    }
    private static void EnsureWithin(string root, string path)
    {
        if (!Path.GetFullPath(path).StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, WonderPath.Comparison))
            throw new InvalidDataException("加载器处理路径越界。");
    }
}
