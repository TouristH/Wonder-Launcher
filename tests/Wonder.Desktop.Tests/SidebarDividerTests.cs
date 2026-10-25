// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Wonder.Workspace;
using Xunit;

namespace Wonder.Desktop.Tests;

public sealed partial class WorkspaceInteractionTests
{
    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(1.75)]
    [InlineData(2)]
    public void Sidebar_divider_matches_rendered_edges_at_different_display_scales(double scale)
    {
        foreach (var edge in new[] { SidebarEdge.Left, SidebarEdge.Right, SidebarEdge.Top, SidebarEdge.Bottom })
        {
            var (document, area) = SidebarDocument(edge, collapsed: true);
            using var desktop = new TestDesktop(document: document);
            desktop.Window.SetRenderScaling(scale);
            desktop.Window.Width = 1181;
            desktop.Window.Height = 779;
            foreach (var extent in new[] { 200, 201, 293 })
            {
                desktop.View.ApplySettings(new LauncherSettings { SidebarExtent = extent });
                Dispatcher.UIThread.RunJobs();
                desktop.Window.MouseMove(Center(desktop.View.GetAreaBounds(area.Id)));
                Dispatcher.UIThread.RunJobs();
                AssertDividerCenteredOnRenderedGap(desktop, area, edge);
                if (scale == 1.5 && extent == 200)
                    SaveMotionFrame(desktop.Window, $"sidebar-divider-{edge.ToString().ToLowerInvariant()}-150-percent");
                desktop.Window.Width += 1;
                desktop.Window.Height += 1;
                Dispatcher.UIThread.RunJobs();
                AssertDividerCenteredOnRenderedGap(desktop, area, edge);
            }
        }
    }

    [AvaloniaTheory]
    [InlineData(SidebarEdge.Left)]
    [InlineData(SidebarEdge.Right)]
    [InlineData(SidebarEdge.Top)]
    [InlineData(SidebarEdge.Bottom)]
    public async Task Sidebar_divider_is_centered_in_gap_stays_open_on_hover_and_restores_at_same_line(SidebarEdge edge)
    {
        var (document, area) = SidebarDocument(edge);
        using var desktop = new TestDesktop(document: document);
        desktop.View.ApplySettings(new LauncherSettings { SidebarCloseDelayMilliseconds = 100 });
        var (start, target) = CollapsePoints(desktop, edge);
        desktop.Press(start);
        desktop.Move(target);
        desktop.Release(target);
        desktop.Window.MouseMove(Center(desktop.View.GetAreaBounds(area.Id)));
        Dispatcher.UIThread.RunJobs();
        var sibling = document.Areas.Single(item => item != area);
        var gap = SidebarGap(desktop, area, sibling);
        var resize = SidebarDivider(desktop);
        AssertDividerAligned(desktop, resize, gap, edge);

        desktop.Window.MouseMove(gap.Center);
        await Task.Delay(240);
        Dispatcher.UIThread.RunJobs();
        Assert.True(resize.IsVisible);
        Assert.Equal(320, Extent(desktop.View.GetAreaBounds(area.Id), edge));
        SaveMotionFrame(desktop.Window, $"sidebar-divider-{edge.ToString().ToLowerInvariant()}");

        // The visible center must also be the actual handle; restoring must not shift the line.
        desktop.Press(gap.Center);
        var end = gap.Center + ExpansionDirection(edge) * 50;
        desktop.Move(end);
        var beforeRestore = SidebarGap(desktop, area, sibling).Center;
        desktop.Release(end);
        Assert.Equal(SidebarEdge.None, area.SidebarEdge);
        Assert.Equal(beforeRestore, Center(Assert.Single(desktop.View.Snapshot.Splits).Value));
        Assert.Equal(2, desktop.ChangeCount);
    }

    [AvaloniaTheory]
    [InlineData(SidebarEdge.Left)]
    [InlineData(SidebarEdge.Right)]
    [InlineData(SidebarEdge.Top)]
    [InlineData(SidebarEdge.Bottom)]
    public async Task Sidebar_divider_follows_the_current_gap_during_open_animation(SidebarEdge edge)
    {
        var (document, area) = SidebarDocument(edge, collapsed: true);
        using var desktop = new TestDesktop(document: document, animateSidebars: true);
        var sibling = document.Areas.Single(item => item != area);
        desktop.Window.MouseMove(Center(desktop.View.GetAreaBounds(area.Id)));
        await WaitForSidebar(() => Extent(desktop.View.GetAreaBounds(area.Id), edge) > 40);
        AssertDividerAligned(desktop, SidebarDivider(desktop), SidebarGap(desktop, area, sibling), edge);
        AssertDividerCenteredOnRenderedGap(desktop, area, edge);
        await WaitForSidebar(() => Extent(desktop.View.GetAreaBounds(area.Id), edge) == 320);
        AssertDividerAligned(desktop, SidebarDivider(desktop), SidebarGap(desktop, area, sibling), edge);
        AssertDividerCenteredOnRenderedGap(desktop, area, edge);
        Assert.Equal(0, desktop.ChangeCount);
    }

    [AvaloniaTheory]
    [InlineData(SidebarEdge.Left)]
    [InlineData(SidebarEdge.Right)]
    [InlineData(SidebarEdge.Top)]
    [InlineData(SidebarEdge.Bottom)]
    public void Sidebar_divider_aligns_with_both_nested_neighbors_after_window_resize(SidebarEdge edge)
    {
        var (document, area) = SidebarDocument(edge, collapsed: true);
        var sibling = document.Areas.Single(item => item != area);
        var next = document.AddArea(sibling.Id, IsHorizontalSidebar(edge) ? SplitAxis.Vertical : SplitAxis.Horizontal);
        using var desktop = new TestDesktop(document: document);
        desktop.Window.MouseMove(Center(desktop.View.GetAreaBounds(area.Id)));
        Dispatcher.UIThread.RunJobs();
        AssertDividerAligned(desktop, SidebarDivider(desktop), SidebarGap(desktop, area, sibling), edge);
        AssertDividerAligned(desktop, SidebarDivider(desktop), SidebarGap(desktop, area, next), edge);

        desktop.Window.Width = 530;
        desktop.Window.Height = 410;
        Dispatcher.UIThread.RunJobs();
        AssertDividerAligned(desktop, SidebarDivider(desktop), SidebarGap(desktop, area, sibling), edge);
        AssertDividerAligned(desktop, SidebarDivider(desktop), SidebarGap(desktop, area, next), edge);
        Assert.Equal(0, desktop.ChangeCount);
    }

    [AvaloniaTheory]
    [InlineData(SidebarEdge.Left, SidebarEdge.Right)]
    [InlineData(SidebarEdge.Right, SidebarEdge.Left)]
    [InlineData(SidebarEdge.Top, SidebarEdge.Bottom)]
    [InlineData(SidebarEdge.Bottom, SidebarEdge.Top)]
    public void Sidebar_divider_stays_next_to_expanded_panel_when_opposite_side_is_a_rail(SidebarEdge edge, SidebarEdge opposite)
    {
        var (document, area) = SidebarDocument(edge, collapsed: true);
        document.Areas.Single(item => item != area).SidebarEdge = opposite;
        using var desktop = new TestDesktop(document: document);
        desktop.Window.MouseMove(Center(desktop.View.GetAreaBounds(area.Id)));
        Dispatcher.UIThread.RunJobs();
        var bounds = desktop.View.GetAreaBounds(area.Id);
        var gap = edge switch
        {
            SidebarEdge.Left => new Rect(bounds.X + bounds.Width, bounds.Y, 10, bounds.Height),
            SidebarEdge.Right => new Rect(bounds.X - 10, bounds.Y, 10, bounds.Height),
            SidebarEdge.Top => new Rect(bounds.X, bounds.Y + bounds.Height, bounds.Width, 10),
            _ => new Rect(bounds.X, bounds.Y - 10, bounds.Width, 10)
        };
        AssertDividerAligned(desktop, SidebarDivider(desktop), gap, edge);
        desktop.Press(gap.Center);
        var target = gap.Center + ExpansionDirection(edge) * 50;
        desktop.Move(target);
        desktop.Release(target);
        Assert.Equal(SidebarEdge.None, area.SidebarEdge);
        Assert.Equal(1, desktop.ChangeCount);
    }

    private static Border SidebarDivider(TestDesktop desktop) => Assert.Single(desktop.View.Children.OfType<Border>(),
        control => AutomationProperties.GetName(control) == "调整侧边栏大小");

    private static void AssertDividerCenteredOnRenderedGap(TestDesktop desktop, Area area, SidebarEdge edge)
    {
        var panel = desktop.View.Children.OfType<AreaView>().Single(view => view.Area == area).Bounds;
        var neighbor = desktop.View.Children.OfType<AreaView>().Single(view => view.Area != area).Bounds;
        var expected = edge switch
        {
            SidebarEdge.Left => (panel.Right + neighbor.Left) / 2,
            SidebarEdge.Right => (neighbor.Right + panel.Left) / 2,
            SidebarEdge.Top => (panel.Bottom + neighbor.Top) / 2,
            _ => (neighbor.Bottom + panel.Top) / 2
        };
        var resize = SidebarDivider(desktop);
        var line = Assert.IsType<Border>(resize.Child);
        var center = line.TranslatePoint(new Point(line.Bounds.Width / 2, line.Bounds.Height / 2), desktop.View)!.Value;
        var actual = IsHorizontalSidebar(edge) ? center.X : center.Y;
        var scale = desktop.Window.RenderScaling;
        Assert.True(Math.Abs(actual - expected) * scale <= .0001,
            $"{edge} at {scale}: visible line {actual}, gap center {expected}, error {Math.Abs(actual - expected) * scale} physical pixels.");
        Assert.InRange(Math.Abs((IsHorizontalSidebar(edge) ? resize.Bounds.Center.X : resize.Bounds.Center.Y) - expected), 0, .0001);
    }

    private static Rect SidebarGap(TestDesktop desktop, Area area, Area sibling)
    {
        var bounds = desktop.View.GetAreaBounds(area.Id);
        var other = desktop.View.GetAreaBounds(sibling.Id);
        return area.SidebarEdge switch
        {
            SidebarEdge.Left => new Rect(bounds.X + bounds.Width, bounds.Y, other.X - bounds.X - bounds.Width, bounds.Height),
            SidebarEdge.Right => new Rect(other.X + other.Width, bounds.Y, bounds.X - other.X - other.Width, bounds.Height),
            SidebarEdge.Top => new Rect(bounds.X, bounds.Y + bounds.Height, bounds.Width, other.Y - bounds.Y - bounds.Height),
            _ => new Rect(bounds.X, other.Y + other.Height, bounds.Width, bounds.Y - other.Y - other.Height)
        };
    }

    private static void AssertDividerAligned(TestDesktop desktop, Border resize, Rect gap, SidebarEdge edge)
    {
        var line = Assert.IsType<Border>(resize.Child);
        var center = line.TranslatePoint(new Point(line.Bounds.Width / 2, line.Bounds.Height / 2), desktop.View)!.Value;
        var actual = IsHorizontalSidebar(edge) ? center.X : center.Y;
        var expected = IsHorizontalSidebar(edge) ? gap.Center.X : gap.Center.Y;
        Assert.InRange(Math.Abs(actual - expected), 0, 1); // physical-pixel layout rounding
        var hitCenter = resize.Bounds.Center;
        Assert.InRange(Math.Abs((IsHorizontalSidebar(edge) ? hitCenter.X : hitCenter.Y) - expected), 0, 1);
    }
}
