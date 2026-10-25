// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia;
using Avalonia.Headless.XUnit;
using Wonder.Workspace;
using Xunit;

namespace Wonder.Desktop.Tests;

public sealed partial class WorkspaceInteractionTests
{
    [AvaloniaFact]
    public async Task Long_pressing_area_body_does_not_swap_regions()
    {
        using var desktop = new TestDesktop();
        var before = desktop.View.Document.Areas.Select(area => area.Id).ToArray();
        var bounds = desktop.View.Snapshot.Areas[before[0]];
        var body = new Point(bounds.X + 30, bounds.Y + 120);
        var target = HeaderPoint(desktop.View.Snapshot.Areas[before[1]]);

        await desktop.Hold(body);
        desktop.Move(target);
        desktop.Release(target);

        Assert.Equal(before, desktop.View.Document.Areas.Select(area => area.Id));
        Assert.Equal(0, desktop.ChangeCount);
    }

    [AvaloniaFact]
    public async Task Dragging_component_across_regions_preserves_instance_identity_and_uses_target_center()
    {
        using var desktop = new TestDesktop();
        var source = desktop.View.Document.Areas.First();
        var target = desktop.View.Document.Areas.Skip(1).First();
        var component = source.Components.Single();
        var application = component.Application;
        var targetBounds = desktop.View.Snapshot.Areas[target.Id];
        var drop = Center(targetBounds) + new Vector(25, 10);

        await desktop.Hold(desktop.View.GetComponentBounds(component.Id).Center);
        desktop.Move(drop);
        desktop.Release(drop);

        Assert.DoesNotContain(component, source.Components);
        Assert.Contains(component, target.Components);
        Assert.Same(application, component.Application);
        Assert.Equal(0, desktop.ActionCount);
        Assert.Equal((25d, 10d), (component.X, component.Y));
        AssertCenteredOffset(desktop, component, 25, 10);
        Assert.Equal(3, desktop.View.Document.Areas.SelectMany(area => area.Components).Count());
        Assert.Equal(1, desktop.ChangeCount);
    }

    [AvaloniaFact]
    public async Task Escape_before_cross_region_drop_preserves_source_and_position()
    {
        using var desktop = new TestDesktop();
        var source = desktop.View.Document.Areas.First();
        var target = desktop.View.Document.Areas.Last();
        var component = source.Components.Single();
        var original = (component.X, component.Y);
        var drop = Center(desktop.View.Snapshot.Areas[target.Id]);

        await desktop.Hold(desktop.View.GetComponentBounds(component.Id).Center);
        desktop.Move(drop);
        desktop.Escape();
        desktop.Release(drop);

        Assert.Contains(component, source.Components);
        Assert.DoesNotContain(component, target.Components);
        Assert.Equal(original, (component.X, component.Y));
        Assert.Equal(0, desktop.ChangeCount);
    }

    [AvaloniaFact]
    public async Task Dropping_component_on_separator_cancels_transfer()
    {
        using var desktop = new TestDesktop();
        var source = desktop.View.Document.Areas.First();
        var component = source.Components.Single();
        var original = (component.X, component.Y);
        var drop = Center(desktop.View.Snapshot.Splits.First().Value);

        await desktop.Hold(desktop.View.GetComponentBounds(component.Id).Center);
        desktop.Move(drop);
        desktop.Release(drop);

        Assert.Contains(component, source.Components);
        Assert.Equal(original, (component.X, component.Y));
        Assert.Equal(0, desktop.ChangeCount);
    }
}
