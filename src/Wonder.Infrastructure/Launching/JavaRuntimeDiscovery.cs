// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Wonder.Core;

namespace Wonder.Infrastructure;

internal static class JavaRuntimeDiscovery
{
    internal static async Task<JavaRuntime> SelectAsync(GameInstance instance, IReadOnlyList<int> majors,
        Func<string, CancellationToken, Task<JavaRuntime>> probe, CancellationToken ct)
    {
        var detected = new HashSet<int>();
        JavaRuntime? match = null;
        InvalidDataException? probeError = null;
        foreach (var executable in Candidates(instance).Distinct(WonderPath.Comparer))
        {
            ct.ThrowIfCancellationRequested();
            JavaRuntime java;
            try { java = await probe(executable, ct).ConfigureAwait(false); }
            catch (InvalidDataException ex) { probeError ??= ex; continue; }
            if (majors.Count == 0 || java.MajorVersion == majors[0]) return java;
            detected.Add(java.MajorVersion);
            if (majors.Contains(java.MajorVersion) && (match is null || java.MajorVersion < match.MajorVersion)) match = java;
        }
        if (match is not null) return match;

        var expected = string.Join(" / ", majors.Select(major => $"Java {major}"));
        var found = detected.Count == 0 ? "" : $"（已检测到 {string.Join("、", detected.Order().Select(major => $"Java {major}"))}）";
        throw new InvalidDataException(majors.Count == 0
            ? "未找到可用 Java。请安装 Java，或在实例设置中指定可执行文件。" + (probeError is null ? "" : "\n" + probeError.Message)
            : $"未找到此实例匹配的 {expected}{found}。请安装所需版本，或在实例设置中指定可执行文件。");
    }

    private static IEnumerable<string> Candidates(GameInstance instance)
    {
        var name = OperatingSystem.IsWindows() ? "java.exe" : "java";
        var localRoots = new[] { instance.RootDirectory, instance.DefaultGameDirectory, instance.GameDirectoryGroup };
        foreach (var root in localRoots.Distinct(WonderPath.Comparer))
        {
            var runtime = Path.Combine(root, "runtime");
            foreach (var home in JavaHomes(runtime)) yield return home;
            foreach (var component in Subdirectories(runtime))
            {
                foreach (var home in JavaHomes(component)) yield return home;
                // The official launcher groups runtimes by component and platform.
                foreach (var platform in Subdirectories(component))
                    foreach (var home in JavaHomes(Path.Combine(platform, Path.GetFileName(component)))) yield return home;
            }
        }
        if (instance.Source is InstanceSource.Prism or InstanceSource.MultiMc)
            foreach (var runtime in Subdirectories(Path.Combine(instance.RootDirectory, "java")))
                foreach (var home in JavaHomes(runtime)) yield return home;

        if (Environment.GetEnvironmentVariable("JAVA_HOME") is { } homePath)
            foreach (var home in JavaHomes(homePath.Trim().Trim('"'))) yield return home;
        foreach (var entry in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var directory = entry.Trim().Trim('"');
            if (!Path.IsPathFullyQualified(directory)) continue;
            var executable = Path.Combine(directory, name);
            if (File.Exists(executable)) yield return executable;
        }
        foreach (var directory in SystemJavaDirectories())
            foreach (var home in Subdirectories(directory))
                foreach (var executable in JavaHomes(home)) yield return executable;
        yield return name;

        IEnumerable<string> JavaHomes(string root)
        {
            if (!Path.IsPathFullyQualified(root)) yield break;
            var paths = OperatingSystem.IsMacOS() ? new[] { root, Path.Combine(root, "Contents", "Home"), Path.Combine(root, "jre.bundle", "Contents", "Home") } : [root];
            foreach (var path in paths)
            {
                var executable = Path.Combine(path, "bin", name);
                if (File.Exists(executable)) yield return executable;
            }
        }
    }

    private static IEnumerable<string> SystemJavaDirectories()
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (var programFiles in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }
                .Select(Environment.GetFolderPath).Where(path => path.Length != 0).Distinct(WonderPath.Comparer))
                foreach (var vendor in new[] { "Java", "Eclipse Adoptium", "Microsoft", "Zulu" }) yield return Path.Combine(programFiles, vendor);
        }
        else if (OperatingSystem.IsMacOS()) yield return "/Library/Java/JavaVirtualMachines";
        else if (OperatingSystem.IsLinux()) yield return "/usr/lib/jvm";
    }

    private static string[] Subdirectories(string path)
    {
        try { return Directory.GetDirectories(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }
}
