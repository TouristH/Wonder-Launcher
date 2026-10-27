// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Wonder.Application;
using Wonder.Core;
using static Wonder.Infrastructure.LocalFiles;

namespace Wonder.Infrastructure;

public sealed class LocalGameLauncher(Func<string, CancellationToken, Task<JavaRuntime>>? javaProbe = null, string? launcherVersion = null) : IGameLauncher
{
    public async Task<LaunchPlan> BuildPlanAsync(LaunchRequest request, CancellationToken ct = default)
    {
        request.Instance.Validate();
        var session = request.Session ?? CreateOfflineSession(request.OfflineUsername);
        AccountLaunchSecurity.ValidateSession(session);
        request = request with { Session = session };
        var instance = request.Instance;
        var settings = instance.EffectiveSettings;
        var executable = settings.JavaPath ?? FindJava(instance.RootDirectory);
        var java = await (javaProbe ?? JavaRuntime.Probe)(executable, ct).ConfigureAwait(false);
        return await Task.Run(() => Build(request, java, launcherVersion ?? LauncherBuildInfo.Version, ct), ct).ConfigureAwait(false);
    }

    private static LaunchPlan Build(LaunchRequest request, JavaRuntime java, string launcherVersion, CancellationToken ct)
    {
        var instance = request.Instance; var settings = instance.EffectiveSettings;
        var session = request.Session!;
        var userType = session.Kind switch { AccountKind.Microsoft => "msa", AccountKind.Yggdrasil => "mojang", _ => "legacy" };
        var authSession = session.Kind == AccountKind.Offline ? "0" : "token:" + session.AccessToken + ":" + session.Uuid;
        var notices = new List<string>();
        if (session.Kind == AccountKind.Offline && session.OfflineSkinId is { } skinId)
            notices.Add("离线默认皮肤：" + OfflineDefaultSkins.Get(skinId).Name + "。" + OfflineDefaultSkins.Get(skinId).Description + "服务器或皮肤模组可能覆盖客户端默认外观。");
        var prism = instance.Source is InstanceSource.Prism or InstanceSource.MultiMc;
        var documents = new List<JsonElement>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void ReadVersion(string path)
        {
            ct.ThrowIfCancellationRequested();
            if (seen.Count >= 32 || !seen.Add(Path.GetFullPath(path))) throw new InvalidDataException("版本继承存在循环或层数过多。");
            var json = Json(Required(path));
            if (json.Get("inheritsFrom").Text() is { } parent)
                ReadVersion(Path.Combine(instance.VersionsDirectory, Segment(parent), parent + ".json"));
            documents.Add(json);
        }
        if (prism)
        {
            // Re-read the pack so a newly missing or disabled component cannot turn into a partial launch.
            var pack = Json(Path.Combine(instance.SourcePath, "mmc-pack.json"));
            foreach (var component in pack.Get("components").Items())
            {
                if (component.Get("disabled").Boolean() == true) continue;
                var uid = Segment(component.Get("uid").Text() ?? ""); var version = Segment(component.Get("version").Text() ?? "");
                var patch = Path.Combine(instance.SourcePath, "patches", uid + ".json");
                var path = File.Exists(patch) ? patch : Path.Combine(instance.RootDirectory, "meta", uid, version + ".json");
                if (!File.Exists(path)) throw new InvalidDataException($"缺少组件元数据：{uid} {version}。请在原启动器补全后刷新。");
                if (documents.Count >= 128) throw new InvalidDataException("组件数量超出限制。");
                documents.Add(Json(path));
            }
        }
        else
        {
            if (instance.MetadataPaths.Count != 1) throw new InvalidDataException("标准实例缺少版本描述。");
            ReadVersion(instance.MetadataPaths[0]);
        }
        if (documents.Count == 0) throw new InvalidDataException("实例缺少可用的版本元数据。");
        var features = new Dictionary<string, bool> { ["has_custom_resolution"] = true, ["is_demo_user"] = false };
        var libraries = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var jvm = new List<string>(); var game = new List<string>(); var tweakers = new List<string>();
        string? main = null, client = null, assetId = null; JsonElement logging = default; var requiredJava = 0;
        foreach (var document in documents)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var unsupported in new[] { "jarMods", "+jarMods", "agents", "+agents" })
                if (document.Get(unsupported).Items().Any()) throw new InvalidDataException("此实例需要尚未支持的 " + unsupported + "；请使用原启动器。");
            var traits = document.Get("+traits").Items().Concat(document.Get("traits").Items()).Select(x => x.Text()).Where(x => x is not null && x != "FirstThreadOnMacOS").ToArray();
            if (traits.Length > 0) throw new InvalidDataException("此实例需要尚未支持的启动特性：" + string.Join(", ", traits));
            main = document.Get("mainClass").Text() ?? main;
            assetId = document.Get("assetIndex").Get("id").Text() ?? document.Get("assets").Text() ?? assetId;
            requiredJava = Math.Max(requiredJava, document.Get("javaVersion").Get("majorVersion").Number() ?? 0);
            if (document.Get("logging").Get("client").ValueKind == JsonValueKind.Object) logging = document.Get("logging").Get("client");
            if (prism && document.Get("mainJar").Get("name").Text() is { } mainJar)
                client = LibraryPath(instance, document.Get("mainJar"), mainJar);
            else if (!prism && (document.Get("jar").Text() ?? (document.Get("inheritsFrom").Text() is null ? document.Get("id").Text() : null)) is { } jar)
                client = Path.Combine(instance.VersionsDirectory, Segment(jar), jar + ".jar");
            foreach (var lib in document.Get("libraries").Items().Concat(document.Get("+libraries").Items()))
                libraries[LaunchArguments.LibraryKey(lib)] = lib;
            var args = document.Get("arguments");
            jvm.AddRange(LaunchArguments.Values(args.Get("jvm"), java.Architecture, features));
            game.AddRange(LaunchArguments.Values(args.Get("game"), java.Architecture, features));
            if (document.Get("minecraftArguments").Text() is { } legacy) game = LaunchArguments.Split(legacy);
            tweakers.AddRange(document.Get("+tweakers").Items().Select(x => x.Text() ?? ""));
        }
        if (requiredJava > java.MajorVersion) throw new InvalidDataException($"此实例需要 Java {requiredJava} 或更新版本，当前为 Java {java.MajorVersion}。");
        if (string.IsNullOrWhiteSpace(main) || main.Any(char.IsWhiteSpace) || main.StartsWith('-')) throw new InvalidDataException("实例主类缺失或无效。");
        if (client is null) throw new InvalidDataException("实例缺少客户端 JAR 描述。");
        var classPath = new List<string>();
        var nativeArchives = new List<(string Path, string[] Exclude)>();
        var os = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "osx" : "linux";
        foreach (var lib in libraries.Values)
        {
            ct.ThrowIfCancellationRequested();
            if (!LaunchArguments.Allowed(lib, java.Architecture, features)) continue;
            var name = lib.Get("name").Text()!;
            var downloads = lib.Get("downloads");
            var native = lib.Get("natives").Get(os).Text();
            // Classifier-only entries have no classpath artifact.
            if (native is null || downloads.Get("artifact").ValueKind == JsonValueKind.Object || downloads.ValueKind == JsonValueKind.Undefined)
                classPath.Add(Required(LibraryPath(instance, lib, name)));
            if (native is not null)
            {
                native = native.Replace("${arch}", java.Architecture.Contains("64", StringComparison.Ordinal) ? "64" : "32");
                var path = downloads.Get("classifiers").Get(native).Get("path").Text() ?? LaunchArguments.Maven(name, native);
                nativeArchives.Add((Required(Under(instance.LibraryDirectory, path)), lib.Get("extract").Get("exclude").Items().Select(x => x.Text() ?? "").ToArray()));
            }
        }
        classPath.Add(Required(client));
        var nativeFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", nativeArchives.Select(x => x.Path + "|" + new FileInfo(x.Path).Length + "|" + File.GetLastWriteTimeUtc(x.Path).Ticks)))))[..16];
        var natives = Under(Path.Combine(Path.GetFullPath(request.CacheDirectory), "natives", instance.Id), nativeFingerprint);
        if (nativeArchives.Count > 0) ExtractNatives(nativeArchives, natives, ct);
        else Directory.CreateDirectory(natives);
        var assets = ValidateAssets(instance.AssetsDirectory, assetId, ct);
        var variables = new Dictionary<string, string>
        {
            ["auth_player_name"] = session.Username, ["auth_uuid"] = session.Uuid,
            ["auth_access_token"] = session.AccessToken, ["auth_session"] = authSession, ["user_type"] = userType,
            ["user_properties"] = "{}", ["clientid"] = session.ClientId, ["auth_xuid"] = session.Xuid,
            ["version_name"] = instance.VersionId, ["version_type"] = "release", ["game_directory"] = settings.GameDirectory!,
            ["assets_root"] = instance.AssetsDirectory, ["assets_index_name"] = assetId ?? "", ["game_assets"] = assets,
            ["natives_directory"] = natives, ["launcher_name"] = "Wonder", ["launcher_version"] = launcherVersion, ["classpath"] = string.Join(Path.PathSeparator, classPath.Distinct(StringComparer.OrdinalIgnoreCase)),
            ["classpath_separator"] = Path.PathSeparator.ToString(), ["library_directory"] = instance.LibraryDirectory,
            ["resolution_width"] = settings.WindowWidth!.Value.ToString(), ["resolution_height"] = settings.WindowHeight!.Value.ToString()
        };
        if (jvm.Count == 0) jvm.AddRange(["-Djava.library.path=${natives_directory}", "-cp", "${classpath}"]);
        if (!jvm.Any(x => x.StartsWith("-Djava.library.path=", StringComparison.Ordinal))) jvm.Add("-Djava.library.path=${natives_directory}");
        jvm = jvm.Select(x => LaunchArguments.Expand(x, variables)).ToList();
        var extraJvm = LaunchArguments.Split(settings.JvmArguments);
        if (extraJvm.Any(x => x is "-cp" or "-classpath" or "--class-path" or "-jar" || x.StartsWith('@'))) throw new InvalidDataException("额外 JVM 参数不能替换主类、类路径或使用参数文件。");
        jvm.AddRange(extraJvm);
        jvm.RemoveAll(x => x.StartsWith("-Xms", StringComparison.Ordinal) || x.StartsWith("-Xmx", StringComparison.Ordinal));
        jvm.AddRange([$"-Xms{settings.MinimumMemoryMb}M", $"-Xmx{settings.MaximumMemoryMb}M"]);
        // Java's stdout/stderr charset can differ from file.encoding on Windows.
        var outputEncodings = new[] { "stdout.encoding", "stderr.encoding", "sun.stdout.encoding", "sun.stderr.encoding" };
        jvm.RemoveAll(x => outputEncodings.Any(key => x.StartsWith("-D" + key + "=", StringComparison.Ordinal)));
        jvm.AddRange(outputEncodings.Select(key => "-D" + key + "=UTF-8"));
        if (LoggingConfiguration.Argument(logging, instance.AssetsDirectory, request.CacheDirectory, notices) is { } logArgument) jvm.Add(logArgument);
        // Validate every metadata/user-controlled JVM argument, including the optional logging descriptor.
        AccountLaunchSecurity.ValidateJvmArguments(jvm, session.Kind);
        if (session.Kind == AccountKind.Yggdrasil)
        {
            AccountLaunchSecurity.VerifyAgent(session.AuthlibInjectorPath, session.AuthlibInjectorSha256);
            jvm.Add("-javaagent:" + session.AuthlibInjectorPath + "=" + session.ServerUrl);
        }
        if (game.Count == 0) game.AddRange(["--username", "${auth_player_name}", "--version", "${version_name}", "--gameDir", "${game_directory}", "--assetsDir", "${assets_root}", "--assetIndex", "${assets_index_name}", "--uuid", "${auth_uuid}", "--accessToken", "0", "--userType", "legacy"]);
        game = game.Select(x => LaunchArguments.Expand(x, variables)).ToList();
        foreach (var (option, value) in new[] { ("--username", session.Username), ("--uuid", session.Uuid), ("--accessToken", session.AccessToken), ("--userType", userType), ("--gameDir", settings.GameDirectory!) })
        { RemoveOption(game, option, true); game.AddRange([option, value]); }
        foreach (var (option, value) in new[] { ("--session", authSession), ("--clientId", session.ClientId), ("--xuid", session.Xuid), ("--userProperties", "{}") })
            if (game.Any(x => x.Equals(option, StringComparison.OrdinalIgnoreCase) || x.StartsWith(option + "=", StringComparison.OrdinalIgnoreCase)))
            { RemoveOption(game, option, true); game.AddRange([option, value]); }
        foreach (var tweaker in tweakers) game.AddRange(["--tweakClass", tweaker]);
        foreach (var option in new[] { "--width", "--height", "--fullscreen" }) RemoveOption(game, option, option != "--fullscreen");
        game.AddRange(["--width", settings.WindowWidth!.Value.ToString(), "--height", settings.WindowHeight!.Value.ToString()]);
        if (settings.FullScreen == true) game.Add("--fullscreen");
        var extraGame = LaunchArguments.Split(settings.GameArguments);
        var reserved = new[] { "--username", "--uuid", "--accessToken", "--session", "--userType", "--userProperties", "--clientId", "--xuid", "--gameDir", "--width", "--height", "--fullscreen" };
        if (extraGame.Any(x => reserved.Any(key => x.Equals(key, StringComparison.OrdinalIgnoreCase) || x.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))))
            throw new InvalidDataException("额外游戏参数不能覆盖账户身份、游戏目录或窗口设置；请使用对应设置项。");
        game.AddRange(extraGame);
        ct.ThrowIfCancellationRequested();
        return new(java.Executable, settings.GameDirectory!, jvm.Concat([main]).Concat(game), instance.Id, notices,
            session.Kind == AccountKind.Offline ? [] : [session.AccessToken], session.Kind != AccountKind.Offline,
            session.AuthlibInjectorPath, session.AuthlibInjectorSha256);
    }

    private static void RemoveOption(List<string> args, string option, bool value)
    {
        for (var index = args.Count - 1; index >= 0; index--)
            if (args[index].Equals(option, StringComparison.OrdinalIgnoreCase)) { if (value && index + 1 < args.Count && !args[index + 1].StartsWith("--", StringComparison.Ordinal)) args.RemoveAt(index + 1); args.RemoveAt(index); }
            else if (args[index].StartsWith(option + "=", StringComparison.OrdinalIgnoreCase)) args.RemoveAt(index);
    }
    private static string LibraryPath(GameInstance instance, JsonElement lib, string name)
    {
        var local = lib.Get("MMC-hint").Text() == "local";
        var relative = local ? lib.Get("MMC-filename").Text() ?? Path.GetFileName(LaunchArguments.Maven(name)) : lib.Get("downloads").Get("artifact").Get("path").Text() ?? LaunchArguments.Maven(name);
        return Under(local ? instance.LocalLibraryDirectory ?? throw new InvalidDataException("缺少本地依赖库目录。") : instance.LibraryDirectory, relative);
    }
    private static string ValidateAssets(string root, string? id, CancellationToken ct)
    {
        if (id is null) return root;
        var index = Json(Required(Path.Combine(root, "indexes", Segment(id) + ".json")));
        if (index.Get("virtual").Boolean() == true || index.Get("map_to_resources").Boolean() == true)
            throw new InvalidDataException("此版本使用旧式资源映射，当前请使用原启动器启动。");
        var objects = index.Get("objects");
        if (objects.ValueKind != JsonValueKind.Object) throw new InvalidDataException("资产索引缺少 objects。");
        foreach (var item in objects.EnumerateObject())
        {
            ct.ThrowIfCancellationRequested(); var hash = item.Value.Get("hash").Text();
            if (hash is null || hash.Length != 40 || !hash.All(char.IsAsciiHexDigit)) throw new InvalidDataException("资产索引包含无效哈希。");
            Required(Path.Combine(root, "objects", hash[..2], hash));
        }
        return root;
    }
    private static void ExtractNatives(List<(string Path, string[] Exclude)> archives, string target, CancellationToken ct)
    {
        Directory.CreateDirectory(target); long total = 0; var count = 0;
        foreach (var (path, exclude) in archives)
        {
            using var archive = ZipFile.OpenRead(path);
            foreach (var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();
                var destination = Under(target, entry.FullName);
                if (entry.FullName.EndsWith('/') || entry.FullName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase) || exclude.Any(x => entry.FullName.StartsWith(x, StringComparison.Ordinal))) continue;
                if (++count > 10000 || (total += entry.Length) > 512L * 1024 * 1024) throw new InvalidDataException("原生库归档超过提取限制。");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, true);
            }
        }
    }
    private static string Required(string path) => File.Exists(path) ? path : throw new FileNotFoundException("缺少本地文件，请在原启动器补全：" + path, path);
    private static string FindJava(string root)
    {
        var home = Environment.GetEnvironmentVariable("JAVA_HOME");
        var name = OperatingSystem.IsWindows() ? "java.exe" : "java";
        foreach (var candidate in new[] { home is null ? "" : Path.Combine(home, "bin", name), Path.Combine(root, "runtime", "bin", name) })
            if (File.Exists(candidate)) return candidate;
        return name;
    }
    private static string OfflineUuid(string username)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + username));
        bytes[6] = (byte)((bytes[6] & 0x0f) | 0x30); bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
    private static GameSession CreateOfflineSession(string username)
    {
        InstanceCatalog.ValidateOfflineUsername(username);
        return new(AccountKind.Offline, username, OfflineUuid(username), "0");
    }
    public async Task<int> RunAsync(LaunchPlan plan, Action started, Action<string> output, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested(); Directory.CreateDirectory(plan.WorkingDirectory);
        if (plan.AuthlibInjectorPath is not null) AccountLaunchSecurity.VerifyAgent(plan.AuthlibInjectorPath, plan.AuthlibInjectorSha256);
        using var process = new Process { StartInfo = JavaRuntime.StartInfo(plan.JavaPath, plan.WorkingDirectory, plan.Arguments) };
        if (plan.HasAuthenticatedAccount)
            foreach (var key in new[] { "JAVA_TOOL_OPTIONS", "JDK_JAVA_OPTIONS", "_JAVA_OPTIONS", "CLASSPATH" }) process.StartInfo.Environment.Remove(key);
        process.Start(); started();
        // Once running, view cancellation must not terminate the game or stop draining its pipes.
        var stdout = Pump(process.StandardOutput, line => output(plan.RedactOutput(line)));
        var stderr = Pump(process.StandardError, line => output("[stderr] " + plan.RedactOutput(line)));
        await process.WaitForExitAsync().ConfigureAwait(false); await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
        return process.ExitCode;
    }
    private static async Task Pump(StreamReader reader, Action<string> output)
    {
        var buffer = new char[2048]; var line = new StringBuilder(); int count; var overflow = false;
        while ((count = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0)
            for (var i = 0; i < count; i++)
            {
                var c = buffer[i];
                if (c == '\n') { output(overflow ? "[超长日志已隐藏]" : line.ToString().TrimEnd('\r')); line.Clear(); overflow = false; }
                else if (line.Length < 65536 && !overflow) line.Append(c);
                else { line.Clear(); overflow = true; }
            }
        if (overflow) output("[超长日志已隐藏]");
        else if (line.Length > 0) output(line.ToString());
    }
}
