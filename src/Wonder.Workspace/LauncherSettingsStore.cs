// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wonder.Workspace;

public sealed class LauncherSettingsStore
{
    private const int MaximumFileBytes = 64 * 1024;
    private readonly string path;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        MaxDepth = 8,
        NumberHandling = JsonNumberHandling.Strict,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public LauncherSettingsStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = Path.GetFullPath(path);
    }

    public LauncherSettings Load()
    {
        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length > MaximumFileBytes)
                throw new InvalidDataException("设置文件超过 64 KiB 限制，原文件已保留。");
            var settings = JsonSerializer.Deserialize<LauncherSettings>(stream, JsonOptions)
                ?? throw new InvalidDataException("设置文件内容为空，原文件已保留。");
            settings.Validate();
            return settings;
        }
        catch (FileNotFoundException) { return new LauncherSettings(); }
        catch (DirectoryNotFoundException) { return new LauncherSettings(); }
        catch (JsonException error) { throw new InvalidDataException("设置文件格式无效，原文件已保留。", error); }
    }

    public void Save(LauncherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, settings, JsonOptions);
                if (stream.Length > MaximumFileBytes)
                    throw new InvalidDataException("设置文件超过 64 KiB 限制。");
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
                File.Replace(temporaryPath, path, destinationBackupFileName: null);
            else
                File.Move(temporaryPath, path);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
