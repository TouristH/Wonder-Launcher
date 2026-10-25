// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
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
    public void Manual_edge_resize_collapses_to_rail_and_preserves_all_component_data(SidebarEdge edge)
    {
        var (document, area) = SidebarDocument(edge);
        using var desktop = new TestDesktop(document: document);
        var components = document.Areas.SelectMany(item => item.Components)
            .Select(item => (item.Id, item.ApplicationId, item.DefinitionId, item.X, item.Y)).ToArray();
        var applications = document.Library.Select(item => (item.Id, item.DefinitionId)).ToArray();
        var (start, target) = CollapsePoints(desktop, edge);

        desktop.Press(start);
        desktop.Move(target);
        desktop.Release(target);

        Assert.Equal(edge, area.SidebarEdge);
        var rail = desktop.View.Snapshot.Areas[area.Id];
        Assert.Equal(28, IsHorizontalSidebar(edge) ? rail.Width : rail.Height, precision: 5);
        Assert.True(area.SidebarExtent > 64);
        Assert.Equal(components, document.Areas.SelectMany(item => item.Components)
            .Select(item => (item.Id, item.ApplicationId, item.DefinitionId, item.X, item.Y)));
        Assert.Equal(applications, document.Library.Select(item => (item.Id, item.DefinitionId)));
        Assert.Equal(1, desktop.ChangeCount);
    }

    [AvaloniaTheory]
    [InlineData(SidebarEdge.Left)]
    [InlineData(SidebarEdge.Right)]
    [InlineData(SidebarEdge.Top)]
    [InlineData(SidebarEdge.Bottom)]
    public void Hovered_sidebar_restores_fixed_extent_and_compresses_sibling(SidebarEdge edge)
    {
        var (document, area) = SidebarDocument(edge, collapsed: true);
        using var desktop = new TestDesktop(document: document);
        var baseBounds = desktop.View.Snapshot.Areas[area.Id];
        var sibling = document.Areas.Single(candidate => candidate != area);
        var siblingBounds = desktop.View.Snapshot.Areas[sibling.Id];
        var ratio = document.Root!.Ratio;

        desktop.Window.MouseMove(Center(baseBounds));

        var expanded = desktop.View.GetAreaBounds(area.Id);
        var compressed = desktop.View.GetAreaBounds(sibling.Id);
        Assert.Equal(320, IsHorizontalSidebar(edge) ? expanded.Width : expanded.Height, precision: 5);
        Assert.Equal(expanded, desktop.View.Snapshot.Areas[area.Id]);
        Assert.True(IsHorizontalSidebar(edge) ? compressed.Width < siblingBounds.Width : compressed.Height < siblingBounds.Height);
        Assert.False(new Rect(expanded.X, expanded.Y, expanded.Width, expanded.Height)
            .Intersects(new Rect(compressed.X, compressed.Y, compressed.Width, compressed.Height)));
        Assert.Equal(ratio, document.Root.Ratio);
        Assert.Equal(edge, area.SidebarEdge);
        Assert.Equal(0, desktop.ChangeCount);
    }

    [AvaloniaTheory]
    [InlineData(SidebarEdge.Left)]
    [InlineData(SidebarEdge.Right)]
    [InlineData(SidebarEdge.Top)]
    [InlineData(SidebarEdge.Bottom)]
    public void Components_in_hovered_sidebar_remain_clickable_beyond_rail(SidebarEdge edge)
    {
        var (document, area) = SidebarDocument(edge, collapsed: true);
        using var desktop = new TestDesktop(document: document);
        var component = Assert.Single(area.Components);
        var originalActions = desktop.ActionCount;
        var baseRect = desktop.View.Snapshot.Areas[area.Id];
        desktop.Window.MouseMove(Center(desktop.View.Snapshot.Areas[area.Id]));
        var point = desktop.View.GetComponentBounds(component.Id).Center;
        Assert.False(new Rect(baseRect.X, baseRect.Y, baseRect.Width, baseRect.Height).Contains(point));

        desktop.Click(point);

        Assert.Equal(originalActions + 1, desktop.ActionCount);
        Assert.Equal(edge, area.SidebarEdge);
        Assert.Equal(0, desktop.ChangeCount);
    }

    [AvaloniaTheory]
    [InlineData(SidebarEdge.Left)]
    [InlineData(SidebarEdge.Top)]
    public void Window_shrink_alone_does_not_create_sidebar(SidebarEdge edge)
    {
        var (document, _) = SidebarDocument(edge);
        using var desktop = new TestDesktop(document: document);
        desktop.Window.Width = 100;
        desktop.Window.Height = 90;
        Dispatcher.UIThread.RunJobs();

        Assert.All(document.Areas, area => Assert.Equal(SidebarEdge.None, area.SidebarEdge));
        Assert.Equal(0, desktop.ChangeCount);
    }

    [AvaloniaTheory]
    [InlineData(SidebarEdge.Left)]
    [InlineData(SidebarEdge.Right)]
    [InlineData(SidebarEdge.Top)]
    [InlineData(SidebarEdge.Bottom)]
    public void Escape_cancels_pending_collapse_and_restores_ratio_extent_and_layout(SidebarEdge edge)
    {
        var (document, area) = SidebarDocument(edge);
        using var desktop = new TestDesktop(document: document);
        var originalBounds = desktop.View.Snapshot.Areas[area.Id];
        var originalRatio = document.Root!.Ratio;
        var originalExtent = area.SidebarExtent;
        var (start, target) = CollapsePoints(desktop, edge);
        desktop.Press(start);
        desktop.Move(target);

        desktop.Escape();
        desktop.Release(target);

        Assert.Equal(SidebarEdge.None, area.SidebarEdge);
        Assert.Equal(originalRatio, document.Root.Ratio);
        Assert.Equal(originalExtent, area.SidebarExtent);
        Assert.Equal(originalBounds, desktop.View.Snapshot.Areas[area.Id]);
        Assert.Equal(0, desktop.ChangeCount);
    }

    [AvaloniaFact]
    public void Capture_loss_cancels_pending_sidebar_collapse()
    {
        var (document, area) = SidebarDocument(SidebarEdge.Left);
        using var desktop = new TestDesktop(document: document);
        var originalBounds = desktop.View.Snapshot.Areas[area.Id];
        var (start, target) = CollapsePoints(desktop, SidebarEdge.Left);
        desktop.Press(start);
        desktop.Move(target);
        Assert.NotNull(desktop.LastPointer);

        desktop.LastPointer.Capture(desktop.Window);
        desktop.Release(target);

        Assert.Equal(SidebarEdge.None, area.SidebarEdge);
        Assert.Equal(originalBounds, desktop.View.Snapshot.Areas[area.Id]);
        Assert.Equal(0, desktop.ChangeCount);
    }

    [AvaloniaTheory]
    [InlineData(SidebarEdge.Left)]
    [InlineData(SidebarEdge.Right)]
    [InlineData(SidebarEdge.Top)]
    [InlineData(SidebarEdge.Bottom)]
    public void Dragging_expanded_sidebar_inner_edge_restores_normal_region(SidebarEdge edge)
    {
        var (document, area) = SidebarDocument(edge, collapsed: true);
        using var desktop = new TestDesktop(document: document);
        desktop.Window.MouseMove(Center(desktop.View.Snapshot.Areas[area.Id]));
        var resize = desktop.View.GetVisualDescendants().OfType<Control>()
            .Single(control => AutomationProperties.GetName(control) == "调整侧边栏大小");
        var start = resize.TranslatePoint(new Point(resize.Bounds.Width / 2, resize.Bounds.Height / 2), desktop.View);
        Assert.NotNull(start);
        var offset = edge switch
        {
            SidebarEdge.Left => new Vector(60, 0),
            SidebarEdge.Right => new Vector(-60, 0),
            SidebarEdge.Top => new Vector(0, 60),
            _ => new Vector(0, -60)
        };
        var end = start.Value + offset;

        desktop.Press(start.Value);
        desktop.Move(end);
        desktop.Release(end);

        Assert.Equal(SidebarEdge.None, area.SidebarEdge);
        var restored = desktop.View.Snapshot.Areas[area.Id];
        Assert.True((IsHorizontalSidebar(edge) ? restored.Width : restored.Height) > 320);
        Assert.Equal(1, desktop.ChangeCount);
    }

    [AvaloniaFact]
    public async Task Leaving_sidebar_restores_sibling_without_mutating_document()
    {
        var (document, area) = SidebarDocument(SidebarEdge.Left, collapsed: true);
        using var desktop = new TestDesktop(document: document);
        var rail = desktop.View.Snapshot.Areas[area.Id];
        var sibling = document.Areas.Single(candidate => candidate != area);
        var siblingBounds = desktop.View.GetAreaBounds(sibling.Id);
        desktop.Window.MouseMove(Center(rail));
        Assert.True(desktop.View.GetAreaBounds(area.Id).Width > rail.Width);

        desktop.Window.MouseMove(new Point(1000, 700));
        await Task.Delay(350);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(rail, desktop.View.GetAreaBounds(area.Id));
        Assert.Equal(siblingBounds, desktop.View.GetAreaBounds(sibling.Id));
        Assert.Equal(SidebarEdge.Left, area.SidebarEdge);
        Assert.Equal(0, desktop.ChangeCount);
    }

    [AvaloniaFact]
    public void Drop_on_expanded_sidebar_uses_reflowed_region_bounds()
    {
        var (document, area) = SidebarDocument(SidebarEdge.Left, collapsed: true);
        using var desktop = new TestDesktop(document: document);
        var sibling = document.Areas.Single(candidate => candidate != area);
        var siblingCount = sibling.Components.Count;
        var originalCount = area.Components.Count;
        desktop.Window.MouseMove(Center(desktop.View.Snapshot.Areas[area.Id]));
        var target = Center(desktop.View.GetAreaBounds(area.Id));
        using IDataTransfer data = new DataTransfer();
        ((DataTransfer)data).Add(DataTransferItem.Create(LibraryComponentTransfer.Format,
            new LibraryComponentTransfer(document.Library.Single(item => item.DefinitionId == ComponentIds.Settings), default)));

        desktop.Window.DragDrop(target, RawDragEventType.DragEnter, data, DragDropEffects.Copy);
        desktop.Window.DragDrop(target, RawDragEventType.Drop, data, DragDropEffects.Copy);

        Assert.Equal(originalCount + 1, area.Components.Count);
        Assert.Equal(siblingCount, sibling.Components.Count);
        Assert.Equal(SidebarEdge.Left, area.SidebarEdge);
    }

    [AvaloniaFact]
    public void Interior_region_cannot_collapse_to_an_outer_sidebar()
    {
        var document = WorkspaceDocument.CreateEmpty();
        var left = document.AddArea();
        document.AddArea(left.Id, SplitAxis.Horizontal);
        var interior = document.AddArea(left.Id, SplitAxis.Horizontal);
        using var desktop = new TestDesktop(document: document);
        var nested = document.Root!.First!;
        var divider = desktop.View.Snapshot.Splits[nested.Id];
        var areaBounds = desktop.View.Snapshot.Areas[interior.Id];
        var start = Center(divider);
        var target = new Point(areaBounds.X + areaBounds.Width - 53, start.Y);

        desktop.Press(start);
        desktop.Move(target);
        desktop.Release(target);

        Assert.Equal(SidebarEdge.None, interior.SidebarEdge);
        Assert.True(desktop.View.Snapshot.Areas[interior.Id].Width >= 160);
    }

    [AvaloniaTheory]
    [InlineData(SidebarEdge.Left)]
    [InlineData(SidebarEdge.Right)]
    [InlineData(SidebarEdge.Top)]
    [InlineData(SidebarEdge.Bottom)]
    public void Escape_cancels_expanded_sidebar_resize_without_pinning_or_changing_offsets(SidebarEdge edge)
    {
        var (document, area) = SidebarDocument(edge, collapsed: true);
        using var desktop = new TestDesktop(document: document);
        desktop.Window.MouseMove(Center(desktop.View.Snapshot.Areas[area.Id]));
        var original = desktop.View.GetAreaBounds(area.Id);
        var component = Assert.Single(area.Components);
        var offsets = (component.X, component.Y);
        var resize = desktop.View.GetVisualDescendants().OfType<Control>()
            .Single(control => AutomationProperties.GetName(control) == "调整侧边栏大小");
        var start = resize.TranslatePoint(new Point(resize.Bounds.Width / 2, resize.Bounds.Height / 2), desktop.View)!.Value;
        var delta = edge switch
        {
            SidebarEdge.Left => new Vector(60, 0),
            SidebarEdge.Right => new Vector(-60, 0),
            SidebarEdge.Top => new Vector(0, 60),
            _ => new Vector(0, -60)
        };
        desktop.Press(start);
        desktop.Move(start + delta);
        Assert.NotEqual(original, desktop.View.GetAreaBounds(area.Id));

        desktop.Escape();
        desktop.Release(start + delta);

        Assert.Equal(edge, area.SidebarEdge);
        Assert.Equal(320, area.SidebarExtent);
        Assert.Equal(original, desktop.View.GetAreaBounds(area.Id));
        Assert.Equal(offsets, (component.X, component.Y));
        Assert.Equal(0, desktop.ChangeCount);
    }

    [AvaloniaFact]
    public async Task Component_can_move_from_normal_region_into_hover_opened_sidebar()
    {
        var (document, area) = SidebarDocument(SidebarEdge.Left, collapsed: true);
        using var desktop = new TestDesktop(document: document);
        var source = document.Areas.Single(candidate => candidate != area);
        var component = Assert.Single(source.Components);
        var originalActions = desktop.ActionCount;
        await desktop.Hold(desktop.View.GetComponentBounds(component.Id).Center);

        desktop.Move(Center(desktop.View.Snapshot.Areas[area.Id]));
        var target = Center(desktop.View.GetAreaBounds(area.Id));
        desktop.Move(target);
        desktop.Release(target);

        Assert.DoesNotContain(component, source.Components);
        Assert.Contains(component, area.Components);
        Assert.Equal(originalActions, desktop.ActionCount);
        Assert.Equal(0, component.X, precision: 5);
        Assert.Equal(0, component.Y, precision: 5);
        Assert.Equal(SidebarEdge.Left, area.SidebarEdge);
        Assert.Equal(1, desktop.ChangeCount);
    }

    [AvaloniaFact]
    public async Task Header_drag_can_hover_open_and_swap_with_collapsed_sidebar()
    {
        var (document, sidebar) = SidebarDocument(SidebarEdge.Left, collapsed: true);
        using var desktop = new TestDesktop(document: document);
        var source = document.Areas.Single(area => area != sidebar);
        var sidebarComponent = Assert.Single(sidebar.Components);
        var originalOffsets = (sidebarComponent.X, sidebarComponent.Y);
        await desktop.Hold(HeaderPoint(desktop.View.GetAreaBounds(source.Id)));

        desktop.Move(Center(desktop.View.Snapshot.Areas[sidebar.Id]));
        Assert.Equal(320, desktop.View.GetAreaBounds(sidebar.Id).Width);
        var target = HeaderPoint(desktop.View.GetAreaBounds(sidebar.Id));
        desktop.Move(target);
        desktop.Release(target);

        Assert.Same(source, document.Root!.First!.Area);
        Assert.Same(sidebar, document.Root.Second!.Area);
        Assert.All(document.Areas, area => Assert.Equal(SidebarEdge.None, area.SidebarEdge));
        Assert.Equal(originalOffsets, (sidebarComponent.X, sidebarComponent.Y));
        Assert.Equal(1, desktop.ChangeCount);
    }

    [AvaloniaFact]
    public async Task Library_drag_preview_stays_above_expanded_sidebar_and_leave_closes_it()
    {
        var (document, area) = SidebarDocument(SidebarEdge.Left, collapsed: true);
        using var desktop = new TestDesktop(document: document);
        using IDataTransfer data = new DataTransfer();
        ((DataTransfer)data).Add(DataTransferItem.Create(LibraryComponentTransfer.Format,
            new LibraryComponentTransfer(document.Library.Single(item => item.DefinitionId == ComponentIds.Settings), default)));
        desktop.Window.DragDrop(Center(desktop.View.Snapshot.Areas[area.Id]), RawDragEventType.DragEnter, data, DragDropEffects.Copy);
        var target = Center(desktop.View.GetAreaBounds(area.Id));
        desktop.Window.DragDrop(target, RawDragEventType.DragOver, data, DragDropEffects.Copy);
        var overlay = desktop.View.Children.OfType<AreaView>().Single(view => view.Area == area);
        var preview = Assert.Single(desktop.View.Children.OfType<ComponentView>());
        Assert.True(preview.ZIndex > overlay.ZIndex);
        var hint = desktop.View.Children.OfType<Border>()
            .Single(control => control.Child is TextBlock { Text: "松开创建快捷方式" });
        Assert.True(hint.ZIndex > overlay.ZIndex);

        desktop.Window.DragDrop(target, RawDragEventType.DragLeave, data, DragDropEffects.Copy);
        await Task.Delay(350);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(desktop.View.Snapshot.Areas[area.Id], desktop.View.GetAreaBounds(area.Id));
        Assert.Equal(28, desktop.View.GetAreaBounds(area.Id).Width);
        Assert.Single(area.Components);
        Assert.Equal(0, desktop.ChangeCount);
    }

    [AvaloniaTheory]
    [InlineData(SidebarEdge.Left)]
    [InlineData(SidebarEdge.Right)]
    [InlineData(SidebarEdge.Top)]
    [InlineData(SidebarEdge.Bottom)]
    public void Sidebar_rail_and_reflowed_region_render_in_all_directions(SidebarEdge edge)
    {
        var (document, area) = SidebarDocument(edge, collapsed: true);
        using var desktop = new TestDesktop(document: document);
        var directory = Environment.GetEnvironmentVariable("WONDER_SIDEBAR_PREVIEW_DIR")
            ?? Path.Combine(AppContext.BaseDirectory, "artifacts");
        Directory.CreateDirectory(directory);
        using (var collapsed = desktop.Window.CaptureRenderedFrame())
        {
            Assert.NotNull(collapsed);
            collapsed.Save(Path.Combine(directory, $"wonder-sidebar-{edge.ToString().ToLowerInvariant()}-collapsed.png"), PngBitmapEncoderOptions.Default);
        }
        desktop.Window.MouseMove(Center(desktop.View.Snapshot.Areas[area.Id]));
        var overlay = desktop.View.Children.OfType<AreaView>().Single(view => view.Area == area);
        Assert.True(overlay.IsVisible);
        var bounds = desktop.View.GetAreaBounds(area.Id);
        Assert.Equal(new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height), overlay.Bounds);
        using var expanded = desktop.Window.CaptureRenderedFrame();
        Assert.NotNull(expanded);
        expanded.Save(Path.Combine(directory, $"wonder-sidebar-{edge.ToString().ToLowerInvariant()}-expanded.png"), PngBitmapEncoderOptions.Default);
    }

    [AvaloniaTheory]
    [InlineData(SidebarEdge.Left)]
    [InlineData(SidebarEdge.Right)]
    [InlineData(SidebarEdge.Top)]
    [InlineData(SidebarEdge.Bottom)]
    public async Task Collapsing_large_region_does_not_capture_inflated_extent_or_grow_on_repeated_hover(SidebarEdge edge)
    {
        var (document, area) = SidebarDocument(edge);
        document.Root!.Ratio = edge is SidebarEdge.Left or SidebarEdge.Top ? .8 : .2;
        using var desktop = new TestDesktop(document: document);
        var before = desktop.View.GetAreaBounds(area.Id);
        Assert.True((IsHorizontalSidebar(edge) ? before.Width : before.Height) > 320);
        var (start, target) = CollapsePoints(desktop, edge);
        desktop.Press(start);
        desktop.Move(target);
        desktop.Release(target);
        Assert.Equal(320, area.SidebarExtent);
        var rail = desktop.View.GetAreaBounds(area.Id);
        var sibling = document.Areas.Single(candidate => candidate != area);
        var offsets = area.Components.Select(item => (item.X, item.Y)).ToArray();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            desktop.Window.MouseMove(Center(rail));
            var expanded = desktop.View.GetAreaBounds(area.Id);
            Assert.Equal(320, IsHorizontalSidebar(edge) ? expanded.Width : expanded.Height, precision: 5);
            desktop.Window.MouseMove(Center(desktop.View.GetAreaBounds(sibling.Id)));
            await Task.Delay(350);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(rail, desktop.View.GetAreaBounds(area.Id));
        }
        Assert.Equal(offsets, area.Components.Select(item => (item.X, item.Y)));
        Assert.Equal(320, area.SidebarExtent);
        Assert.Equal(1, desktop.ChangeCount);
    }

    [AvaloniaTheory]
    [InlineData(SidebarEdge.Left)]
    [InlineData(SidebarEdge.Top)]
    public void Clicking_resize_edge_in_small_window_does_not_overwrite_preferred_extent(SidebarEdge edge)
    {
        var (document, area) = SidebarDocument(edge, collapsed: true);
        using var desktop = new TestDesktop(document: document);
        desktop.Window.Width = 400;
        desktop.Window.Height = 300;
        Dispatcher.UIThread.RunJobs();
        desktop.Window.MouseMove(Center(desktop.View.Snapshot.Areas[area.Id]));
        var limited = desktop.View.GetAreaBounds(area.Id);
        Assert.True((IsHorizontalSidebar(edge) ? limited.Width : limited.Height) < 320);
        var resize = desktop.View.GetVisualDescendants().OfType<Control>()
            .Single(control => AutomationProperties.GetName(control) == "调整侧边栏大小");
        var point = resize.TranslatePoint(new Point(resize.Bounds.Width / 2, resize.Bounds.Height / 2), desktop.View)!.Value;

        desktop.Click(point);

        Assert.Equal(320, area.SidebarExtent);
        Assert.Equal(edge, area.SidebarEdge);
        Assert.Equal(0, desktop.ChangeCount);
        desktop.Window.Width = 1100;
        desktop.Window.Height = 740;
        Dispatcher.UIThread.RunJobs();
        var restored = desktop.View.GetAreaBounds(area.Id);
        Assert.Equal(320, IsHorizontalSidebar(edge) ? restored.Width : restored.Height, precision: 5);
    }

    [AvaloniaFact]
    public void Previously_saved_oversized_sidebar_is_bounded_without_rewriting_component_offsets()
    {
        var (document, area) = SidebarDocument(SidebarEdge.Left, collapsed: true);
        area.SidebarExtent = 900;
        using var desktop = new TestDesktop(document: document);
        var component = Assert.Single(area.Components);
        var offsets = (component.X, component.Y);

        desktop.Window.MouseMove(Center(desktop.View.Snapshot.Areas[area.Id]));

        Assert.Equal(320, desktop.View.GetAreaBounds(area.Id).Width);
        Assert.Equal(offsets, (component.X, component.Y));
        Assert.Equal(900, area.SidebarExtent);
        Assert.Equal(0, desktop.ChangeCount);
    }

    private static (WorkspaceDocument Document, Area Sidebar) SidebarDocument(SidebarEdge edge, bool collapsed = false)
    {
        var document = WorkspaceDocument.CreateEmpty();
        var first = document.AddArea();
        var second = document.AddArea(first.Id, IsHorizontalSidebar(edge) ? SplitAxis.Horizontal : SplitAxis.Vertical);
        var firstShortcut = document.CreateShortcut(document.Library.Single(item => item.DefinitionId == ComponentIds.Settings));
        firstShortcut.X = 12;
        firstShortcut.Y = -9;
        first.Components.Add(firstShortcut);
        var secondShortcut = document.CreateShortcut(document.Library.Single(item => item.DefinitionId == ComponentIds.Settings));
        secondShortcut.X = -14;
        secondShortcut.Y = 18;
        second.Components.Add(secondShortcut);
        var area = edge is SidebarEdge.Left or SidebarEdge.Top ? first : second;
        area.SidebarExtent = 320;
        if (collapsed) area.SidebarEdge = edge;
        return (document, area);
    }

    private static bool IsHorizontalSidebar(SidebarEdge edge) => edge is SidebarEdge.Left or SidebarEdge.Right;

    private static (Point Start, Point Target) CollapsePoints(TestDesktop desktop, SidebarEdge edge)
    {
        var divider = Assert.Single(desktop.View.Snapshot.Splits).Value;
        var start = Center(divider);
        var target = edge switch
        {
            SidebarEdge.Left => new Point(53, start.Y),
            SidebarEdge.Right => new Point(desktop.View.Bounds.Width - 53, start.Y),
            SidebarEdge.Top => new Point(start.X, 53),
            _ => new Point(start.X, desktop.View.Bounds.Height - 53)
        };
        return (start, target);
    }
}
