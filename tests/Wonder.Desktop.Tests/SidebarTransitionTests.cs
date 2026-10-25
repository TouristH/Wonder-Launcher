// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Wonder.Workspace;
using Xunit;

namespace Wonder.Desktop.Tests;

public sealed partial class WorkspaceInteractionTests
{
    [AvaloniaTheory]
    [InlineData(SidebarEdge.Left)]
    [InlineData(SidebarEdge.Right)]
    [InlineData(SidebarEdge.Top)]
    [InlineData(SidebarEdge.Bottom)]
    public async Task Animated_sidebar_reflows_neighbors_through_visible_intermediate_bounds(SidebarEdge edge)
    {
        var (document, area) = SidebarDocument(edge, collapsed: true);
        using var desktop = new TestDesktop(document: document, animateSidebars: true);
        desktop.View.ApplySettings(new LauncherSettings { SidebarCloseDelayMilliseconds = 100 });
        var sibling = document.Areas.Single(item => item != area);
        var rail = desktop.View.GetAreaBounds(area.Id);
        var siblingBefore = desktop.View.GetAreaBounds(sibling.Id);
        var ratio = document.Root!.Ratio;
        if (edge == SidebarEdge.Left) SaveMotionFrame(desktop.Window, "sidebar-closed");
        desktop.Window.MouseMove(Center(rail));
        Assert.Equal(rail, desktop.View.GetAreaBounds(area.Id));
        await WaitForSidebar(() => Extent(desktop.View.GetAreaBounds(area.Id), edge) > 28);
        var middle = desktop.View.GetAreaBounds(area.Id);
        Assert.InRange(Extent(middle, edge), 29, 319.99);
        var visible = desktop.View.Children.OfType<AreaView>().Single(view => view.Area == area);
        // Avalonia rounds arranged bounds to physical pixels; the frame is shared within that tolerance.
        Assert.InRange(Math.Abs(middle.X - visible.Bounds.X), 0, 1);
        Assert.InRange(Math.Abs(middle.Y - visible.Bounds.Y), 0, 1);
        Assert.InRange(Math.Abs(middle.Width - visible.Bounds.Width), 0, 1);
        Assert.InRange(Math.Abs(middle.Height - visible.Bounds.Height), 0, 1);
        Assert.True(Extent(desktop.View.GetAreaBounds(sibling.Id), edge) < Extent(siblingBefore, edge));
        if (edge == SidebarEdge.Left) SaveMotionFrame(desktop.Window, "sidebar-opening");
        // Wait for the completed opening frame, not its almost-320 easing tail.
        // Otherwise the closing predicate can mistake that tail for a closing frame.
        await WaitForSidebar(() => Extent(desktop.View.GetAreaBounds(area.Id), edge) == 320);
        if (edge == SidebarEdge.Left) SaveMotionFrame(desktop.Window, "sidebar-open");

        desktop.Window.MouseMove(Center(desktop.View.GetAreaBounds(sibling.Id)));
        await WaitForSidebar(() => Extent(desktop.View.GetAreaBounds(area.Id), edge) < 319.99);
        Assert.True(visible.IsVisible);
        if (edge == SidebarEdge.Left) SaveMotionFrame(desktop.Window, "sidebar-closing");
        Assert.InRange(Extent(desktop.View.GetAreaBounds(area.Id), edge), 28.01, 319.99);
        await WaitForSidebar(() => desktop.View.GetAreaBounds(area.Id) == rail);
        Assert.False(visible.IsVisible);
        Assert.Equal(siblingBefore, desktop.View.GetAreaBounds(sibling.Id));
        Assert.Equal(ratio, document.Root.Ratio);
        Assert.Equal(320, area.SidebarExtent);
        Assert.Equal(0, desktop.ChangeCount);
    }

    [AvaloniaFact]
    public async Task Animated_sidebar_can_reverse_a_close_without_jumping_or_persisting_it()
    {
        var (document, area) = SidebarDocument(SidebarEdge.Left, collapsed: true);
        using var desktop = new TestDesktop(document: document, animateSidebars: true);
        desktop.View.ApplySettings(new LauncherSettings { SidebarCloseDelayMilliseconds = 100 });
        desktop.Window.MouseMove(Center(desktop.View.GetAreaBounds(area.Id)));
        await WaitForSidebar(() => desktop.View.GetAreaBounds(area.Id).Width == 320);
        desktop.Window.MouseMove(new Point(1000, 700));
        await WaitForSidebar(() => desktop.View.GetAreaBounds(area.Id).Width < 320);
        var reversing = desktop.View.GetAreaBounds(area.Id);
        desktop.Window.MouseMove(Center(reversing));
        Assert.Equal(reversing, desktop.View.GetAreaBounds(area.Id));
        await WaitForSidebar(() => desktop.View.GetAreaBounds(area.Id).Width == 320);
        Assert.Equal(0, desktop.ChangeCount);
        Assert.Equal(SidebarEdge.Left, area.SidebarEdge);
    }

    [AvaloniaFact]
    public async Task Animated_sidebar_resize_then_escape_restores_hover_extent_and_keeps_document()
    {
        var (document, area) = SidebarDocument(SidebarEdge.Left, collapsed: true);
        using var desktop = new TestDesktop(document: document, animateSidebars: true);
        var ratio = document.Root!.Ratio;
        desktop.Window.MouseMove(Center(desktop.View.GetAreaBounds(area.Id)));
        await WaitForSidebar(() => desktop.View.GetAreaBounds(area.Id).Width > 80);
        var visibleWidth = desktop.View.GetAreaBounds(area.Id).Width;
        var point = SidebarResizePoint(desktop);
        desktop.Press(point);
        Assert.InRange(Math.Abs(desktop.View.GetAreaBounds(area.Id).Width - visibleWidth), 0, .001);
        desktop.Move(point + new Vector(30, 0));
        desktop.Escape();
        desktop.Release(point);
        await WaitForSidebar(() => desktop.View.GetAreaBounds(area.Id).Width == 320);
        Assert.Equal(ratio, document.Root.Ratio);
        Assert.Equal(320, area.SidebarExtent);
        Assert.Equal(SidebarEdge.Left, area.SidebarEdge);
        Assert.Equal(0, desktop.ChangeCount);
    }

    [AvaloniaFact]
    public async Task Animated_sidebar_document_replacement_and_detach_cancel_stale_frames()
    {
        var (document, area) = SidebarDocument(SidebarEdge.Left, collapsed: true);
        using var desktop = new TestDesktop(document: document, animateSidebars: true);
        desktop.Window.MouseMove(Center(desktop.View.GetAreaBounds(area.Id)));
        await WaitForSidebar(() => desktop.View.GetAreaBounds(area.Id).Width > 28);
        desktop.View.ReplaceDocument(WorkspaceDocument.CreateEmpty());
        desktop.Window.Content = null;
        await Task.Delay(250);
        desktop.Window.Content = desktop.View;
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(desktop.View.Snapshot.Areas);
        Assert.Empty(desktop.View.Children.OfType<AreaView>());
        Assert.Equal(1, desktop.ChangeCount);
    }

    private static double Extent(LayoutRect bounds, SidebarEdge edge) => IsHorizontalSidebar(edge) ? bounds.Width : bounds.Height;

    private static void SaveMotionFrame(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("WONDER_MOTION_PREVIEW_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var bitmap = window.CaptureRenderedFrame();
        Assert.NotNull(bitmap);
        bitmap.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
    }

    private static async Task WaitForSidebar(Func<bool> predicate)
    {
        for (var i = 0; i < 150; i++)
        {
            Dispatcher.UIThread.RunJobs();
            if (predicate()) return;
            await Task.Delay(10);
        }
        Assert.True(predicate(), "Sidebar animation did not reach the expected frame within 1.5 seconds.");
    }

    [AvaloniaTheory]
    [InlineData(SidebarEdge.Left)]
    [InlineData(SidebarEdge.Right)]
    [InlineData(SidebarEdge.Top)]
    [InlineData(SidebarEdge.Bottom)]
    public void Sidebar_resize_hint_matches_noop_shrink_and_real_restore(SidebarEdge edge)
    {
        var (document, area) = SidebarDocument(edge, collapsed: true);
        using var desktop = new TestDesktop(document: document);
        desktop.Window.MouseMove(Center(desktop.View.Snapshot.Areas[area.Id]));
        var before = desktop.View.GetAreaBounds(area.Id);
        var start = SidebarResizePoint(desktop);
        var direction = ExpansionDirection(edge);
        desktop.Press(start);
        desktop.Move(start - direction * 40);
        Assert.Contains(desktop.View.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "松开保持侧边栏 · Esc 取消");
        desktop.Release(start - direction * 40);
        Assert.Equal(before, desktop.View.GetAreaBounds(area.Id));
        Assert.Equal(320, area.SidebarExtent);
        Assert.Equal(0, desktop.ChangeCount);

        desktop.Press(start);
        desktop.Move(start + direction * 60);
        Assert.Contains(desktop.View.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "松开恢复为普通功能区 · Esc 取消");
        desktop.Release(start + direction * 60);
        Assert.Equal(SidebarEdge.None, area.SidebarEdge);
        Assert.Equal(1, desktop.ChangeCount);
    }

    [AvaloniaTheory]
    [InlineData(SidebarEdge.Left)]
    [InlineData(SidebarEdge.Right)]
    [InlineData(SidebarEdge.Top)]
    [InlineData(SidebarEdge.Bottom)]
    public void Sidebar_resize_hint_does_not_promise_restore_when_available_space_clamps_growth(SidebarEdge edge)
    {
        var (document, area) = SidebarDocument(edge, collapsed: true);
        using var desktop = new TestDesktop(document: document);
        desktop.Window.Width = 400;
        desktop.Window.Height = 300;
        Dispatcher.UIThread.RunJobs();
        desktop.Window.MouseMove(Center(desktop.View.Snapshot.Areas[area.Id]));
        var start = SidebarResizePoint(desktop);
        var end = start + ExpansionDirection(edge) * 60;
        desktop.Press(start);
        desktop.Move(end);
        Assert.Contains(desktop.View.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "松开保持侧边栏 · Esc 取消");
        desktop.Release(end);
        Assert.Equal(edge, area.SidebarEdge);
        Assert.Equal(0, desktop.ChangeCount);
    }

    private static Point SidebarResizePoint(TestDesktop desktop)
    {
        var resize = desktop.View.GetVisualDescendants().OfType<Control>().Single(control => AutomationProperties.GetName(control) == "调整侧边栏大小");
        return resize.TranslatePoint(new Point(resize.Bounds.Width / 2, resize.Bounds.Height / 2), desktop.View)!.Value;
    }

    private static Vector ExpansionDirection(SidebarEdge edge) => edge switch
    {
        SidebarEdge.Left => new Vector(1, 0), SidebarEdge.Right => new Vector(-1, 0),
        SidebarEdge.Top => new Vector(0, 1), _ => new Vector(0, -1)
    };
}
