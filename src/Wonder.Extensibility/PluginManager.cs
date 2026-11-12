// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Wonder.Extensibility.Contracts;

namespace Wonder.Extensibility;

public sealed partial class PluginManager : IDisposable
{
    private const string ManifestFile = "plugin.json";
    private const int JsonLimit = 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        AllowDuplicateProperties = false,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true
    };
    private readonly object _gate = new();
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly string _dataDirectory;
    private readonly string _settingsPath;
    private readonly Dictionary<string, LocalPlugin> _installed = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RunningPlugin> _running = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _failures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ActivityLog> _logs = new(StringComparer.Ordinal);
    private HashSet<string> _enabled = new(StringComparer.Ordinal);
    private Dictionary<string, string> _consents = new(StringComparer.Ordinal);
    private bool _settingsInvalid;
    private bool _loaded;
    private bool _disposed;
    private string? _error;

    public PluginManager(string dataDirectory, HttpClient? httpClient = null)
    {
        _dataDirectory = Path.GetFullPath(dataDirectory);
        RootDirectory = Path.Combine(_dataDirectory, "plugins");
        _settingsPath = Path.Combine(_dataDirectory, "plugin-settings.json");
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;
    }

    public string RootDirectory { get; }
    public string? Error { get { lock (_gate) return _error; } }
    public event EventHandler? Changed;

    public IReadOnlyList<InstalledPlugin> Plugins
    {
        get
        {
            lock (_gate)
                return _installed.Values.OrderBy(item => item.Manifest.Id, StringComparer.Ordinal).Select(item =>
                {
                    var id = item.Manifest.Id;
                    var enabled = _running.ContainsKey(id);
                    var error = _failures.GetValueOrDefault(id);
                    return new InstalledPlugin(item.Manifest, item.Directory, enabled,
                        enabled ? "已启用" : error is null ? "已停用" : "加载失败", error,
                        _logs.GetValueOrDefault(id)?.Snapshot() ?? []);
                }).ToArray();
        }
    }

    public void Refresh() => Change(() => RefreshCore(restart: true));

    private void RefreshCore(bool restart = false)
    {
        _loaded = true;
        var problems = new List<string>();
        LoadSettings(problems);
        var found = new Dictionary<string, LocalPlugin>(StringComparer.Ordinal);
        var duplicates = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            CheckLink(RootDirectory);
            if (Directory.Exists(RootDirectory))
                foreach (var directory in Directory.EnumerateDirectories(RootDirectory))
                    Discover(directory, found, duplicates, problems);
        }
        catch (Exception error) when (IsFileError(error))
        {
            problems.Add("读取插件目录失败：" + error.Message);
        }

        _failures.Clear();
        foreach (var id in _running.Keys.ToArray())
            if (restart || _settingsInvalid || !_enabled.Contains(id) || !found.TryGetValue(id, out var replacement) ||
                !_installed.TryGetValue(id, out var existing) || !SameInstallation(existing, replacement))
                Stop(id);
        _installed.Clear();
        foreach (var item in found)
        {
            _installed.Add(item.Key, item.Value);
            if (!_settingsInvalid && _enabled.Contains(item.Key) && !_running.ContainsKey(item.Key))
                Start(item.Value);
        }
        _error = problems.Count == 0 ? null : string.Join(Environment.NewLine, problems);
    }

    private static void Discover(string directory, Dictionary<string, LocalPlugin> found,
        HashSet<string> duplicates, List<string> problems)
    {
        try
        {
            var manifest = ReadManifest(directory);
            if (!string.Equals(Path.GetFileName(directory), manifest.Id, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("插件目录名称必须与插件 ID 一致。");
            if (duplicates.Contains(manifest.Id) || !found.TryAdd(manifest.Id, new(manifest, directory)))
            {
                found.Remove(manifest.Id);
                duplicates.Add(manifest.Id);
                throw new InvalidDataException("插件 ID 重复：" + manifest.Id);
            }
        }
        catch (Exception error) when (IsFileError(error))
        {
            problems.Add($"插件 {Path.GetFileName(directory)}：{error.Message}");
        }
    }

    public void SetEnabled(string id, bool enabled) => Change(() =>
    {
        var local = RequireInstalled(id);
        CheckWritable();
        if (!enabled)
        {
            StoreEnabled(id, false);
            Stop(id);
            return;
        }
        RequireConsent(local);
        var wasRunning = _running.ContainsKey(id);
        if (!wasRunning && !Start(local))
            throw new InvalidOperationException(_failures[id]);
        try { StoreEnabled(id, true); }
        catch
        {
            if (!wasRunning) Stop(id);
            throw;
        }
    });

    public string ReadConfiguration(string id)
    {
        lock (_gate)
        {
            RequireInstalled(id);
            return ReadConfigurationCore(id);
        }
    }

    public void SaveConfiguration(string id, string json) => Change(() =>
    {
        var local = RequireInstalled(id);
        CheckWritable();
        ValidateConfiguration(json);
        var path = ConfigurationPath(id);
        var old = File.Exists(path) ? ReadText(path) : null;
        var restart = _running.ContainsKey(id);
        WriteAtomic(path, json);
        if (!restart)
            return;

        Stop(id);
        if (Start(local))
            return;
        var failure = _failures[id];
        if (old is null) File.Delete(path);
        else WriteAtomic(path, old);
        if (!Start(local))
            throw new InvalidOperationException(failure + " 恢复原插件配置后仍无法启动：" + _failures[id]);
        throw new InvalidOperationException(failure + " 已恢复原插件配置。");
    });

    private LocalPlugin RequireInstalled(string id)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_loaded) RefreshCore();
        return _installed.TryGetValue(id, out var plugin) ? plugin :
            throw new InvalidOperationException("插件未安装：" + id);
    }

    private void LoadSettings(List<string> problems)
    {
        try
        {
            CheckLink(_settingsPath);
            var settings = File.Exists(_settingsPath)
                ? ReadJson<PluginSettings>(_settingsPath)
                : new PluginSettings([]);
            var enabled = settings.Enabled;
            if (enabled is null || enabled.Any(id => !IsId(id)) ||
                enabled.Distinct(StringComparer.Ordinal).Count() != enabled.Length)
                throw new InvalidDataException("启用清单无效。");
            _enabled = new(enabled, StringComparer.Ordinal);
            if (settings.Consents is { } consents && consents.Any(pair =>
                !IsId(pair.Key) || pair.Value is not { Length: 64 } || !pair.Value.All(Uri.IsHexDigit)))
                throw new InvalidDataException("插件同意记录无效。");
            _consents = new(settings.Consents ?? [], StringComparer.Ordinal);
            _settingsInvalid = false;
        }
        catch (Exception error) when (IsFileError(error))
        {
            _enabled.Clear();
            _consents.Clear();
            _settingsInvalid = true;
            problems.Add("读取插件设置失败，已保留原文件：" + error.Message);
        }
    }

    private void CheckWritable()
    {
        if (_settingsInvalid)
            throw new InvalidOperationException("插件设置无法读取。请修复后刷新，再修改插件。");
    }

    private void StoreEnabled(string id, bool enabled)
    {
        var next = new HashSet<string>(_enabled, StringComparer.Ordinal);
        var changed = enabled ? next.Add(id) : next.Remove(id);
        if (!changed) return;
        WriteAtomic(_settingsPath, JsonSerializer.Serialize(new PluginSettings(next.Order(StringComparer.Ordinal).ToArray(), _consents), Json));
        _enabled = next;
    }

    private bool Start(LocalPlugin local)
    {
        var manifest = local.Manifest;
        var id = manifest.Id;
        PluginLoadContext? loader = null;
        IWonderPlugin? plugin = null;
        try
        {
            local = ReadCurrentPlugin(local);
            manifest = local.Manifest;
            RequireConsent(local);
            var configuration = ReadConfigurationCore(id);
            var data = PluginDataDirectory(id);
            Directory.CreateDirectory(data);
            var entryAssembly = EntryPath(local.Directory, manifest);
            loader = new PluginLoadContext(local.Directory, entryAssembly);
            var type = loader.LoadManaged(entryAssembly).GetType(manifest.EntryType, throwOnError: true)!;
            if (!type.IsClass || type.IsAbstract || !type.IsVisible || !typeof(IWonderPlugin).IsAssignableFrom(type))
                throw new InvalidDataException("插件入口必须是实现 IWonderPlugin 的公共类。");
            plugin = (IWonderPlugin?)Activator.CreateInstance(type) ??
                throw new InvalidDataException("无法创建插件入口。");
            if (!_logs.TryGetValue(id, out var log))
                _logs.Add(id, log = new ActivityLog());
            plugin.Activate(new PluginContext(configuration, data, log));
            _running.Add(id, new(plugin, loader));
            _failures.Remove(id);
            return true;
        }
        catch (Exception error)
        {
            Release(plugin, loader);
            _failures[id] = "启用插件失败：" + (error is TargetInvocationException { InnerException: { } inner }
                ? inner.Message : error.Message);
            return false;
        }
    }

    private void Stop(string id)
    {
        _failures.Remove(id);
        if (_running.Remove(id, out var running) && Release(running.Instance, running.Loader) is { } error)
            _failures[id] = "停用插件失败：" + error.Message;
    }

    private static Exception? Release(IWonderPlugin? plugin, PluginLoadContext? loader)
    {
        try { plugin?.Dispose(); }
        catch (Exception error) { return error; }
        finally { loader?.Unload(); }
        return null;
    }

    private void Change(Action action)
    {
        try
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                action();
            }
        }
        finally { Changed?.Invoke(this, EventArgs.Empty); }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var id in _running.Keys.ToArray()) Stop(id);
            if (_ownsHttpClient) _httpClient.Dispose();
        }
    }

    private string PluginDataDirectory(string id)
    {
        var root = Path.Combine(_dataDirectory, "plugin-data");
        CheckLink(root);
        var directory = Path.Combine(root, id);
        CheckLink(directory);
        return directory;
    }

    private string ConfigurationPath(string id) => Path.Combine(PluginDataDirectory(id), "config.json");

    private string ReadConfigurationCore(string id)
    {
        var path = ConfigurationPath(id);
        CheckLink(path);
        var value = File.Exists(path) ? ReadText(path) : "{}";
        ValidateConfiguration(value);
        return value;
    }

    private static void ValidateConfiguration(string value)
    {
        if (Encoding.UTF8.GetByteCount(value) > JsonLimit)
            throw new InvalidDataException("插件配置超过 1 MiB。");
        try
        {
            using var document = JsonDocument.Parse(value, new JsonDocumentOptions { AllowDuplicateProperties = false });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("插件配置必须是 JSON 对象。");
        }
        catch (JsonException error) { throw new InvalidDataException("插件配置不是有效 JSON。", error); }
    }

    private static string ReadText(string path)
    {
        CheckLink(path);
        using var stream = File.OpenRead(path);
        if (stream.Length > JsonLimit)
            throw new InvalidDataException("JSON 文件超过 1 MiB。");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
        return reader.ReadToEnd();
    }

    private static T ReadJson<T>(string path) where T : class =>
        JsonSerializer.Deserialize<T>(ReadText(path), Json) ??
        throw new InvalidDataException("JSON 内容为空。");

    private static void WriteAtomic(string path, string contents)
    {
        CheckLink(path);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, contents);
            File.Move(temporary, path, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }

    private static bool IsFileError(Exception error) => error is IOException or UnauthorizedAccessException or
        JsonException or InvalidDataException or ArgumentException or NotSupportedException;

    private sealed record PluginSettings(string[] Enabled, Dictionary<string, string>? Consents = null);
    private sealed record LocalPlugin(PluginManifest Manifest, string Directory);
    private static bool SameInstallation(LocalPlugin left, LocalPlugin right) =>
        left.Directory == right.Directory &&
        left.Manifest with { Permissions = null } == right.Manifest with { Permissions = null } &&
        PermissionsEqual(left.Manifest.Permissions, right.Manifest.Permissions);
    private sealed record RunningPlugin(IWonderPlugin Instance, PluginLoadContext Loader);

    private sealed record PluginContext(string ConfigurationJson, string DataDirectory, ActivityLog Activity) : IPluginContext
    {
        public void Log(string message) => Activity.Add(message);
    }

    private sealed class ActivityLog
    {
        private readonly Queue<string> _messages = new();

        public void Add(string message)
        {
            ArgumentNullException.ThrowIfNull(message);
            lock (_messages)
            {
                if (_messages.Count == 100) _messages.Dequeue();
                _messages.Enqueue(message.Length <= 4096 ? message : message[..4096]);
            }
        }

        public string[] Snapshot()
        {
            lock (_messages) return _messages.ToArray();
        }
    }

    private sealed class PluginLoadContext(string directory, string entryAssembly) : AssemblyLoadContext(isCollectible: true)
    {
        private static readonly Assembly Contracts = typeof(IWonderPlugin).Assembly;
        private readonly AssemblyDependencyResolver _resolver = new(entryAssembly);

        protected override Assembly? Load(AssemblyName name)
        {
            if (string.Equals(name.Name, Contracts.GetName().Name, StringComparison.Ordinal))
                return Contracts;
            var dependency = _resolver.ResolveAssemblyToPath(name);
            if (dependency is null)
            {
                if (name.Name is null || !IsPathSegment(name.Name)) return null;
                var sibling = Path.Combine(Path.GetDirectoryName(entryAssembly)!, name.Name + ".dll");
                if (File.Exists(sibling)) dependency = sibling;
            }
            return dependency is null ? null : LoadManaged(dependency);
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var dependency = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return dependency is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(CheckedLocalPath(directory, dependency));
        }

        internal Assembly LoadManaged(string path)
        {
            using var stream = File.OpenRead(CheckedLocalPath(directory, path));
            return LoadFromStream(stream);
        }
    }
}
