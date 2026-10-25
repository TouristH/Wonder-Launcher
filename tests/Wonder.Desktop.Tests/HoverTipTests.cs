// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Wonder.Workspace;
using Xunit;

namespace Wonder.Desktop.Tests;

public sealed partial class WorkspaceInteractionTests
{
    [AvaloniaTheory]
    [InlineData("divider")]
    [InlineData("header")]
    [InlineData("component")]
    public async Task Hover_tip_stays_closed_during_stationary_press_and_recovers_after_release(string target)
    {
        using var desktop = new TestDesktop();
        var control = target switch
        {
            "divider" => desktop.View.Children.OfType<Control>().First(control => ToolTip.GetTip(control) is string tip && tip.StartsWith("按住整条")),
            "header" => desktop.View.GetVisualDescendants().OfType<Control>().First(control => ToolTip.GetTip(control) is string tip && tip.StartsWith("长按顶边栏")),
            _ => desktop.View.GetVisualDescendants().OfType<ComponentView>().First()
        };
        var point = control.TranslatePoint(new Point(target == "header" ? control.Bounds.Width - 12 : control.Bounds.Width / 2, control.Bounds.Height / 2), desktop.Window)!.Value;
        ToolTip.SetShowDelay(control, 20);
        ToolTip.SetBetweenShowDelay(control, -1);
        desktop.Window.MouseMove(point);
        await WaitForHoverTip(() => ToolTip.GetIsOpen(control));
        desktop.Press(point);
        Assert.False(ToolTip.GetIsOpen(control));
        await Task.Delay(550);
        Dispatcher.UIThread.RunJobs();
        Assert.False(ToolTip.GetIsOpen(control));
        var operationHint = target switch
        {
            "divider" => "调整大小 · Esc 取消",
            "header" => "拖到其他功能区交换 · Esc 取消",
            _ => "拖入功能区放置 · Esc 取消"
        };
        Assert.Contains(desktop.View.GetVisualDescendants().OfType<TextBlock>(), text => text.IsEffectivelyVisible && text.Text == operationHint);
        var previewDirectory = Environment.GetEnvironmentVariable("WONDER_HOVER_PREVIEW_DIR");
        if (previewDirectory != null && target == "divider")
        {
            Directory.CreateDirectory(previewDirectory);
            using var frame = desktop.Window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame.Save(Path.Combine(previewDirectory, "held-divider.png"), PngBitmapEncoderOptions.Default);
        }
        // A timer or a newly hovered control must not reopen a popup while pressed.
        ToolTip.SetIsOpen(control, true);
        Assert.False(ToolTip.GetIsOpen(control));
        desktop.Escape();
        ToolTip.SetIsOpen(control, true);
        Assert.False(ToolTip.GetIsOpen(control));
        desktop.Release(point);
        desktop.Window.MouseMove(new Point(-20, -20));
        desktop.Window.MouseMove(point);
        await WaitForHoverTip(() => ToolTip.GetIsOpen(control));
        Assert.True(ToolTip.GetIsOpen(control));
    }

    private static async Task WaitForHoverTip(Func<bool> predicate)
    {
        for (var i = 0; i < 100; i++)
        {
            Dispatcher.UIThread.RunJobs();
            if (predicate()) return;
            await Task.Delay(10);
        }
        Assert.True(predicate(), "Tooltip did not reach the expected visibility.");
    }

    [AvaloniaTheory]
    [InlineData(MouseButton.Left, RawInputModifiers.LeftMouseButton)]
    [InlineData(MouseButton.Right, RawInputModifiers.RightMouseButton)]
    [InlineData(MouseButton.Middle, RawInputModifiers.MiddleMouseButton)]
    public async Task Hover_tip_on_another_control_is_blocked_while_pressed_and_recovers_after_outside_release(MouseButton button, RawInputModifiers held)
    {
        var first = new Border { Width = 120, Height = 80, Background = Avalonia.Media.Brushes.Gray };
        var second = new Border { Width = 120, Height = 80, Background = Avalonia.Media.Brushes.Gray };
        ToolTip.SetTip(second, "另一个控件的悬浮提示");
        ToolTip.SetShowDelay(second, 20);
        ToolTip.SetBetweenShowDelay(second, -1);
        var stack = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 40 };
        stack.Children.Add(first);
        stack.Children.Add(second);
        var window = new Window { Width = 400, Height = 180, Content = stack };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var start = first.TranslatePoint(new Point(60, 40), window)!.Value;
            var target = second.TranslatePoint(new Point(60, 40), window)!.Value;
            window.MouseMove(start);
            window.MouseDown(start, button);
            window.MouseMove(target, held);
            await Task.Delay(100);
            Dispatcher.UIThread.RunJobs();
            Assert.False(ToolTip.GetIsOpen(second));
            ToolTip.SetIsOpen(second, true);
            Assert.False(ToolTip.GetIsOpen(second));
            // No release is sent to this window, as when released outside the application.
            window.MouseMove(new Point(-20, -20));
            window.MouseMove(target);
            await WaitForHoverTip(() => ToolTip.GetIsOpen(second));
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Hover_tip_is_suppressed_across_native_library_drag_and_restored_after_drop_or_cancel(bool cancel)
    {
        var directory = Path.Combine(Path.GetTempPath(), "Wonder.Hover.Tests", Guid.NewGuid().ToString("N"));
        var main = new MainWindow(Path.Combine(directory, "workspace.json"));
        try
        {
            main.Show();
            Dispatcher.UIThread.RunJobs();
            var workspace = Assert.Single(main.GetVisualDescendants().OfType<WorkspaceView>());
            var menuItem = Assert.Single(workspace.CreateWorkspaceMenu().Items.OfType<MenuItem>(), item => Equals(item.Header, "组件库…"));
            menuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var library = Assert.Single(main.OwnedWindows);
            var source = library.GetVisualDescendants().OfType<Control>()
                .Single(control => AutomationProperties.GetName(control) == "拖动设置");
            var target = workspace.GetVisualDescendants().OfType<ComponentView>().First();
            var start = source.TranslatePoint(new Point(source.Bounds.Width / 2, source.Bounds.Height / 2), library)!.Value;
            var end = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), main)!.Value;
            library.MouseMove(start);
            library.MouseDown(start, MouseButton.Left);
            library.MouseMove(start + new Vector(10, 0), RawInputModifiers.LeftMouseButton);
            await Task.Yield();
            main.MouseMove(end, RawInputModifiers.LeftMouseButton);
            ToolTip.SetIsOpen(source, true);
            ToolTip.SetIsOpen(target, true);
            Assert.False(ToolTip.GetIsOpen(source));
            Assert.False(ToolTip.GetIsOpen(target));
            if (cancel)
            {
                main.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
                await Task.Yield();
                ToolTip.SetIsOpen(target, true);
                Assert.False(ToolTip.GetIsOpen(target));
            }
            main.MouseUp(end, MouseButton.Left);
            await WaitForHoverTip(() => library.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text is { } value && (value.StartsWith("已创建") || value.StartsWith("已取消"))));
            Dispatcher.UIThread.RunJobs();
            main.MouseMove(new Point(-20, -20));
            main.MouseMove(end);
            // Drop rebuilds the workspace controls, so find the current shortcut view.
            target = workspace.GetVisualDescendants().OfType<ComponentView>().First();
            ToolTip.SetIsOpen(target, true);
            Assert.True(ToolTip.GetIsOpen(target));
        }
        finally
        {
            main.Close();
            Directory.Delete(directory, true);
        }
    }
}
