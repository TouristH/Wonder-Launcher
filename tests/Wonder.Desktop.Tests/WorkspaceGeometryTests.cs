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
    [InlineData(0, .1, .5)]
    [InlineData(1, .9, .5)]
    public void Polygon_outer_lobe_accepts_short_click(int slot, double xFraction, double yFraction)
    {
        using var desktop = new TestDesktop();
        var component = desktop.Component(slot);
        var bounds = desktop.View.GetComponentBounds(component.Id);
        var originalActions = desktop.ActionCount;

        desktop.Click(new Point(bounds.X + bounds.Width * xFraction, bounds.Y + bounds.Height * yFraction));

        Assert.Equal(originalActions + 1, desktop.ActionCount);
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    public void Polygon_transparent_bounding_box_corner_does_not_click(int slot)
    {
        using var desktop = new TestDesktop();
        var component = desktop.Component(slot);
        var bounds = desktop.View.GetComponentBounds(component.Id);
        var originalActions = desktop.ActionCount;

        desktop.Click(new Point(bounds.X + bounds.Width * .06, bounds.Y + bounds.Height * .06));

        Assert.Equal(originalActions, desktop.ActionCount);
        Assert.Equal(0, desktop.ChangeCount);
    }

    [AvaloniaTheory]
    [InlineData(true, .02)]
    [InlineData(true, .98)]
    [InlineData(false, .02)]
    [InlineData(false, .98)]
    public void Divider_can_be_dragged_immediately_near_either_end(bool verticalEdge, double fraction)
    {
        using var desktop = new TestDesktop();
        var divider = desktop.View.Snapshot.Splits.First(pair => (pair.Value.Width < pair.Value.Height) == verticalEdge);
        var start = verticalEdge
            ? new Point(divider.Value.X + divider.Value.Width / 2, divider.Value.Y + divider.Value.Height * fraction)
            : new Point(divider.Value.X + divider.Value.Width * fraction, divider.Value.Y + divider.Value.Height / 2);
        var end = start + (verticalEdge ? new Vector(60, 0) : new Vector(0, 60));

        desktop.Press(start);
        desktop.Move(end);
        desktop.Release(end);

        var after = desktop.View.Snapshot.Splits[divider.Key];
        Assert.True(verticalEdge ? after.X > divider.Value.X + 40 : after.Y > divider.Value.Y + 40);
        Assert.Equal(1, desktop.ChangeCount);
    }

    [AvaloniaTheory]
    [InlineData(45, -30)]
    [InlineData(-55, 38)]
    public void Window_resize_keeps_signed_component_offset_from_full_area_center(double x, double y)
    {
        var document = DocumentWithOffset(ComponentIds.Settings, x, y);
        using var desktop = new TestDesktop(document: document);
        var component = desktop.Component(0);
        AssertCenteredOffset(desktop, component, x, y);

        desktop.Window.Width = 820;
        desktop.Window.Height = 600;
        Dispatcher.UIThread.RunJobs();
        AssertCenteredOffset(desktop, component, x, y);
        desktop.Window.Width = 1280;
        desktop.Window.Height = 860;
        Dispatcher.UIThread.RunJobs();
        AssertCenteredOffset(desktop, component, x, y);
        Assert.Equal((x, y), (component.X, component.Y));
    }

    [AvaloniaTheory]
    [InlineData(45, -30)]
    [InlineData(-55, 38)]
    public void Divider_resize_keeps_component_offset_from_its_area_center(double x, double y)
    {
        using var desktop = new TestDesktop(document: DocumentWithOffset(ComponentIds.Settings, x, y));
        var component = desktop.Component(0);
        var divider = desktop.View.Snapshot.Splits.First(pair => pair.Value.Width < pair.Value.Height);
        var start = Center(divider.Value);
        var end = start + new Vector(90, 0);

        desktop.Press(start);
        desktop.Move(end);
        desktop.Release(end);

        Assert.NotEqual(divider.Value, desktop.View.Snapshot.Splits[divider.Key]);
        AssertCenteredOffset(desktop, component, x, y);
        Assert.Equal((x, y), (component.X, component.Y));
    }

    [AvaloniaFact]
    public void Extreme_shrink_clips_without_scaling_or_rewriting_center_offset_then_recovers()
    {
        using var desktop = new TestDesktop(document: DocumentWithOffset(ComponentIds.Settings, 170, -110));
        var component = desktop.Component(0);
        var originalBounds = desktop.View.GetComponentBounds(component.Id);
        desktop.Window.Width = 200;
        desktop.Window.Height = 150;
        Dispatcher.UIThread.RunJobs();

        AssertCenteredOffset(desktop, component, 170, -110);
        Assert.Equal(originalBounds.Size, desktop.View.GetComponentBounds(component.Id).Size);
        Assert.Equal((170d, -110d), (component.X, component.Y));
        desktop.Window.Width = 1100;
        desktop.Window.Height = 740;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(originalBounds, desktop.View.GetComponentBounds(component.Id));
    }

    [AvaloniaFact]
    public async Task Swapping_regions_preserves_signed_offsets_and_follows_the_new_region_center()
    {
        using var desktop = new TestDesktop(document: DocumentWithOffset(ComponentIds.Settings, -45, 28));
        var component = desktop.Component(0);
        var areas = desktop.View.Document.Areas.Take(2).ToArray();
        var start = HeaderPoint(desktop.View.Snapshot.Areas[areas[0].Id]);
        var target = HeaderPoint(desktop.View.Snapshot.Areas[areas[1].Id]);

        await desktop.Hold(start);
        desktop.Move(target);
        desktop.Release(target);

        Assert.Equal((-45d, 28d), (component.X, component.Y));
        AssertCenteredOffset(desktop, component, -45, 28);
    }

    [AvaloniaFact]
    public async Task Component_drag_stores_a_signed_center_offset()
    {
        using var desktop = new TestDesktop(document: DocumentWithOffset(ComponentIds.Settings, -40, 22));
        var component = desktop.Component(0);
        var start = desktop.View.GetComponentBounds(component.Id).Center;
        var end = start + new Vector(35, -15);

        await desktop.Hold(start);
        desktop.Move(end);
        desktop.Release(end);

        Assert.Equal((-5d, 7d), (component.X, component.Y));
        AssertCenteredOffset(desktop, component, -5, 7);
    }

    [AvaloniaFact]
    public void Version_one_layout_migrates_once_without_moving_visible_component()
    {
        var document = TestWorkspaceFactory.CreateThreeAreas();
        document.Version = 1;
        var component = document.Areas.First().Components.Single();
        component.X = 32;
        component.Y = 32;
        using var desktop = new TestDesktop(document: document);

        // v1: 1 px area border + 14 px side inset / 46 px header + 32 px stored position.
        var original = desktop.View.GetComponentBounds(component.Id);
        Assert.Equal(new Point(47, 79), original.TopLeft);
        Assert.Equal(2, document.Version);
        var migratedOffset = (component.X, component.Y);
        desktop.Window.Width = 900;
        desktop.Window.Height = 620;
        Dispatcher.UIThread.RunJobs();

        AssertCenteredOffset(desktop, component, migratedOffset.X, migratedOffset.Y);
        Assert.Equal(migratedOffset, (component.X, component.Y));
    }

    [AvaloniaFact]
    public void Divider_drag_commits_when_pointer_releases_outside_workspace()
    {
        using var desktop = new TestDesktop();
        var divider = desktop.View.Snapshot.Splits.First(pair => pair.Value.Width < pair.Value.Height);
        var start = Center(divider.Value);
        var outside = new Point(1250, start.Y);

        desktop.Press(start);
        desktop.Move(outside);
        desktop.Release(outside);

        Assert.True(desktop.View.Snapshot.Splits[divider.Key].X > divider.Value.X);
        Assert.Equal(1, desktop.ChangeCount);
    }

    [AvaloniaFact]
    public void Divider_drag_includes_shared_region_border()
    {
        using var desktop = new TestDesktop();
        var divider = desktop.View.Snapshot.Splits.First(pair => pair.Value.Width < pair.Value.Height);
        var start = new Point(divider.Value.X - 2, divider.Value.Y + divider.Value.Height / 2);
        var end = start + new Vector(60, 0);

        desktop.Press(start);
        desktop.Move(end);
        desktop.Release(end);

        Assert.True(desktop.View.Snapshot.Splits[divider.Key].X > divider.Value.X + 40);
    }

    [AvaloniaFact]
    public void Losing_pointer_capture_rolls_back_immediate_divider_drag()
    {
        using var desktop = new TestDesktop();
        var divider = desktop.View.Snapshot.Splits.First();
        var start = Center(divider.Value);
        var end = start + new Vector(60, 0);
        desktop.Press(start);
        desktop.Move(end);
        Assert.NotEqual(divider.Value, desktop.View.Snapshot.Splits[divider.Key]);

        Assert.NotNull(desktop.LastPointer);
        desktop.LastPointer.Capture(desktop.Window);
        desktop.Release(end);

        Assert.Equal(divider.Value, desktop.View.Snapshot.Splits[divider.Key]);
        Assert.Equal(0, desktop.ChangeCount);
    }

    private static WorkspaceDocument DocumentWithOffset(string kind, double x, double y)
    {
        var document = TestWorkspaceFactory.CreateThreeAreas();
        document.Version = 2;
        var component = document.Areas.SelectMany(area => area.Components).First(item => item.DefinitionId == kind);
        component.X = x;
        component.Y = y;
        return document;
    }

    private static void AssertCenteredOffset(TestDesktop desktop, ComponentItem component, double x, double y)
    {
        var area = desktop.View.Document.Areas.Single(area => area.Components.Contains(component));
        var areaBounds = desktop.View.Snapshot.Areas[area.Id];
        var center = desktop.View.GetComponentBounds(component.Id).Center;
        Assert.Equal(areaBounds.X + areaBounds.Width / 2 + x, center.X, precision: 5);
        Assert.Equal(areaBounds.Y + areaBounds.Height / 2 + y, center.Y, precision: 5);
        var polygon = desktop.View.GetVisualDescendants().OfType<ComponentView>().Single(view => view.Item.Id == component.Id)
            .GetVisualDescendants().OfType<Button>().Single();
        var renderedCenter = polygon.TranslatePoint(new Point(polygon.Bounds.Width / 2, polygon.Bounds.Height / 2), desktop.View);
        Assert.NotNull(renderedCenter);
        Assert.InRange(Math.Abs(renderedCenter.Value.X - center.X), 0, 1);
        Assert.InRange(Math.Abs(renderedCenter.Value.Y - center.Y), 0, 1);
    }
}
