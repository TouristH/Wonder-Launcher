// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Xunit;

namespace Wonder.Desktop.Tests;

public sealed class ComponentRippleTests
{
    [AvaloniaFact]
    public async Task Click_ripple_starts_at_release_position_expands_fades_and_stays_inside_polygon()
    {
        using var host = new RippleHost();
        var origin = new Point(64, 100);
        var distant = new Point(132, 100);
        var corner = new Point(20, 20);
        host.Window.MouseMove(origin);
        using var resting = host.Frame();

        host.Window.MouseDown(origin - new Vector(2, 0), MouseButton.Left);
        host.Window.MouseUp(origin, MouseButton.Left);
        Assert.Equal(1, host.Clicks); // The action runs immediately, without awaiting the effect.
        using var first = host.Frame();
        SaveMotionFrame(first, "ripple-origin");
        Assert.NotEqual(Pixel(resting, origin), Pixel(first, origin));
        Assert.Equal(Pixel(resting, distant), Pixel(first, distant));
        Assert.Equal(Pixel(resting, corner), Pixel(first, corner));

        await Task.Delay(120);
        using var expanded = host.Frame();
        SaveMotionFrame(expanded, "ripple-expanded");
        Assert.NotEqual(Pixel(resting, distant), Pixel(expanded, distant));
        Assert.Equal(Pixel(resting, corner), Pixel(expanded, corner));

        await Task.Delay(120);
        using var fading = host.Frame();
        Assert.True(ColorDistance(Pixel(resting, origin), Pixel(fading, origin)) <
                    ColorDistance(Pixel(resting, origin), Pixel(first, origin)));
        await Task.Delay(180);
        using var complete = host.Frame();
        SaveMotionFrame(complete, "ripple-finished");
        Assert.Equal(Pixel(resting, origin), Pixel(complete, origin));
        Assert.Equal(Pixel(resting, distant), Pixel(complete, distant));
    }

    [AvaloniaFact]
    public async Task Holding_pointer_does_not_animate_until_button_clicks()
    {
        using var host = new RippleHost();
        var origin = new Point(64, 100);
        host.Window.MouseDown(origin, MouseButton.Left);
        using var pressed = host.Frame();
        await Task.Delay(100);
        using var held = host.Frame();
        Assert.Equal(Pixel(pressed, origin), Pixel(held, origin));
        Assert.Equal(Pixel(pressed, new Point(112, 100)), Pixel(held, new Point(112, 100)));
        Assert.Equal(0, host.Clicks);

        host.Window.MouseUp(origin, MouseButton.Left);
        using var clicked = host.Frame();
        await Task.Delay(420);
        using var complete = host.Frame();
        Assert.NotEqual(Pixel(clicked, origin), Pixel(complete, origin));
    }

    [AvaloniaTheory]
    [InlineData(PhysicalKey.Enter)]
    [InlineData(PhysicalKey.Space)]
    public async Task Keyboard_click_ripple_uses_center_after_a_pointer_click(PhysicalKey key)
    {
        using var host = new RippleHost();
        var pointerOrigin = new Point(40, 100);
        host.Click(pointerOrigin);
        await Task.Delay(420);
        host.Window.MouseMove(new Point(1, 1));
        using var resting = host.Frame();

        host.Window.KeyPressQwerty(key, RawInputModifiers.None);
        host.Window.KeyReleaseQwerty(key, RawInputModifiers.None);

        using var clicked = host.Frame();
        Assert.Equal(2, host.Clicks);
        Assert.NotEqual(Pixel(resting, new Point(100, 100)), Pixel(clicked, new Point(100, 100)));
        Assert.Equal(Pixel(resting, pointerOrigin), Pixel(clicked, pointerOrigin));
    }

    [AvaloniaFact]
    public async Task Repeated_click_restarts_ripple_at_new_origin_and_detach_clears_it()
    {
        using var host = new RippleHost();
        var firstOrigin = new Point(48, 100);
        var nextOrigin = new Point(152, 100);
        host.Window.MouseMove(nextOrigin);
        using var resting = host.Frame();
        host.Click(firstOrigin);
        await Task.Delay(90);
        host.Click(nextOrigin);
        using var restarted = host.Frame();
        Assert.Equal(2, host.Clicks);
        Assert.NotEqual(Pixel(resting, nextOrigin), Pixel(restarted, nextOrigin));
        Assert.Equal(Pixel(resting, firstOrigin), Pixel(restarted, firstOrigin));

        host.Window.Content = null;
        host.Window.Content = host.Button;
        Dispatcher.UIThread.RunJobs();
        host.Button.Focus();
        host.Window.MouseMove(nextOrigin);
        using var reattached = host.Frame();
        Assert.Equal(Pixel(resting, nextOrigin), Pixel(reattached, nextOrigin));
        await Task.Delay(80);
        using var later = host.Frame();
        Assert.Equal(Pixel(reattached, nextOrigin), Pixel(later, nextOrigin));
    }

    private static int Pixel(WriteableBitmap bitmap, Point point)
    {
        using var pixels = bitmap.Lock();
        return Marshal.ReadInt32(pixels.Address, (int)point.Y * pixels.RowBytes + (int)point.X * 4);
    }

    private static void SaveMotionFrame(WriteableBitmap bitmap, string name)
    {
        var directory = Environment.GetEnvironmentVariable("WONDER_MOTION_PREVIEW_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        bitmap.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
    }

    private static int ColorDistance(int first, int second) =>
        Math.Abs((first & 255) - (second & 255)) +
        Math.Abs(((first >> 8) & 255) - ((second >> 8) & 255)) +
        Math.Abs(((first >> 16) & 255) - ((second >> 16) & 255));

    private sealed class RippleHost : IDisposable
    {
        public PolygonButton Button { get; } = new(
            new Points { new(100, 4), new(196, 100), new(100, 196), new(4, 100) },
            new Size(200, 200), new Border());
        public Window Window { get; }
        public int Clicks { get; private set; }

        public RippleHost()
        {
            Window = new Window
            {
                Width = 200, Height = 200, WindowDecorations = WindowDecorations.None,
                Background = Brushes.Black, Content = Button
            };
            Button.Click += (_, _) => Clicks++;
            Window.Show();
            Button.Focus();
            Dispatcher.UIThread.RunJobs();
        }

        public void Click(Point point)
        {
            Window.MouseDown(point, MouseButton.Left);
            Window.MouseUp(point, MouseButton.Left);
        }

        public WriteableBitmap Frame()
        {
            var frame = Window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            return frame;
        }

        public void Dispose() => Window.Close();
    }
}

public sealed partial class WorkspaceInteractionTests
{
    [AvaloniaTheory]
    [InlineData("move")]
    [InlineData("hold")]
    [InlineData("escape")]
    public async Task Cancelled_component_gesture_does_not_leave_a_click_ripple(string gesture)
    {
        using var desktop = new TestDesktop();
        var bounds = desktop.View.GetComponentBounds(desktop.Component(0).Id);
        var origin = new Point(bounds.X + 30, bounds.Center.Y);
        desktop.Press(origin);
        if (gesture == "move") desktop.Move(origin + new Vector(18, 0));
        else if (gesture == "hold") await Task.Delay(550);
        else desktop.Escape();
        desktop.Release(origin);
        using var released = desktop.Window.CaptureRenderedFrame();
        Assert.NotNull(released);
        Assert.Equal(0, desktop.ActionCount);

        await Task.Delay(420);
        using var resting = desktop.Window.CaptureRenderedFrame();
        Assert.NotNull(resting);
        Assert.Equal(Pixel(resting, origin), Pixel(released, origin));
    }
}
