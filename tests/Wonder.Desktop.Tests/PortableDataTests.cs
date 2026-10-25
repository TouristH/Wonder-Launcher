// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Wonder.Workspace;
using Xunit;

namespace Wonder.Desktop.Tests;

public sealed class PortableDataTests
{
    [Fact]
    public void Default_paths_are_rooted_next_to_the_application_not_the_working_directory()
    {
        var expected = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "WonderData"));
        Assert.Equal(expected, LauncherDataPaths.RootDirectory);
        Assert.Equal(Path.Combine(expected, "workspace.json"), LauncherDataPaths.WorkspacePath);
        Assert.Equal(Path.Combine(expected, "settings.json"), LauncherDataPaths.SettingsPath);
        Assert.Equal(Path.Combine(expected, "plugins"), LauncherDataPaths.PluginsDirectory);
    }

    [AvaloniaFact]
    public void Starting_with_missing_directories_creates_them_before_any_save()
    {
        using var folder = new TemporaryFolder();
        var workspaceDirectory = Path.Combine(folder.Path, "layout");
        var settingsDirectory = Path.Combine(folder.Path, "preferences");
        var window = new MainWindow(Path.Combine(workspaceDirectory, "workspace.json"), Path.Combine(settingsDirectory, "settings.json"));
        try
        {
            Assert.True(Directory.Exists(workspaceDirectory));
            Assert.True(Directory.Exists(settingsDirectory));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Startup_directory_failure_is_visible_and_preserves_the_blocking_file()
    {
        using var folder = new TemporaryFolder();
        var blockedDirectory = Path.Combine(folder.Path, "blocked");
        File.WriteAllText(blockedDirectory, "keep this file");
        var window = new MainWindow(Path.Combine(blockedDirectory, "workspace.json"), Path.Combine(folder.Path, "settings.json"));
        try
        {
            window.Show();
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(),
                button => button.Content is string content && content.Contains("点击查看"));
            Assert.Equal("keep this file", File.ReadAllText(blockedDirectory));
        }
        finally { window.Close(); }
        Assert.Equal("keep this file", File.ReadAllText(blockedDirectory));
        Assert.False(File.Exists(Path.Combine(folder.Path, "workspace.json")));
    }

    [AvaloniaFact]
    public void Explicit_paths_load_existing_workspace_and_settings()
    {
        using var folder = new TemporaryFolder();
        var workspacePath = Path.Combine(folder.Path, "custom-layout.json");
        var settingsPath = Path.Combine(folder.Path, "custom-settings.json");
        new WorkspaceStore(workspacePath).Save(WorkspaceDocument.CreateEmpty());
        new LauncherSettingsStore(settingsPath).Save(new LauncherSettings { WorkspacePadding = 36 });
        var window = new MainWindow(workspacePath, settingsPath);
        try
        {
            window.Show();
            var workspace = Assert.Single(window.GetVisualDescendants().OfType<WorkspaceView>());
            Assert.Empty(workspace.Document.Areas);
            Assert.Equal(new Thickness(36), workspace.Margin);
        }
        finally { window.Close(); }
        Assert.Empty(new WorkspaceStore(workspacePath).Load().Areas);
        Assert.Equal(36, new LauncherSettingsStore(settingsPath).Load().WorkspacePadding);
        Assert.False(File.Exists(Path.Combine(folder.Path, "workspace.json")));
        Assert.False(File.Exists(Path.Combine(folder.Path, "settings.json")));
    }

    [AvaloniaFact]
    public void Omitted_settings_path_stays_beside_an_injected_workspace()
    {
        using var folder = new TemporaryFolder();
        new LauncherSettingsStore(Path.Combine(folder.Path, "settings.json")).Save(new LauncherSettings { WorkspacePadding = 30 });
        var window = new MainWindow(Path.Combine(folder.Path, "workspace.json"));
        try
        {
            window.Show();
            Assert.Equal(new Thickness(30), Assert.Single(window.GetVisualDescendants().OfType<WorkspaceView>()).Margin);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Invalid_injected_paths_are_rejected_without_using_a_fallback()
    {
        using var folder = new TemporaryFolder();
        Assert.Throws<ArgumentException>(() => new MainWindow("\0"));
        Assert.Throws<ArgumentException>(() => new MainWindow(Path.Combine(folder.Path, "workspace.json"), "\0"));
        Assert.Empty(Directory.GetFileSystemEntries(folder.Path));
    }

    private sealed class TemporaryFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Wonder.Portable.Tests", Guid.NewGuid().ToString("N"));

        public TemporaryFolder() => Directory.CreateDirectory(Path);

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
