// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Reflection;
using Wonder.Core;
using Wonder.Infrastructure;

namespace Wonder.Game.Tests;

public sealed class WonderLauncherVersionTests
{
    [Theory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(false, "8.6.4-preview.2")]
    [InlineData(true, "8.6.4-preview.2")]
    public async Task Launcher_version_expands_to_the_built_or_explicit_version_in_actual_arguments(bool legacy, string? suppliedVersion)
    {
        using var fixture = new ImporterFixtures();
        var versionDirectory = fixture.Version();
        fixture.Write(".minecraft/versions/1.20.1/1.20.1.jar", "synthetic client jar");
        fixture.Json(".minecraft/versions/1.20.1/1.20.1.json", new
        {
            id = "1.20.1", mainClass = "Main",
            arguments = new
            {
                jvm = new[] { "-Dwonder.launcher.version=${launcher_version}", "-cp", "${classpath}" },
                game = legacy ? Array.Empty<string>() : new[] { "--launcherVersion", "${launcher_version}" }
            },
            minecraftArguments = legacy ? "--launcherVersion ${launcher_version}" : null
        });
        var instance = Assert.Single((await new InstanceDiscovery().DiscoverAsync(versionDirectory)).Instances);
        var launcher = suppliedVersion is null
            ? new LocalGameLauncher((path, _) => Task.FromResult(new JavaRuntime(path, 21, "amd64")))
            : new LocalGameLauncher((path, _) => Task.FromResult(new JavaRuntime(path, 21, "amd64")), launcherVersion: suppliedVersion);

        var plan = await launcher.BuildPlanAsync(new(instance, "Player", fixture.DirectoryAt("cache")));

        var expected = suppliedVersion ?? typeof(GameInstance).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
        Assert.Contains("-Dwonder.launcher.version=" + expected, plan.Arguments);
        var arguments = plan.Arguments.ToList();
        Assert.Equal(expected, arguments[arguments.IndexOf("--launcherVersion") + 1]);
    }
}
