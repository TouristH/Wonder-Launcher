// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Wonder.Extensibility;

public sealed partial class PluginManager
{
    public PluginConsentRequest? GetConsentRequest(string id)
    {
        lock (_gate)
        {
            var local = ReadCurrentPlugin(RequireInstalled(id));
            CheckWritable();
            var fingerprint = Fingerprint(local.Directory);
            if (HasConsent(id, fingerprint)) return null;
            var analysis = PluginAnalyzer.Analyze(local.Directory);
            if (fingerprint != Fingerprint(local.Directory))
                throw new InvalidOperationException("插件文件在分析期间发生变化，请重新启用并查看提示。");
            _installed[id] = local;
            return new(local.Manifest, fingerprint, analysis);
        }
    }

    public void AcceptAndEnable(string id, string fingerprint) => Change(() =>
    {
        var local = ReadCurrentPlugin(RequireInstalled(id));
        CheckWritable();
        if (!string.Equals(Fingerprint(local.Directory), fingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException("插件文件已改变，请重新查看启用提示后再同意。");
        var consents = new Dictionary<string, string>(_consents, StringComparer.Ordinal) { [id] = fingerprint };
        WriteAtomic(_settingsPath, JsonSerializer.Serialize(new PluginSettings(_enabled.Order(StringComparer.Ordinal).ToArray(), consents), Json));
        _consents = consents;
        _installed[id] = local;
        Stop(id);
        if (!Start(local)) throw new InvalidOperationException(_failures[id]);
        try { StoreEnabled(id, true); }
        catch
        {
            Stop(id);
            throw;
        }
    });

    private bool HasConsent(string id, string fingerprint) =>
        string.Equals(_consents.GetValueOrDefault(id), fingerprint, StringComparison.Ordinal);

    private void RequireConsent(LocalPlugin local)
    {
        if (!HasConsent(local.Manifest.Id, Fingerprint(local.Directory)))
            throw new InvalidOperationException("请先阅读并同意此插件的启用提示。");
    }

    private static LocalPlugin ReadCurrentPlugin(LocalPlugin local)
    {
        var manifest = ReadManifest(local.Directory);
        if (manifest.Id != local.Manifest.Id)
            throw new InvalidDataException("插件 ID 已改变，请刷新本地插件。");
        return new(manifest, local.Directory);
    }

    // This detects changed installations for renewed consent. It is not a sandbox.
    private static string Fingerprint(string directory)
    {
        var files = new List<string>();
        var pending = new Stack<string>();
        pending.Push(directory);
        var count = 0;
        while (pending.TryPop(out var current))
        {
            CheckLink(current);
            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                if (++count > EntryLimit) throw new InvalidDataException("插件目录的文件数量超出限制。");
                CheckLink(entry);
                if (Directory.Exists(entry)) pending.Push(entry);
                else files.Add(entry);
            }
        }
        using var fingerprint = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long remaining = ExtractedLimit;
        foreach (var file in files.OrderBy(path => Path.GetRelativePath(directory, path), StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(directory, file).Replace(Path.DirectorySeparatorChar, '/');
            _ = SplitRelativePath(relative);
            using var stream = File.OpenRead(file);
            remaining -= stream.Length;
            if (remaining < 0) throw new InvalidDataException("插件文件总大小超过 128 MiB。");
            fingerprint.AppendData(Encoding.UTF8.GetBytes(relative));
            fingerprint.AppendData([0]);
            fingerprint.AppendData(SHA256.HashData(stream));
        }
        return Convert.ToHexString(fingerprint.GetHashAndReset());
    }
}
