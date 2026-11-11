// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Text.Json;
using Wonder.Application;
using Wonder.Core;

namespace Wonder.Infrastructure;

public sealed class InstanceStore : IInstanceStore
{
    private readonly string path;
    private readonly FileOperations files;
    private const int MaximumBytes = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        IgnoreReadOnlyProperties = true, AllowDuplicateProperties = false, MaxDepth = 32
    };

    public InstanceStore(string path) : this(path, new FileOperations()) { }

    internal InstanceStore(string path, FileOperations files)
    {
        this.path = path;
        this.files = files;
    }

    public InstanceCatalog Load()
    {
        if (!File.Exists(path)) return new();
        try
        {
            if (new FileInfo(path).Length > MaximumBytes) throw new InvalidDataException("实例索引超过大小限制。");
            var catalog = JsonSerializer.Deserialize<InstanceCatalog>(File.ReadAllBytes(path), Options) ?? throw new InvalidDataException("实例索引为空。");
            catalog.Validate();
            for (var index = 0; index < catalog.Instances.Count; index++)
            {
                var instance = catalog.Instances[index];
                if (instance.Source == InstanceSource.Wonder && Directory.Exists(instance.RootDirectory))
                {
                    var native = WonderInstanceConfigurationStore.Read(instance.RootDirectory);
                    if (native.Id != instance.Id) throw new InvalidDataException("Wonder 实例身份与索引不一致，原文件已保留。");
                    catalog.Instances[index] = native with { PreviousRootDirectories = instance.PreviousRootDirectories };
                    continue;
                }
                if (string.IsNullOrWhiteSpace(instance.GameVersion) || instance.ModLoaders.Count == 0)
                    catalog.Instances[index] = instance with
                    {
                        GameVersion = string.IsNullOrWhiteSpace(instance.GameVersion) ? InstanceVersionMetadata.Read(instance) : instance.GameVersion,
                        ModLoaders = instance.ModLoaders.Count == 0 ? InstanceVersionMetadata.ReadModLoaders(instance) : instance.ModLoaders
                    };
            }
            return catalog;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException) { throw new InvalidDataException("实例索引格式无效。", ex); }
    }
    public void Save(InstanceCatalog catalog)
    {
        catalog.Validate();
        var updates = new List<(string Target, byte[] Bytes, string Temporary, string Backup)>();
        var committed = new List<(string Target, string Backup)>();
        var successful = false;
        try
        {
            foreach (var instance in catalog.Instances.Where(x => x.Source == InstanceSource.Wonder))
            {
                var target = Path.Combine(instance.RootDirectory, "wonder.instance.json");
                // A disconnected, unchanged instance must not prevent selecting another game.
                if (!Directory.Exists(instance.RootDirectory))
                {
                    var previous = Load().Instances.FirstOrDefault(x => x.Id == instance.Id);
                    if (previous is not null && previous.Name == instance.Name && previous.Overrides == instance.Overrides) continue;
                    throw new DirectoryNotFoundException("Wonder 实例目录不可用，参数未保存：" + instance.RootDirectory);
                }
                var bytes = WonderInstanceConfigurationStore.Serialize(instance);
                if (File.ReadAllBytes(target).AsSpan().SequenceEqual(bytes)) continue;
                if (updates.Any(x => WonderPath.Comparer.Equals(x.Target, target))) throw new InvalidDataException("同一 Wonder 配置对应多个实例登记。");
                var token = Guid.NewGuid().ToString("N");
                updates.Add((target, bytes, target + "." + token + ".tmp", target + "." + token + ".bak"));
            }
            foreach (var update in updates)
            {
                using var stream = new FileStream(update.Temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                stream.Write(update.Bytes); stream.Flush(true);
            }
            foreach (var update in updates)
            {
                // Retain a same-directory backup until the catalog has committed.
                File.Replace(update.Temporary, update.Target, update.Backup);
                committed.Add((update.Target, update.Backup));
            }
            SaveIndex(catalog);
            successful = true;
        }
        catch (Exception error)
        {
            foreach (var update in committed.AsEnumerable().Reverse())
            {
                try { File.Move(update.Backup, update.Target, true); }
                catch (Exception restoreError)
                {
                    error.Data["Wonder.InstanceConfig.RecoveryPath"] = update.Backup;
                    error.Data["Wonder.InstanceConfig.RecoveryError"] = restoreError;
                }
            }
            throw;
        }
        finally
        {
            foreach (var update in updates)
            {
                try { if (File.Exists(update.Temporary)) File.Delete(update.Temporary); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                if (successful) try { if (File.Exists(update.Backup)) File.Delete(update.Backup); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    private void SaveIndex(InstanceCatalog catalog)
    {
        catalog.Validate();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(catalog, Options);
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("实例索引超过大小限制。");
        var target = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var ownsTemporary = false;
        Exception? saveError = null;
        try
        {
            var stream = files.CreateTemporary(temporary);
            ownsTemporary = true;
            try
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            catch (Exception error) { saveError = error; throw; }
            finally
            {
                // Disposal can flush buffered data again after a failed write or flush.
                try { stream.Dispose(); }
                catch (Exception disposeError) when (saveError is not null)
                {
                    saveError.Data["Wonder.Storage.DisposeException"] = disposeError;
                }
            }
            files.Commit(temporary, target);
        }
        catch (Exception error) { saveError = error; throw; }
        finally
        {
            if (ownsTemporary && File.Exists(temporary))
            {
                try { files.DeleteTemporary(temporary); }
                catch (Exception cleanupError) when (saveError is not null)
                {
                    saveError.Data["Wonder.Storage.CleanupException"] = cleanupError;
                    saveError.Data["Wonder.Storage.TemporaryPath"] = temporary;
                }
            }
        }
    }

    internal class FileOperations
    {
        public virtual FileStream CreateTemporary(string path) => new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        public virtual void Commit(string temporaryPath, string targetPath)
        {
            if (File.Exists(targetPath))
                File.Replace(temporaryPath, targetPath, destinationBackupFileName: null);
            else
                File.Move(temporaryPath, targetPath);
        }
        public virtual void DeleteTemporary(string path) => File.Delete(path);
    }
}
