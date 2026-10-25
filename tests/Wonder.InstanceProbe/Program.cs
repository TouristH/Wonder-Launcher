// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Diagnostics;
using System.Text.Json;
using Wonder.Core;
using Wonder.Infrastructure;

if (args.Length == 3 && args[0] == "--catalog")
{
    var catalog = new InstanceStore(args[1]).Load();
    await File.WriteAllTextAsync(args[2], JsonSerializer.Serialize(catalog.Instances.Select(x => new { x.Id, x.Name, x.GameVersion, x.ModLoaders, x.VersionLabel, x.RootDirectory, x.SourcePath }), new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"CATALOG: {catalog.Instances.Count} instances, {catalog.Instances.Count(x => x.GameVersion.Length > 0)} identified versions.");
    return;
}
if (args.Length < 2) throw new ArgumentException("Usage: Wonder.InstanceProbe <single instance directory> <report directory> [--run], or --catalog <catalog path> <report file>");
var output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
var discovered = await new InstanceDiscovery().DiscoverAsync(args[0]);
var instance = discovered.Instances.Single() with { Overrides = new() { GameDirectory = Path.Combine(output, "isolated-game") } };
var launcher = new LocalGameLauncher();
var plan = await launcher.BuildPlanAsync(new(instance, "Wonder_Test", Path.Combine(output, "cache")));
await File.WriteAllTextAsync(Path.Combine(output, "plan.json"), JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("PLAN OK: " + instance.Name);
if (!args.Contains("--run")) return;
var before = Process.GetProcessesByName("java").Select(p => { using (p) return p.Id; }).ToHashSet();
Process? game = null;
var log = new List<string>(); var gate = new object();
var running = launcher.RunAsync(plan, () =>
{
    var added = Process.GetProcessesByName("java").Where(p => !before.Contains(p.Id)).ToArray();
    if (added.Length == 1) game = added[0]; else foreach (var process in added) process.Dispose();
}, line => { lock (gate) log.Add(line); });
var deadline = DateTime.UtcNow.AddSeconds(90); string title = ""; bool window = false, sound = false, close = false;
while (!running.IsCompleted && DateTime.UtcNow < deadline)
{
    await Task.Delay(500); game?.Refresh();
    if (game is { HasExited: false }) { title = game.MainWindowTitle; window |= game.MainWindowHandle != IntPtr.Zero; }
    lock (gate) sound = log.Any(x => x.Contains("Sound engine started", StringComparison.Ordinal));
    if (window && sound)
    {
        // Keep the real game visible long enough for a screen capture, then request a normal close.
        Console.WriteLine("GAME READY: " + title + " PID=" + game!.Id);
        await Task.Delay(20000); close = game.CloseMainWindow(); break;
    }
}
if (!running.IsCompleted && !close && game is { HasExited: false }) close = game.CloseMainWindow();
var finished = await Task.WhenAny(running, Task.Delay(15000)) == running;
var exit = finished ? await running : (int?)null;
await File.WriteAllTextAsync(Path.Combine(output, "game-log.txt"), string.Join("\n", log));
await File.WriteAllTextAsync(Path.Combine(output, "game-result.json"), JsonSerializer.Serialize(new { Instance = instance.SourcePath, GameDirectory = plan.WorkingDirectory, Window = window, Title = title, SoundEngine = sound, CloseRequested = close, Finished = finished, ExitCode = exit, ProcessId = game?.Id }, new JsonSerializerOptions { WriteIndented = true }));
game?.Dispose();
if (!finished || exit != 0 || !window || !sound) throw new InvalidOperationException("Real instance probe failed; inspect game-result.json and game-log.txt.");
Console.WriteLine("PASS: real instance reached a game window with sound engine initialized and closed normally.");
