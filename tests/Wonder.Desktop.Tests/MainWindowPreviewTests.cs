// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Xunit;

namespace Wonder.Desktop.Tests;

public sealed class MainWindowPreviewTests
{
    [AvaloniaFact]
    public void First_run_window_renders_brand_and_settings_workspace()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Wonder.Desktop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var workspacePath = Path.Combine(directory, "workspace.json");
        var window = new MainWindow(workspacePath);
        try
        {
            window.Show();
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.Equal(new PixelSize(1180, 780), frame.PixelSize);
            var workspace = Assert.Single(window.GetVisualDescendants().OfType<WorkspaceView>());
            Assert.Single(workspace.Document.Areas);
            Assert.Equal(Wonder.Workspace.ComponentIds.Settings, Assert.Single(workspace.Document.Areas.Single().Components).DefinitionId);
            var configuredPath = Environment.GetEnvironmentVariable("WONDER_MAIN_WINDOW_PREVIEW_PATH");
            var path = string.IsNullOrWhiteSpace(configuredPath)
                ? Path.Combine(AppContext.BaseDirectory, "artifacts", "wonder-main-window.png")
                : Path.GetFullPath(configuredPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            frame.Save(path, PngBitmapEncoderOptions.Default);
            Assert.True(new FileInfo(path).Length > 1000);
        }
        finally
        {
            window.Close();
            File.Delete(workspacePath);
            Directory.Delete(directory);
        }
    }
}
