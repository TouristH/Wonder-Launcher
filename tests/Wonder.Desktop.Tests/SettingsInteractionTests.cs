// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia;
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
    [AvaloniaFact]
    public void Settings_area_numbers_apply_to_rebuilt_regions_without_changing_component_positions()
    {
        using var desktop = new TestDesktop();
        var components = desktop.View.Document.Areas.SelectMany(area => area.Components).ToArray();
        var positions = components.Select(component => (component.X, component.Y)).ToArray();
        desktop.View.ApplySettings(new LauncherSettings { ShowAreaNumbers = true });
        Assert.Equal(0, desktop.ChangeCount);
        var document = desktop.View.Document;
        document.AddArea(document.Areas.Last().Id);

        desktop.View.ReplaceDocument(document);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(positions, components.Select(component => (component.X, component.Y)));
        var numbers = desktop.View.GetVisualDescendants().OfType<TextBlock>()
            .Where(block => block.IsEffectivelyVisible && block.Text is "01" or "02" or "03" or "04");
        Assert.Equal(new[] { "01", "02", "03", "04" }, numbers.Select(block => block.Text));
        var changesBeforeHide = desktop.ChangeCount;
        desktop.View.ApplySettings(new LauncherSettings());
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(numbers);
        Assert.Equal(changesBeforeHide, desktop.ChangeCount);
    }

    [AvaloniaFact]
    public async Task Settings_short_hold_delay_starts_drag_before_the_default_threshold()
    {
        using var desktop = new TestDesktop();
        desktop.View.ApplySettings(new LauncherSettings { HoldDelayMilliseconds = 250 });
        var component = desktop.Component();
        var originalPosition = (component.X, component.Y);
        var start = desktop.View.GetComponentBounds(component.Id).Center;
        var end = start + new Vector(42, 28);

        desktop.Press(start);
        await Task.Delay(350);
        Dispatcher.UIThread.RunJobs();
        desktop.Move(end);
        desktop.Release(end);

        Assert.Equal((originalPosition.X + 42, originalPosition.Y + 28), (component.X, component.Y));
        Assert.Equal(1, desktop.ChangeCount);
        Assert.Equal(0, desktop.ActionCount);
    }

    [AvaloniaFact]
    public async Task Settings_long_hold_delay_keeps_movement_before_its_threshold_from_dragging()
    {
        using var desktop = new TestDesktop();
        desktop.View.ApplySettings(new LauncherSettings { HoldDelayMilliseconds = 1000 });
        var component = desktop.Component();
        var originalPosition = (component.X, component.Y);
        var start = desktop.View.GetComponentBounds(component.Id).Center;
        var end = start + new Vector(42, 28);

        desktop.Press(start);
        await Task.Delay(600);
        Dispatcher.UIThread.RunJobs();
        desktop.Move(end);
        desktop.Release(end);

        Assert.Equal(originalPosition, (component.X, component.Y));
        Assert.Equal(0, desktop.ChangeCount);
        Assert.Equal(0, desktop.ActionCount);
    }

    [AvaloniaTheory]
    [InlineData(SidebarEdge.Left)]
    [InlineData(SidebarEdge.Right)]
    [InlineData(SidebarEdge.Top)]
    [InlineData(SidebarEdge.Bottom)]
    public void Settings_sidebar_extent_controls_hover_expansion_and_updates_an_open_sidebar(SidebarEdge edge)
    {
        var (document, area) = SidebarDocument(edge, collapsed: true);
        using var desktop = new TestDesktop(document: document);
        var originalExtent = area.SidebarExtent;
        var originalRatio = document.Root!.Ratio;
        desktop.View.ApplySettings(new LauncherSettings { SidebarExtent = 460 });

        desktop.Window.MouseMove(Center(desktop.View.Snapshot.Areas[area.Id]));
        Dispatcher.UIThread.RunJobs();

        var expanded = desktop.View.GetAreaBounds(area.Id);
        Assert.Equal(460, IsHorizontalSidebar(edge) ? expanded.Width : expanded.Height, precision: 5);

        desktop.View.ApplySettings(new LauncherSettings { SidebarExtent = 240 });
        Dispatcher.UIThread.RunJobs();

        var resized = desktop.View.GetAreaBounds(area.Id);
        Assert.Equal(240, IsHorizontalSidebar(edge) ? resized.Width : resized.Height, precision: 5);
        Assert.Equal(originalExtent, area.SidebarExtent);
        Assert.Equal(originalRatio, document.Root.Ratio);
        Assert.Equal(edge, area.SidebarEdge);
        Assert.Equal(0, desktop.ChangeCount);
    }

    [AvaloniaFact]
    public async Task Settings_sidebar_close_delay_keeps_sidebar_open_until_the_configured_timeout()
    {
        var (document, area) = SidebarDocument(SidebarEdge.Left, collapsed: true);
        using var desktop = new TestDesktop(document: document);
        desktop.View.ApplySettings(new LauncherSettings { SidebarCloseDelayMilliseconds = 1000 });
        var rail = desktop.View.GetAreaBounds(area.Id);
        desktop.Window.MouseMove(Center(rail));
        var expanded = desktop.View.GetAreaBounds(area.Id);
        Assert.True(expanded.Width > rail.Width);

        desktop.Window.MouseMove(new Point(1000, 700));
        await Task.Delay(450);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(expanded, desktop.View.GetAreaBounds(area.Id));

        await Task.Delay(700);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(rail, desktop.View.GetAreaBounds(area.Id));
        Assert.Equal(SidebarEdge.Left, area.SidebarEdge);
        Assert.Equal(0, desktop.ChangeCount);
    }

    [AvaloniaFact]
    public async Task Settings_application_cancels_a_pending_button_press_without_opening_settings()
    {
        using var desktop = new TestDesktop();
        var component = desktop.Component();
        var originalPosition = (component.X, component.Y);
        var originalBounds = desktop.View.GetComponentBounds(component.Id);
        var button = desktop.ButtonPoint("打开设置");
        desktop.Press(button);

        desktop.View.ApplySettings(new LauncherSettings { HoldDelayMilliseconds = 1000 });
        await Task.Delay(550);
        Dispatcher.UIThread.RunJobs();
        desktop.Release(button);

        Assert.Equal(originalPosition, (component.X, component.Y));
        Assert.Equal(originalBounds, desktop.View.GetComponentBounds(component.Id));
        Assert.Equal(0, desktop.ChangeCount);
        Assert.Equal(0, desktop.ActionCount);

        desktop.Click(button);

        Assert.Equal(1, desktop.ActionCount);
    }

    [AvaloniaFact]
    public async Task Settings_application_cancels_an_active_drag_and_restores_its_original_position()
    {
        using var desktop = new TestDesktop();
        var component = desktop.Component();
        var originalPosition = (component.X, component.Y);
        var originalBounds = desktop.View.GetComponentBounds(component.Id);
        var end = originalBounds.Center + new Vector(42, 28);
        await desktop.Hold(originalBounds.Center);
        desktop.Move(end);
        Assert.NotEqual(originalBounds, desktop.View.GetComponentBounds(component.Id));

        desktop.View.ApplySettings(new LauncherSettings { HoldDelayMilliseconds = 1000 });
        Dispatcher.UIThread.RunJobs();
        desktop.Release(end);

        Assert.Equal(originalPosition, (component.X, component.Y));
        Assert.Equal(originalBounds, desktop.View.GetComponentBounds(component.Id));
        Assert.Equal(0, desktop.ChangeCount);
        Assert.Equal(0, desktop.ActionCount);
    }
}
