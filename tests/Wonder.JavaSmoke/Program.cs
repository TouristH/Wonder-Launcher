// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using Wonder.Core;
using Wonder.Infrastructure;

if (args.Length != 2) throw new ArgumentException("Usage: Wonder.JavaSmoke <JDK bin directory> <output directory>");
var javaBin = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
var fixture = Path.Combine(output, "中文 带空格实例 " + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(fixture);
var source = Path.Combine(fixture, "Smoke.java");
await File.WriteAllTextAsync(source, """
public class Smoke {
    public static void main(String[] args) {
        System.out.println("WONDER_PROCESS_OK");
        System.out.println("message=" + System.getProperty("message"));
        System.out.println("inherited=" + System.getProperty("wonder.inherited"));
        for (String arg : args) System.out.println("arg=" + arg);
        for (int i = 0; i < args.length - 1; i++) if ("--uuid".equals(args[i])) {
            String id = args[i + 1];
            if (id.length() == 32) id = id.substring(0,8) + "-" + id.substring(8,12) + "-" + id.substring(12,16) + "-" + id.substring(16,20) + "-" + id.substring(20);
            int hash = java.util.UUID.fromString(id).hashCode();
            System.out.println("default-skin-index=" + Math.floorMod(hash, 18));
            System.out.println("legacy-skin=" + ((hash & 1) == 0 ? "Steve" : "Alex"));
        }
        System.err.println("WONDER_STDERR_OK");
    }
}
""");
var compile = new ProcessStartInfo(Path.Combine(javaBin, "javac.exe")) { UseShellExecute = false, CreateNoWindow = true };
compile.ArgumentList.Add("-encoding"); compile.ArgumentList.Add("UTF-8"); compile.ArgumentList.Add(source);
using (var compiler = Process.Start(compile)!) { await compiler.WaitForExitAsync(); if (compiler.ExitCode != 0) throw new InvalidOperationException("javac failed"); }
var version = Path.Combine(fixture, "versions", "smoke"); Directory.CreateDirectory(version);
using (var zip = ZipFile.Open(Path.Combine(version, "smoke.jar"), ZipArchiveMode.Create)) zip.CreateEntryFromFile(Path.Combine(fixture, "Smoke.class"), "Smoke.class");
await File.WriteAllTextAsync(Path.Combine(version, "smoke.json"), JsonSerializer.Serialize(new { id = "smoke", mainClass = "Smoke", arguments = new { jvm = new[] { "-cp", "${classpath}" }, game = new[] { "--fixture", "argument with spaces" } }, libraries = Array.Empty<object>() }));
var instance = (await new InstanceDiscovery().DiscoverAsync(fixture)).Instances.Single() with { Overrides = new() { JavaPath = Path.Combine(javaBin, "java.exe"), MinimumMemoryMb = 32, MaximumMemoryMb = 128, JvmArguments = "-Dmessage=\"hello world\" -Dfile.encoding=UTF-8" } };
var launcher = new LocalGameLauncher();
var plan = await launcher.BuildPlanAsync(new(instance, "Wonder_Test", Path.Combine(fixture, "cache")));
var log = new List<string>(); var gate = new object(); var started = false;
var exit = await launcher.RunAsync(plan, () => started = true, line => { lock (gate) log.Add(line); });
if (!started || exit != 0 || !log.Contains("WONDER_PROCESS_OK") || !log.Contains("message=hello world") || !log.Contains("arg=argument with spaces") || !log.Contains("arg=" + fixture) || !log.Contains("[stderr] WONDER_STDERR_OK"))
    throw new InvalidOperationException("Java process assertions failed:\n" + string.Join("\n", log));
// Exercise the real child process with synthetic online credentials; never contact authentication services.
var syntheticToken = "WonderSyntheticAccess-" + new string('x', 3000) + "-end";
var online = await launcher.BuildPlanAsync(new(instance, "Wonder_Test", Path.Combine(fixture, "cache"))
{
    Session = new(AccountKind.Microsoft, "Wonder_Online", "0123456789abcdef0123456789abcdef", syntheticToken,
        "11111111-2222-4333-8444-555555555555", "123456789")
});
var onlineLog = new List<string>();
var inherited = Environment.GetEnvironmentVariable("JAVA_TOOL_OPTIONS");
int onlineExit;
try
{
    Environment.SetEnvironmentVariable("JAVA_TOOL_OPTIONS", "-Dwonder.inherited=must-not-reach-online-game");
    onlineExit = await launcher.RunAsync(online, () => { }, line => { lock (gate) onlineLog.Add(line); });
}
finally { Environment.SetEnvironmentVariable("JAVA_TOOL_OPTIONS", inherited); }
if (onlineExit != 0 || onlineLog.Any(line => line.Contains(syntheticToken) || line.Contains("WonderSyntheticAccess") || line.Contains("must-not-reach-online-game")) ||
    !onlineLog.Contains("arg=[已隐藏凭据]") || !onlineLog.Contains("inherited=null") || !onlineLog.Contains("arg=Wonder_Online"))
    throw new InvalidOperationException("Synthetic authenticated Java process or output redaction failed.");
var skinUuid = OfflineDefaultSkins.CreateUuid("Wonder_Test", "steve-classic");
var skinPlan = await launcher.BuildPlanAsync(new(instance, "Wonder_Test", Path.Combine(fixture, "cache"))
{ Session = new(AccountKind.Offline, "Wonder_Test", skinUuid, "0", offlineSkinId: "steve-classic") });
var skinLog = new List<string>();
var skinExit = await launcher.RunAsync(skinPlan, () => { }, line => { lock (gate) skinLog.Add(line); });
if (skinExit != 0 || !skinLog.Contains("arg=" + skinUuid) || !skinLog.Contains("default-skin-index=15") || !skinLog.Contains("legacy-skin=Alex"))
    throw new InvalidOperationException("Offline skin UUID did not reach Java or disagreed with Java default selection rules.");
var report = new { Fixture = fixture, ExitCode = exit, Started = started, Game = "Synthetic Java fixture, not Minecraft", Log = log,
    OfflineSkinUuidVerifiedByJava = true, OfflineSkinLog = skinLog,
    SyntheticAuthenticatedExitCode = onlineExit, OnlineLog = onlineLog, OnlineOutputRedacted = true, InheritedJavaOptionsRemoved = true };
await File.WriteAllTextAsync(Path.Combine(output, "java-smoke.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("PASS: actual Java launch, offline/online identity, chosen skin UUID and Java hash mapping, Unicode and spaces, stdout/stderr, long-token redaction, inherited Java options removed. No live authentication performed.");
