// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.IO.Compression;
using System.Text.Json;
using Wonder.Core;

namespace Wonder.Infrastructure;

public sealed partial class InstanceFileOperations
{
    private const string DisabledSuffix = ".disabled";
    private const int MetadataLimit = 1024 * 1024;
    private const int IconLimit = 2 * 1024 * 1024;
    private const int ScreenshotLimit = 32 * 1024 * 1024;

    public IReadOnlyList<InstanceContent> ReadContent(string gameDirectory, InstanceContentKind kind, CancellationToken ct)
    {
        var (active, inactive) = ContentDirectories(gameDirectory, kind);
        var entries = new List<InstanceContent>();
        Scan(active, false);
        if (kind is InstanceContentKind.ResourcePack or InstanceContentKind.ShaderPack or InstanceContentKind.World)
            Scan(inactive, true);
        return entries.OrderBy(entry => entry.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(entry => entry.FileName, WonderPath.Comparer).ThenByDescending(entry => entry.Enabled).ToArray();

        void Scan(string path, bool stashed)
        {
            EnsurePathWithoutLinks(path);
            if (!Directory.Exists(path)) return;
            foreach (var child in new DirectoryInfo(path).EnumerateFileSystemInfos())
            {
                ct.ThrowIfCancellationRequested();
                if (!child.Exists || (child.Attributes & FileAttributes.ReparsePoint) != 0 ||
                    !TryDescribe(child, kind, stashed, out var name, out var enabled)) continue;
                if (ReadEntry(child, kind, name, enabled, ct) is { } entry) entries.Add(entry);
            }
        }
    }

    public byte[]? ReadContentIcon(string gameDirectory, InstanceContent item, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (item.IconPath is not { } icon) return null;
        var child = CurrentEntry(gameDirectory, item);
        if (item.Kind == InstanceContentKind.Screenshot)
            return ReadFile(child.FullName, ScreenshotLimit, ct);
        if (child is DirectoryInfo)
            return DirectoryMember(child.FullName, icon) is { } path ? ReadFile(path, IconLimit, ct) : null;
        using var archive = ZipFile.OpenRead(child.FullName);
        return ReadMember(archive, icon, IconLimit, ct);
    }

    public void SetContentEnabled(string gameDirectory, InstanceContent item, bool enabled)
    {
        if (item.Kind == InstanceContentKind.Screenshot) throw new ArgumentException("截图没有启用状态。");
        ValidatePath(item.Path);
        var child = CurrentEntry(gameDirectory, item);
        if (item.Enabled == enabled) return;
        var (active, inactive) = ContentDirectories(gameDirectory, item.Kind);
        var destination = child is DirectoryInfo
            ? Path.Combine(enabled ? active : inactive, item.FileName)
            : Path.Combine(active, enabled ? item.FileName : item.FileName + DisabledSuffix);
        ValidatePath(destination);
        if (Path.Exists(destination)) throw new IOException("目标位置已有同名资源，未覆盖：" + destination);
        if (child is DirectoryInfo)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            Directory.Move(child.FullName, destination);
        }
        else File.Move(child.FullName, destination);
    }

    public Task RecycleContentAsync(string gameDirectory, InstanceContent item) => RecyclePathAsync(item.Path, () =>
    {
        ValidatePath(item.Path);
        var child = CurrentEntry(gameDirectory, item);
        if (child is DirectoryInfo) EnsureNoLinks(child.FullName);
    });

    private static FileSystemInfo CurrentEntry(string gameDirectory, InstanceContent item)
    {
        var (active, inactive) = ContentDirectories(gameDirectory, item.Kind);
        if (!Path.IsPathFullyQualified(item.Path)) throw new ArgumentException("资源路径无效。");
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(item.Path));
        var parent = Path.GetDirectoryName(path);
        var stashed = WonderPath.Comparer.Equals(parent, inactive);
        if (!WonderPath.Comparer.Equals(parent, active) && !stashed)
            throw new IOException("资源已不在实例的游戏目录中，请刷新列表。");
        EnsurePathWithoutLinks(path);
        FileSystemInfo child = item.IsDirectory ? new DirectoryInfo(path) : new FileInfo(path);
        if (!child.Exists || !TryDescribe(child, item.Kind, stashed, out var name, out var enabled) ||
            !WonderPath.Comparer.Equals(name, item.FileName) || enabled != item.Enabled ||
            child.CreationTimeUtc != item.CreatedAt.UtcDateTime || child.LastWriteTimeUtc != item.ModifiedAt.UtcDateTime ||
            child is FileInfo file && file.Length != item.Size)
            throw new IOException("资源在读取后发生变化，请刷新列表再操作。");
        return child;
    }

    private static (string Active, string Inactive) ContentDirectories(string gameDirectory, InstanceContentKind kind)
    {
        if (!Path.IsPathFullyQualified(gameDirectory)) throw new ArgumentException("游戏目录必须为完整路径。");
        var folder = kind switch
        {
            InstanceContentKind.Mod => "mods",
            InstanceContentKind.ResourcePack => "resourcepacks",
            InstanceContentKind.ShaderPack => "shaderpacks",
            InstanceContentKind.World => "saves",
            InstanceContentKind.Screenshot => "screenshots",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var root = Path.GetFullPath(gameDirectory);
        return (Path.Combine(root, folder), Path.Combine(root, ".wonder-disabled", folder));
    }

    private static bool TryDescribe(FileSystemInfo child, InstanceContentKind kind, bool stashed, out string name, out bool enabled)
    {
        name = child.Name;
        enabled = !stashed;
        if (child is DirectoryInfo)
        {
            return kind switch
            {
                InstanceContentKind.ResourcePack => DirectoryMember(child.FullName, "pack.mcmeta") is not null,
                InstanceContentKind.ShaderPack => HasRealDirectory(Path.Combine(child.FullName, "shaders")),
                InstanceContentKind.World => DirectoryMember(child.FullName, "level.dat") is not null || DirectoryMember(child.FullName, "level.dat_old") is not null,
                _ => false
            };
        }
        if (stashed) return false;
        if (kind != InstanceContentKind.Screenshot && name.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^DisabledSuffix.Length];
            enabled = false;
        }
        var extension = Path.GetExtension(name).ToLowerInvariant();
        return kind switch
        {
            InstanceContentKind.Mod => extension is ".jar" or ".zip" or ".litemod",
            InstanceContentKind.ResourcePack or InstanceContentKind.ShaderPack => extension == ".zip",
            InstanceContentKind.Screenshot => extension is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".gif",
            _ => false
        };
    }

    private static InstanceContent? ReadEntry(FileSystemInfo child, InstanceContentKind kind, string name, bool enabled, CancellationToken ct)
    {
        var metadata = new InstanceContentMetadata();
        try
        {
            if (kind == InstanceContentKind.Screenshot) metadata = new(IconPath: name);
            else if (child is DirectoryInfo)
            {
                int Limit(string entry) => entry is "level.dat" or "level.dat_old" ? 8 * MetadataLimit : MetadataLimit;
                metadata = InstanceContentMetadataReader.Read(kind,
                    entry => DirectoryMember(child.FullName, entry) is { } path ? AsStream(ReadFile(path, Limit(entry), ct)) : null,
                    entry => DirectoryMember(child.FullName, entry) is not null);
                if (metadata.IconPath is { } icon && DirectoryMember(child.FullName, icon) is null) metadata = metadata with { IconPath = null };
            }
            else
            {
                using var archive = ZipFile.OpenRead(child.FullName);
                metadata = InstanceContentMetadataReader.Read(kind, entry => AsStream(ReadMember(archive, entry, MetadataLimit, ct)),
                    entry => FindMember(archive, entry) is { Length: > 0 and <= IconLimit });
                if (metadata.IconPath is { } icon && FindMember(archive, icon) is not { Length: > 0 and <= IconLimit })
                    metadata = metadata with { IconPath = null };
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or NotSupportedException)
        {
            metadata = new(Details: [new("读取详情", "无法读取包内元数据")]);
        }
        var title = metadata.Title ?? (child is DirectoryInfo ? name : Path.GetFileNameWithoutExtension(name));
        // Directory enumeration can return cached timestamps on Windows.
        child.Refresh();
        if (!child.Exists || (child.Attributes & FileAttributes.ReparsePoint) != 0) return null;
        return new(child.FullName, name, kind, title, metadata.Description, enabled, child is DirectoryInfo,
            child is FileInfo file ? file.Length : null, child.LastWriteTimeUtc, metadata.Details ?? [], metadata.IconPath)
        { CreatedAt = child.CreationTimeUtc };
    }

    private static bool HasRealDirectory(string path) => Directory.Exists(path) &&
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;

    private static string? DirectoryMember(string root, string relative)
    {
        if (!IsMemberPath(relative)) return null;
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path)) return null;
        for (var part = path; !WonderPath.Comparer.Equals(part, root); part = Path.GetDirectoryName(part)!)
            if ((File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0) return null;
        return path;
    }

    private static ZipArchiveEntry? FindMember(ZipArchive archive, string name) => IsMemberPath(name) ? archive.GetEntry(name) : null;

    private static bool IsMemberPath(string relative) => !Path.IsPathRooted(relative) &&
        !relative.Contains('\\') && !relative.Contains(':') && !relative.Contains('\0') &&
        relative.Split('/').All(segment => segment.Length != 0 && segment is not ("." or "..") &&
            segment[^1] is not ('.' or ' '));

    private static Stream? AsStream(byte[]? bytes) => bytes is not null ? new MemoryStream(bytes, writable: false) : null;

    private static byte[]? ReadFile(string path, int limit, CancellationToken ct)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return ReadBytes(file, file.Length, limit, ct);
    }

    private static byte[]? ReadMember(ZipArchive archive, string name, int limit, CancellationToken ct)
    {
        if (FindMember(archive, name) is not { Length: > 0 } entry || entry.Length > limit) return null;
        using var stream = entry.Open();
        return ReadBytes(stream, entry.Length, limit, ct);
    }

    private static byte[]? ReadBytes(Stream stream, long size, int limit, CancellationToken ct)
    {
        if (size <= 0 || size > limit) return null;
        var bytes = new byte[(int)size];
        for (var offset = 0; offset < bytes.Length;)
        {
            ct.ThrowIfCancellationRequested();
            var count = stream.Read(bytes, offset, Math.Min(64 * 1024, bytes.Length - offset));
            if (count == 0) throw new InvalidDataException("资源读取过程中发生变化。");
            offset += count;
        }
        if (stream.ReadByte() != -1) throw new InvalidDataException("资源内容超出声明大小。");
        return bytes;
    }

    private static void EnsurePathWithoutLinks(string path)
    {
        for (var part = Path.GetFullPath(path); part is not null; part = Path.GetDirectoryName(part))
        {
            try
            {
                if ((File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("资源路径含有链接：" + part);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
}
