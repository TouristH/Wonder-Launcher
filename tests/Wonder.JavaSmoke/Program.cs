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
        for (String arg : args) System.out.println("arg=" + arg);
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
var report = new { Fixture = fixture, ExitCode = exit, Started = started, Game = "Synthetic Java fixture, not Minecraft", Log = log };
await File.WriteAllTextAsync(Path.Combine(output, "java-smoke.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("PASS: actual Java launch, offline arguments, Unicode and spaces, stdout/stderr, normal exit.");
