// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Wonder.Core;
using Wonder.Infrastructure;
using Xunit;

namespace Wonder.Desktop.Tests;

public sealed class WonderLauncherVersionTests
{
    [Fact]
    public void Desktop_core_and_launcher_assemblies_share_one_build_version()
    {
        var expected = typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        Assert.Equal(expected, typeof(GameInstance).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion);
        Assert.Equal(expected, typeof(LocalGameLauncher).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion);
        Assert.Equal(typeof(MainWindow).Assembly.GetName().Version, typeof(GameInstance).Assembly.GetName().Version);
        Assert.Equal(typeof(MainWindow).Assembly.GetName().Version, typeof(LocalGameLauncher).Assembly.GetName().Version);
    }

    [AvaloniaFact]
    public void Window_status_shows_the_version_of_the_built_desktop_assembly()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Wonder.Version.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var window = new MainWindow(Path.Combine(directory, "workspace.json"));
        try
        {
            window.Show();
            var status = Assert.Single(window.GetVisualDescendants().OfType<Button>(),
                button => button.Content is string text && text.StartsWith("WONDER  /  ", StringComparison.Ordinal));
            var expected = typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
            Assert.Equal("WONDER  /  " + expected, status.Content);
        }
        finally
        {
            window.Close();
            Directory.Delete(directory, recursive: true);
        }
    }
}
