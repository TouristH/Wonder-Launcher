// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Wonder.Extensibility;

public sealed partial class PluginManager
{
    private const long ZipLimit = 64L * 1024 * 1024;
    private const long ExtractedLimit = 128L * 1024 * 1024;
    private const int EntryLimit = 2048;
    private const int ApiVersion = 2;

    public async Task<IReadOnlyList<PluginCatalogEntry>> GetCatalogAsync(string url, CancellationToken cancellationToken = default)
    {
        using var response = await GetAsync(RequireHttps(url), JsonLimit, cancellationToken).ConfigureAwait(false);
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await ReadCatalogAsync(body, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<IReadOnlyList<PluginCatalogEntry>> ReadCatalogAsync(Stream body, CancellationToken cancellationToken)
    {
        using var json = new MemoryStream();
        await CopyBoundedAsync(body, json, JsonLimit, cancellationToken).ConfigureAwait(false);
        json.Position = 0;
        PluginCatalog? catalog;
        try { catalog = await JsonSerializer.DeserializeAsync<PluginCatalog>(json, Json, cancellationToken).ConfigureAwait(false); }
        catch (JsonException error) { throw new InvalidDataException("插件目录 JSON 无效。", error); }
        if (catalog is not { SchemaVersion: 1, Product: "wonder", Plugins: not null })
            throw new InvalidDataException("插件目录格式不受支持。");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in catalog.Plugins)
        {
            ValidateCatalogEntry(entry);
            if (!ids.Add(entry.Id + "@" + entry.Version))
                throw new InvalidDataException("插件目录包含重复版本：" + entry.Id + "@" + entry.Version);
        }
        return Array.AsReadOnly(catalog.Plugins);
    }

    public Task InstallAsync(PluginCatalogEntry entry, CancellationToken cancellationToken = default)
    {
        ValidateCatalogEntry(entry);
        return StageAndInstallAsync(async stage =>
        {
            var path = Path.Combine(stage, "download.zip");
            using var response = await GetAsync(RequireHttps(entry.DownloadUrl), entry.Size, cancellationToken).ConfigureAwait(false);
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                if (await CopyBoundedAsync(source, destination, entry.Size, cancellationToken).ConfigureAwait(false) != entry.Size)
                    throw new InvalidDataException("插件包大小与目录不匹配。");

            await using var verified = File.OpenRead(path);
            var hash = await SHA256.HashDataAsync(verified, cancellationToken).ConfigureAwait(false);
            if (!hash.AsSpan().SequenceEqual(Convert.FromHexString(entry.Sha256)))
                throw new InvalidDataException("插件包 SHA-256 与目录不匹配。");
            return path;
        }, entry, cancellationToken);
    }

    public Task InstallLocalAsync(string zipPath, CancellationToken cancellationToken = default) =>
        StageAndInstallAsync(_ => Task.FromResult(Path.GetFullPath(zipPath)), null, cancellationToken);

    private async Task StageAndInstallAsync(Func<string, Task<string>> getPackage, PluginCatalogEntry? catalog,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_loaded) RefreshCore();
            CheckWritable();
        }
        var stagingRoot = Path.Combine(_dataDirectory, ".plugin-staging");
        CheckLink(stagingRoot);
        var stage = Path.Combine(stagingRoot, Guid.NewGuid().ToString("N"));
        var package = Path.Combine(stage, "package");
        var backup = Path.Combine(stage, "previous");
        Directory.CreateDirectory(package);
        var keepBackup = false;
        try
        {
            var zipPath = await getPackage(stage);
            await ExtractPackageAsync(zipPath, package, cancellationToken);
            var manifest = ReadManifest(package);
            if (catalog is not null && (manifest.Id != catalog.Id || manifest.Name != catalog.Name ||
                manifest.Author != catalog.Author || manifest.Description != catalog.Description ||
                manifest.ApiVersion != catalog.ApiVersion || manifest.Version != catalog.Version ||
                !PermissionsEqual(manifest.Permissions, catalog.Permissions) ||
                !PluginConfiguration.Equivalent(manifest.ConfigurationSchema, catalog.ConfigurationSchema)))
                throw new InvalidDataException("插件清单与目录条目不匹配。");
            cancellationToken.ThrowIfCancellationRequested();
            Change(() => InstallPackage(manifest, package, backup, ref keepBackup));
        }
        finally
        {
            if (!keepBackup)
                try { Directory.Delete(stage, recursive: true); }
                catch (Exception error) when (IsFileError(error)) { }
        }
    }

    private void InstallPackage(PluginManifest manifest, string package, string backup, ref bool keepBackup)
    {
        CheckWritable();
        CheckLink(RootDirectory);
        Directory.CreateDirectory(RootDirectory);
        var id = manifest.Id;
        var old = _installed.GetValueOrDefault(id);
        var target = old?.Directory ?? Path.Combine(RootDirectory, id);
        CheckLink(target);
        var wasEnabled = _enabled.Contains(id);
        StoreEnabled(id, false);
        Stop(id);
        try
        {
            if (Directory.Exists(target)) Directory.Move(target, backup);
            try { Directory.Move(package, target); }
            catch
            {
                if (Directory.Exists(backup))
                {
                    keepBackup = true;
                    Directory.Move(backup, target);
                    keepBackup = false;
                }
                throw;
            }
        }
        catch (Exception error) when (IsFileError(error))
        {
            if (keepBackup)
                throw new IOException($"插件替换和回滚失败。旧包保存在 {backup}。", error);
            if (wasEnabled) StoreEnabled(id, true);
            if (wasEnabled && old is not null) Start(old);
            throw;
        }
        RefreshCore();
    }

    private async Task<HttpResponseMessage> GetAsync(Uri uri, long limit, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        try
        {
            response.EnsureSuccessStatusCode();
            if (response.RequestMessage?.RequestUri is { } finalUri) RequireHttps(finalUri.AbsoluteUri);
            if (response.Content.Headers.ContentLength > limit)
                throw new InvalidDataException("插件下载超出允许的大小。");
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private static async Task ExtractPackageAsync(string path, string destination, CancellationToken cancellationToken)
    {
        using var input = File.OpenRead(path);
        if (input.Length > ZipLimit) throw new InvalidDataException("插件 ZIP 超过 64 MiB。");
        using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        if (zip.Entries.Count > EntryLimit) throw new InvalidDataException("插件 ZIP 的文件数量超出限制。");
        var paths = new Dictionary<string, ArchivePath>(StringComparer.OrdinalIgnoreCase);
        long available = ExtractedLimit;
        foreach (var entry in zip.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = entry.FullName.EndsWith('/');
            var relative = directory ? entry.FullName[..^1] : entry.FullName;
            var segments = SplitRelativePath(relative);
            var unixType = (entry.ExternalAttributes >> 16) & 0xf000;
            if ((entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0 ||
                unixType != 0 && unixType != (directory ? 0x4000 : 0x8000))
                throw new InvalidDataException("插件 ZIP 包含链接或特殊文件。");
            CheckArchiveCollision(paths, segments, directory);
            var target = Path.Combine(destination, Path.Combine(segments));
            if (directory)
            {
                if (entry.Length != 0) throw new InvalidDataException("插件 ZIP 的目录条目无效。");
                Directory.CreateDirectory(target);
                continue;
            }
            if (entry.Length > available) throw new InvalidDataException("插件 ZIP 解压后超过 128 MiB。");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var source = entry.Open();
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            var bytes = await CopyBoundedAsync(source, output, entry.Length, cancellationToken).ConfigureAwait(false);
            if (bytes != entry.Length) throw new InvalidDataException("插件 ZIP 条目大小与声明不符。");
            available -= bytes;
        }
    }

    private static void CheckArchiveCollision(Dictionary<string, ArchivePath> paths, string[] segments, bool directory)
    {
        var name = "";
        for (var index = 0; index < segments.Length; index++)
        {
            name = index == 0 ? segments[index] : name + "/" + segments[index];
            var explicitEntry = index == segments.Length - 1;
            var entry = new ArchivePath(name, directory || !explicitEntry, explicitEntry);
            if (paths.TryGetValue(name, out var previous))
            {
                if (previous.Name != entry.Name || previous.Directory != entry.Directory || previous.Explicit && entry.Explicit)
                    throw new InvalidDataException("插件 ZIP 中路径重复或冲突：" + name);
                if (!explicitEntry) continue;
            }
            paths[name] = entry;
        }
    }

    private static async Task<long> CopyBoundedAsync(Stream source, Stream target, long maximum, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        int count;
        while ((count = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
        {
            total += count;
            if (total > maximum) throw new InvalidDataException("插件文件超出允许的大小。");
            await target.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
        }
        return total;
    }

    private static PluginManifest ReadManifest(string directory)
    {
        CheckLink(directory);
        var manifest = ReadJson<PluginManifest>(Path.Combine(directory, ManifestFile));
        ValidateMetadata(manifest);
        if (!HasText(manifest.EntryType, 512))
            throw new InvalidDataException("插件入口类型无效。");
        _ = EntryPath(directory, manifest);
        return manifest;
    }

    private static string EntryPath(string directory, PluginManifest manifest)
    {
        var parts = SplitRelativePath(manifest.EntryAssembly);
        if (!parts[^1].EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("插件入口必须是 DLL。");
        var path = CheckedLocalPath(directory, Path.Combine(directory, Path.Combine(parts)));
        if (!File.Exists(path))
            throw new InvalidDataException("找不到插件入口程序集：" + manifest.EntryAssembly);
        return path;
    }

    private static string CheckedLocalPath(string directory, string path)
    {
        CheckLink(directory);
        var relative = Path.GetRelativePath(directory, path).Replace(Path.DirectorySeparatorChar, '/');
        var checkedPath = directory;
        foreach (var segment in SplitRelativePath(relative))
        {
            checkedPath = Path.Combine(checkedPath, segment);
            CheckLink(checkedPath);
        }
        return checkedPath;
    }

    private static void CheckLink(string path)
    {
        FileAttributes attributes;
        try { attributes = File.GetAttributes(path); }
        catch (FileNotFoundException) { return; }
        catch (DirectoryNotFoundException) { return; }
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("插件路径不能是链接：" + Path.GetFileName(path));
    }

    private static string[] SplitRelativePath(string value)
    {
        if (!HasText(value, 512))
            throw new InvalidDataException("插件文件路径无效。");
        var segments = value.Split('/');
        if (!segments.All(IsPathSegment))
            throw new InvalidDataException("插件文件路径无效：" + value);
        return segments;
    }

    private static bool IsPathSegment(string segment)
    {
        if (segment.Length is 0 or > 255 || segment.EndsWith('.') || segment.EndsWith(' ') ||
            segment.Any(character => char.IsControl(character) || "<>:\"/\\|?*".Contains(character)))
            return false;
        var stem = segment.Split('.', 2)[0].TrimEnd(' ').ToUpperInvariant();
        return stem is not ("CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$") &&
            !(stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) &&
                "123456789¹²³".Contains(stem[3]));
    }

    private static bool IsId(string? value) => value is { Length: > 0 and <= 80 } &&
        char.IsAsciiLetterOrDigit(value[0]) &&
        value.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character is '.' or '_' or '-') &&
        IsPathSegment(value);

    private static bool HasText(string? value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximum;

    private static void ValidateMetadata(PluginManifest manifest)
    {
        if (!IsId(manifest.Id) || !HasText(manifest.Name, 256) || !HasText(manifest.Description, 4096) ||
            !HasText(manifest.Author, 256) || !HasText(manifest.Version, 32) || !IsVersion(manifest.Version))
            throw new InvalidDataException("插件元数据无效。");
        if (manifest.ApiVersion is < 1 or > ApiVersion)
            throw new InvalidDataException("插件 API 版本不受支持：" + manifest.ApiVersion);
        if (manifest.Permissions is { } permissions && (permissions.Length > 64 || permissions.Any(permission =>
            permission is null || !HasText(permission.Name, 128) || !HasText(permission.Description, 2048))))
            throw new InvalidDataException("插件作者提供的访问说明无效。");
        if (manifest.ConfigurationSchema is { } schema) PluginConfiguration.ValidateSchema(schema);
    }

    private static bool PermissionsEqual(PluginPermission[]? left, PluginPermission[]? right) =>
        left is null ? right is null : right is not null && left.SequenceEqual(right);

    private static bool IsVersion(string version)
    {
        var parts = version.Split('.');
        return parts.Length == 3 && parts.All(part => part.Length > 0 && (part.Length == 1 || part[0] != '0') &&
            part.All(char.IsAsciiDigit) && int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _));
    }

    private static void ValidateCatalogEntry(PluginCatalogEntry? entry)
    {
        if (entry is null) throw new InvalidDataException("插件目录条目为空。");
        ValidateMetadata(entry);
        if (entry.Size is <= 0 or > ZipLimit || entry.Sha256 is not { Length: 64 } || !entry.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("插件目录中的下载大小或 SHA-256 无效。");
        RequireHttps(entry.DownloadUrl);
        RequireHttps(entry.SourceUrl);
        if (entry.Review is { } review && (review.Status is not ("verified" or "revoked") ||
            string.IsNullOrWhiteSpace(review.Reviewer) || review.Reviewer.Length > 256 ||
            review.ReviewedAt == default ||
            !string.Equals(review.Sha256, entry.Sha256, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("插件目录中的人工验证记录与此安装包不匹配。");
    }

    private static Uri RequireHttps(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            uri.Host.Length == 0 || uri.UserInfo.Length != 0)
            throw new InvalidDataException("插件链接必须使用 HTTPS 且不能包含登录信息。");
        return uri;
    }

    private sealed record PluginCatalog(int SchemaVersion, string Product, PluginCatalogEntry[] Plugins);
    private readonly record struct ArchivePath(string Name, bool Directory, bool Explicit);
}
