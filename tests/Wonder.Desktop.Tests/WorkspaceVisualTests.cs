// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Wonder.Workspace;
using Xunit;

namespace Wonder.Desktop.Tests;

public sealed partial class WorkspaceInteractionTests
{
    [AvaloniaTheory]
    [InlineData(0, .1, .5)]
    [InlineData(1, .9, .5)]
    public void Polygon_hover_and_pressed_feedback_reaches_outer_lobes_without_filling_corners(
        int slot, double xFraction, double yFraction)
    {
        using var desktop = new TestDesktop();
        var component = desktop.Component(slot);
        var bounds = desktop.View.GetComponentBounds(component.Id);
        var lobe = new Point(bounds.X + bounds.Width * xFraction, bounds.Y + bounds.Height * yFraction);
        var corner = new Point(bounds.X + bounds.Width * .06, bounds.Y + bounds.Height * .06);
        desktop.Window.MouseMove(new Point(2, 2));
        using var normal = desktop.Window.CaptureRenderedFrame();
        Assert.NotNull(normal);

        desktop.Window.MouseMove(bounds.Center);
        using var hovered = desktop.Window.CaptureRenderedFrame();
        Assert.NotNull(hovered);
        Assert.NotEqual(Pixel(normal, lobe), Pixel(hovered, lobe));
        Assert.Equal(Pixel(normal, corner), Pixel(hovered, corner));

        desktop.Press(bounds.Center);
        using var pressed = desktop.Window.CaptureRenderedFrame();
        Assert.NotNull(pressed);
        Assert.NotEqual(Pixel(hovered, lobe), Pixel(pressed, lobe));
        Assert.Equal(Pixel(normal, corner), Pixel(pressed, corner));
        desktop.Escape();
        desktop.Release(bounds.Center);
    }

    [AvaloniaFact]
    public void Keyboard_focus_and_hover_render_as_full_polygon_highlights()
    {
        using var desktop = new TestDesktop();
        var firstShortcut = desktop.Component(0);
        var firstBounds = desktop.View.GetComponentBounds(firstShortcut.Id);
        var secondBounds = desktop.View.GetComponentBounds(desktop.Component(1).Id);
        desktop.Window.MouseMove(new Point(2, 2));
        using var normal = desktop.Window.CaptureRenderedFrame();
        Assert.NotNull(normal);

        desktop.Window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        desktop.Window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        var focus = desktop.Window.FocusManager?.GetFocusedElement();
        var focusedControl = Assert.IsAssignableFrom<Control>(focus);
        Assert.Equal("打开设置", AutomationProperties.GetName(focusedControl));
        desktop.Window.MouseMove(secondBounds.Center);
        using var highlighted = desktop.Window.CaptureRenderedFrame();
        Assert.NotNull(highlighted);

        // Focus feedback must extend into both outer lobes, beyond the old fixed central button.
        Assert.True(ChangedPixels(normal, highlighted, new Rect(firstBounds.X + 8, firstBounds.Center.Y - 12, 14, 24)) > 0);
        Assert.True(ChangedPixels(normal, highlighted, new Rect(firstBounds.Right - 22, firstBounds.Center.Y - 12, 14, 24)) > 0);
        var corner = new Point(firstBounds.X + firstBounds.Width * .06, firstBounds.Y + firstBounds.Height * .06);
        Assert.Equal(Pixel(normal, corner), Pixel(highlighted, corner));
        SaveVisual(highlighted, "WONDER_POLYGON_PREVIEW_PATH", "wonder-polygon-highlight.png");
    }

    [AvaloniaFact]
    public void Divider_hover_highlights_the_full_shared_edge()
    {
        using var desktop = new TestDesktop();
        var edge = desktop.View.Snapshot.Splits.Values.First(rect => rect.Width < rect.Height);
        var nearTop = new Point(edge.X + edge.Width / 2, edge.Y + edge.Height * .05);
        var nearBottom = new Point(edge.X + edge.Width / 2, edge.Y + edge.Height * .95);
        desktop.Window.MouseMove(new Point(2, 2));
        using var normal = desktop.Window.CaptureRenderedFrame();
        Assert.NotNull(normal);

        desktop.Window.MouseMove(nearTop);
        using var hovered = desktop.Window.CaptureRenderedFrame();
        Assert.NotNull(hovered);

        Assert.NotEqual(Pixel(normal, nearTop), Pixel(hovered, nearTop));
        Assert.NotEqual(Pixel(normal, nearBottom), Pixel(hovered, nearBottom));
        SaveVisual(hovered, "WONDER_DIVIDER_PREVIEW_PATH", "wonder-divider-highlight.png");
    }

    private static int Pixel(WriteableBitmap bitmap, Point position)
    {
        using var pixels = bitmap.Lock();
        return Marshal.ReadInt32(pixels.Address, (int)position.Y * pixels.RowBytes + (int)position.X * 4);
    }

    private static int ChangedPixels(WriteableBitmap before, WriteableBitmap after, Rect region)
    {
        using var first = before.Lock();
        using var second = after.Lock();
        var changed = 0;
        for (var y = (int)region.Y; y < region.Bottom; y++)
            for (var x = (int)region.X; x < region.Right; x++)
                if (Marshal.ReadInt32(first.Address, y * first.RowBytes + x * 4) !=
                    Marshal.ReadInt32(second.Address, y * second.RowBytes + x * 4))
                    changed++;
        return changed;
    }

    private static void SaveVisual(WriteableBitmap frame, string environmentVariable, string fileName)
    {
        var configuredPath = Environment.GetEnvironmentVariable(environmentVariable);
        var path = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(AppContext.BaseDirectory, "artifacts", fileName)
            : Path.GetFullPath(configuredPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        frame.Save(path, PngBitmapEncoderOptions.Default);
    }
}
