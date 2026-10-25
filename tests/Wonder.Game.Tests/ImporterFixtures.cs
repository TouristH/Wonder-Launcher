// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Text.Json;

namespace Wonder.Game.Tests;

internal sealed class ImporterFixtures : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "Wonder-import-tests", Guid.NewGuid().ToString("N"));
    public ImporterFixtures() => Directory.CreateDirectory(Root);
    public string DirectoryAt(string relative)
    {
        var path = Path.GetFullPath(Path.Combine(Root, relative));
        Directory.CreateDirectory(path);
        return path;
    }
    public string Write(string relative, string content)
    {
        var path = Path.GetFullPath(Path.Combine(Root, relative));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }
    public string Json(string relative, object content) => Write(relative, JsonSerializer.Serialize(content));
    public string Version(string id = "1.20.1", string root = ".minecraft")
    {
        Json(Path.Combine(root, "versions", id, id + ".json"), new { id, mainClass = "net.minecraft.client.main.Main", libraries = Array.Empty<object>() });
        return Path.Combine(Root, root, "versions", id);
    }
    public string Prism(string name = "Pack")
    {
        var path = DirectoryAt(Path.Combine("Prism", "instances", name, "minecraft"));
        Write(Path.Combine("Prism", "instances", name, "instance.cfg"), "[General]\nname=测试整合包\nOverrideMemory=true\nMinMemAlloc=512\nMaxMemAlloc=4096\nOverrideWindow=true\nMinecraftWinWidth=1280\nMinecraftWinHeight=720\n");
        Json(Path.Combine("Prism", "instances", name, "mmc-pack.json"), new { formatVersion = 1, components = new[] { new { uid = "net.minecraft", version = "1.20.1" }, new { uid = "org.lwjgl3", version = "3.3.1" } } });
        Json(Path.Combine("Prism", "meta", "net.minecraft", "1.20.1.json"), new { uid = "net.minecraft", version = "1.20.1", mainClass = "net.minecraft.client.main.Main", mainJar = new { name = "com.mojang:minecraft:1.20.1:client" } });
        Json(Path.Combine("Prism", "meta", "org.lwjgl3", "3.3.1.json"), new { uid = "org.lwjgl3", version = "3.3.1", libraries = Array.Empty<object>() });
        Write(Path.Combine("Prism", "prismlauncher.cfg"), "[General]\nJavaPath=C:/Java/bin/javaw.exe\nMaxMemAlloc=2048\nJvmArgs=-Dglobal=true\n");
        return Path.GetDirectoryName(path)!;
    }
    public void Dispose()
    {
        if (Directory.Exists(Root)) Directory.Delete(Root, true);
    }
}
