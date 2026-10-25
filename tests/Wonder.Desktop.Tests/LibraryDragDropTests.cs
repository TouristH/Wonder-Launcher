// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Wonder.Workspace;
using Xunit;

namespace Wonder.Desktop.Tests;

public sealed class LibraryDragDropTests
{
    [AvaloniaFact]
    public void Enter_and_over_advertise_copy_without_mutating_workspace()
    {
        using var desktop = new LibraryDesktop();
        var before = desktop.View.Document.Areas.SelectMany(area => area.Components).Select(item => item.Id).ToArray();
        using var data = Payload(desktop.View, new Vector(12, -8));
        var point = AreaCenter(desktop.View, 1);

        var enter = desktop.Send(DragDrop.DragEnterEvent, data, point);
        var over = desktop.Send(DragDrop.DragOverEvent, data, point + new Vector(20, 10));

        Assert.Equal(DragDropEffects.Copy, enter.DragEffects);
        Assert.Equal(DragDropEffects.Copy, over.DragEffects);
        Assert.Equal(before, desktop.View.Document.Areas.SelectMany(area => area.Components).Select(item => item.Id));
        Assert.Equal(0, desktop.Changes);
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Drop_links_application_in_pointed_area_using_pointer_grab_offset(int areaIndex)
    {
        using var desktop = new LibraryDesktop();
        var source = desktop.SourceComponents.Single(item => item.DefinitionId == ComponentIds.Settings);
        var sourceId = source.Id;
        using var data = Payload(desktop.View, new Vector(20, -15));
        var targetArea = desktop.View.Document.Areas.ElementAt(areaIndex);
        var originalIds = targetArea.Components.Select(item => item.Id).ToHashSet();
        var point = AreaCenter(desktop.View, areaIndex) + new Vector(30, 25);

        var drop = desktop.Send(DragDrop.DropEvent, data, point);

        Assert.Equal(DragDropEffects.Copy, drop.DragEffects);
        var added = Assert.Single(targetArea.Components, item => !originalIds.Contains(item.Id));
        Assert.Equal(ComponentIds.Settings, added.DefinitionId);
        Assert.Same(source.Application, added.Application);
        Assert.Equal(50, added.X, precision: 5);
        Assert.Equal(10, added.Y, precision: 5);
        Assert.NotEqual(sourceId, added.Id);
        Assert.Equal(ComponentIds.Settings, source.DefinitionId);
        Assert.Equal(sourceId, source.Id);
        Assert.Contains(source, desktop.SourceComponents);
        Assert.Equal(1, desktop.Changes);
    }

    [AvaloniaFact]
    public void Repeated_drop_creates_shortcuts_with_shared_application_identity()
    {
        using var desktop = new LibraryDesktop();
        var source = desktop.SourceComponents.Single(item => item.DefinitionId == ComponentIds.Settings);
        var target = desktop.View.Document.Areas.First();
        var count = target.Components.Count;
        using var data = Payload(desktop.View, default);
        var point = AreaCenter(desktop.View, 0);

        desktop.Send(DragDrop.DropEvent, data, point);
        desktop.Send(DragDrop.DropEvent, data, point);

        var copies = target.Components.Skip(count).ToArray();
        Assert.Equal(2, copies.Length);
        Assert.NotEqual(copies[0].Id, copies[1].Id);
        Assert.All(copies, copy => Assert.NotEqual(source.Id, copy.Id));
        Assert.All(copies, copy => Assert.Same(source.Application, copy.Application));
        Assert.Equal(3, desktop.SourceComponents.Count);
    }

    [AvaloniaFact]
    public void Drop_near_area_corner_clamps_new_component_inside_content()
    {
        using var desktop = new LibraryDesktop();
        var area = desktop.View.Document.Areas.First();
        var bounds = desktop.View.Snapshot.Areas[area.Id];
        var originalIds = area.Components.Select(item => item.Id).ToHashSet();
        using var data = Payload(desktop.View, new Vector(-200, -200));

        desktop.Send(DragDrop.DropEvent, data, new Point(bounds.X + 1, bounds.Y + 1));

        var added = Assert.Single(area.Components, item => !originalIds.Contains(item.Id));
        var placed = desktop.View.GetComponentBounds(added.Id);
        Assert.True(placed.Left >= bounds.X + 14);
        Assert.True(placed.Top >= bounds.Y + 46);
        Assert.True(placed.Right <= bounds.X + bounds.Width - 14);
        Assert.True(placed.Bottom <= bounds.Y + bounds.Height - 14);
    }

    [AvaloniaFact]
    public void Empty_workspace_stays_empty_during_over_and_creates_area_only_on_drop()
    {
        using var desktop = new LibraryDesktop(WorkspaceDocument.CreateEmpty());
        using var data = Payload(desktop.View, default);
        var point = new Point(550, 370);

        var over = desktop.Send(DragDrop.DragOverEvent, data, point);
        Assert.Equal(DragDropEffects.Copy, over.DragEffects);
        Assert.Empty(desktop.View.Document.Areas);
        Assert.Equal(0, desktop.Changes);
        var drop = desktop.Send(DragDrop.DropEvent, data, point);

        Assert.Equal(DragDropEffects.Copy, drop.DragEffects);
        var area = Assert.Single(desktop.View.Document.Areas);
        var item = Assert.Single(area.Components);
        Assert.Equal(ComponentIds.Settings, item.DefinitionId);
        Assert.Same(desktop.View.Document.Library.Single(item => item.DefinitionId == ComponentIds.Settings), item.Application);
        Assert.Equal((0d, 0d), (item.X, item.Y));
        Assert.Equal(1, desktop.Changes);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Gap_or_outside_drop_is_rejected_without_changing_document(bool outside)
    {
        using var desktop = new LibraryDesktop();
        using var data = Payload(desktop.View, default);
        var gap = desktop.View.Snapshot.Splits.Values.First();
        var point = outside ? new Point(-20, -20) : new Point(gap.X + gap.Width / 2, gap.Y + gap.Height / 2);
        var count = desktop.View.Document.Areas.Sum(area => area.Components.Count);

        var over = desktop.Send(DragDrop.DragOverEvent, data, point);
        var drop = desktop.Send(DragDrop.DropEvent, data, point);

        Assert.Equal(DragDropEffects.None, over.DragEffects);
        Assert.Equal(DragDropEffects.None, drop.DragEffects);
        Assert.Equal(count, desktop.View.Document.Areas.Sum(area => area.Components.Count));
        Assert.Equal(0, desktop.Changes);
    }

    [AvaloniaFact]
    public void Full_area_rejects_copy_without_overflowing_component_limit()
    {
        var document = TestWorkspaceFactory.CreateThreeAreas();
        var area = document.Areas.First();
        while (area.Components.Count < 24)
            area.Components.Add(new ComponentItem { DefinitionId = ComponentIds.Settings });
        using var desktop = new LibraryDesktop(document);
        using var data = Payload(desktop.View, default);
        var point = AreaCenter(desktop.View, 0);

        var over = desktop.Send(DragDrop.DragOverEvent, data, point);
        var drop = desktop.Send(DragDrop.DropEvent, data, point);

        Assert.Equal(DragDropEffects.None, over.DragEffects);
        Assert.Equal(DragDropEffects.None, drop.DragEffects);
        Assert.Equal(24, area.Components.Count);
        Assert.Equal(0, desktop.Changes);
    }

    [AvaloniaFact]
    public void Unrelated_external_data_is_rejected()
    {
        using var desktop = new LibraryDesktop();
        using IDataTransfer data = new DataTransfer { };
        ((DataTransfer)data).Add(DataTransferItem.CreateText("not a Wonder component"));
        var count = desktop.View.Document.Areas.Sum(area => area.Components.Count);

        var drop = desktop.Send(DragDrop.DropEvent, data, AreaCenter(desktop.View, 0));

        Assert.Equal(DragDropEffects.None, drop.DragEffects);
        Assert.Equal(count, desktop.View.Document.Areas.Sum(area => area.Components.Count));
        Assert.Equal(0, desktop.Changes);
    }

    [AvaloniaFact]
    public void Drag_leave_does_not_commit_or_create_components()
    {
        using var desktop = new LibraryDesktop(WorkspaceDocument.CreateEmpty());
        using var data = Payload(desktop.View, default);
        desktop.Send(DragDrop.DragEnterEvent, data, new Point(400, 300));
        desktop.Send(DragDrop.DragLeaveEvent, data, new Point(-10, -10));

        Assert.Empty(desktop.View.Document.Areas);
        Assert.Equal(0, desktop.Changes);
    }

    [AvaloniaFact]
    public void Headless_raw_drag_events_copy_into_a_second_window()
    {
        using var desktop = new LibraryDesktop();
        using var data = Payload(desktop.View, default);
        var targetArea = desktop.View.Document.Areas.ElementAt(1);
        var before = targetArea.Components.Count;
        var point = AreaCenter(desktop.View, 1);

        desktop.TargetWindow.DragDrop(point, RawDragEventType.DragEnter, data, DragDropEffects.Copy);
        desktop.TargetWindow.DragDrop(point, RawDragEventType.DragOver, data, DragDropEffects.Copy);
        desktop.TargetWindow.DragDrop(point, RawDragEventType.Drop, data, DragDropEffects.Copy);

        Assert.Equal(before + 1, targetArea.Components.Count);
        Assert.Same(desktop.View.Document.Library.Single(item => item.DefinitionId == ComponentIds.Settings), targetArea.Components.Last().Application);
        Assert.Equal(3, desktop.SourceComponents.Count);
        Assert.Equal(1, desktop.Changes);
    }

    [AvaloniaFact]
    public async Task Dragging_library_handle_into_another_window_copies_source_component()
    {
        using var desktop = new LibraryDesktop();
        var source = desktop.SourceComponents.Single(item => item.DefinitionId == ComponentIds.Settings);
        var handle = desktop.SourceWindow.GetVisualDescendants().OfType<Control>()
            .Single(control => AutomationProperties.GetName(control) == "拖动设置");
        var start = handle.TranslatePoint(new Point(handle.Bounds.Width / 2, handle.Bounds.Height / 2), desktop.SourceWindow);
        Assert.NotNull(start);
        var area = desktop.View.Document.Areas.ElementAt(1);
        var originalIds = area.Components.Select(item => item.Id).ToHashSet();
        var target = AreaCenter(desktop.View, 1) + new Vector(20, 10);

        desktop.SourceWindow.MouseDown(start.Value, MouseButton.Left);
        desktop.SourceWindow.MouseMove(start.Value + new Vector(10, 0), RawInputModifiers.LeftMouseButton);
        await Task.Yield();
        desktop.TargetWindow.MouseMove(target, RawInputModifiers.LeftMouseButton);
        desktop.TargetWindow.MouseUp(target, MouseButton.Left);
        await Task.Yield();
        Dispatcher.UIThread.RunJobs();

        var copied = Assert.Single(area.Components, item => !originalIds.Contains(item.Id));
        Assert.Equal(ComponentIds.Settings, copied.DefinitionId);
        Assert.Same(source.Application, copied.Application);
        Assert.Equal((20d, 10d), (copied.X, copied.Y));
        Assert.NotEqual(source.Id, copied.Id);
        Assert.Equal(0, desktop.ActionCount);
        Assert.Contains(source, desktop.SourceComponents);
    }

    [AvaloniaFact]
    public async Task Dragging_operated_library_component_preserves_identity_and_grab_point()
    {
        using var desktop = new LibraryDesktop();
        var settingsButton = desktop.SourceWindow.GetVisualDescendants().OfType<Button>()
            .Single(button => AutomationProperties.GetName(button) == "打开设置");
        var buttonPosition = settingsButton.TranslatePoint(new Point(settingsButton.Bounds.Width / 2, settingsButton.Bounds.Height / 2), desktop.SourceWindow);
        Assert.NotNull(buttonPosition);
        for (var click = 0; click < 2; click++)
        {
            desktop.SourceWindow.MouseDown(buttonPosition.Value, MouseButton.Left);
            desktop.SourceWindow.MouseUp(buttonPosition.Value, MouseButton.Left);
        }
        var preview = desktop.SourceWindow.GetVisualDescendants().OfType<ComponentView>()
            .Single(view => view.Item.DefinitionId == ComponentIds.Settings);
        var source = preview.Item;
        Assert.Equal(2, desktop.ActionCount);
        var grab = new Point(preview.Bounds.Width / 2 - 30, preview.Bounds.Height / 2 - 20);
        var start = preview.TranslatePoint(grab, desktop.SourceWindow);
        Assert.NotNull(start);
        var area = desktop.View.Document.Areas.ElementAt(2);
        var originalIds = area.Components.Select(item => item.Id).ToHashSet();
        var target = AreaCenter(desktop.View, 2) + new Vector(20, 10);

        desktop.SourceWindow.MouseDown(start.Value, MouseButton.Left);
        desktop.SourceWindow.MouseMove(start.Value + new Vector(10, 0), RawInputModifiers.LeftMouseButton);
        await Task.Yield();
        desktop.TargetWindow.MouseMove(target, RawInputModifiers.LeftMouseButton);
        desktop.TargetWindow.MouseUp(target, MouseButton.Left);
        await Task.Yield();
        Dispatcher.UIThread.RunJobs();

        var copied = Assert.Single(area.Components, item => !originalIds.Contains(item.Id));
        Assert.Equal(ComponentIds.Settings, copied.DefinitionId);
        Assert.Same(source.Application, copied.Application);
        Assert.Equal((50d, 30d), (copied.X, copied.Y));
        Assert.NotEqual(source.Id, copied.Id);
        Assert.Equal(2, desktop.ActionCount);
    }

    [AvaloniaFact]
    public async Task Escape_cancels_native_library_drag_without_creating_a_copy()
    {
        using var desktop = new LibraryDesktop();
        var handle = desktop.SourceWindow.GetVisualDescendants().OfType<Control>()
            .Single(control => AutomationProperties.GetName(control) == "拖动设置");
        var start = handle.TranslatePoint(new Point(handle.Bounds.Width / 2, handle.Bounds.Height / 2), desktop.SourceWindow);
        Assert.NotNull(start);
        var before = desktop.View.Document.Areas.Sum(area => area.Components.Count);
        var target = AreaCenter(desktop.View, 1);

        desktop.SourceWindow.MouseDown(start.Value, MouseButton.Left);
        desktop.SourceWindow.MouseMove(start.Value + new Vector(10, 0), RawInputModifiers.LeftMouseButton);
        await Task.Yield();
        desktop.TargetWindow.MouseMove(target, RawInputModifiers.LeftMouseButton);
        desktop.TargetWindow.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        desktop.TargetWindow.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        desktop.TargetWindow.MouseUp(target, MouseButton.Left);
        await Task.Yield();

        Assert.Equal(before, desktop.View.Document.Areas.Sum(area => area.Components.Count));
        Assert.Equal(0, desktop.Changes);
        Assert.Equal(3, desktop.SourceComponents.Count);
    }

    private static IDataTransfer Payload(WorkspaceView workspace, Vector grabOffset)
    {
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(LibraryComponentTransfer.Format, new LibraryComponentTransfer(workspace.Document.Library.Single(item => item.DefinitionId == ComponentIds.Settings), grabOffset)));
        return data;
    }

    private static Point AreaCenter(WorkspaceView view, int index)
    {
        var bounds = view.Snapshot.Areas[view.Document.Areas.ElementAt(index).Id];
        return new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
    }

    private sealed class LibraryDesktop : IDisposable
    {
        public WorkspaceView View { get; }
        public Window TargetWindow { get; }
        public Window SourceWindow { get; }
        private ComponentLibraryView Library { get; }
        public IReadOnlyList<ComponentItem> SourceComponents => SourceWindow.GetVisualDescendants().OfType<ComponentView>().Select(view => view.Item).ToArray();
        public int Changes { get; private set; }
        public int ActionCount { get; private set; }

        public LibraryDesktop(WorkspaceDocument? document = null)
        {
            View = new WorkspaceView(document ?? TestWorkspaceFactory.CreateThreeAreas());
            View.Changed += (_, _) => Changes++;
            View.SettingsRequested += () => ActionCount++;
            TargetWindow = new Window { Width = 1100, Height = 740, Content = View, WindowDecorations = WindowDecorations.None };
            var sourceItems = View.Document.Library.ToArray();
            Library = new ComponentLibraryView(View, sourceItems, () => { });
            SourceWindow = new Window
            {
                Width = 1020, Height = 740, WindowDecorations = WindowDecorations.None,
                Content = Library
            };
            TargetWindow.Show();
            SourceWindow.Show();
            Dispatcher.UIThread.RunJobs();
        }

        public DragEventArgs Send(RoutedEvent<DragEventArgs> routedEvent, IDataTransfer data, Point point)
        {
            var args = new DragEventArgs(routedEvent, data, View, point, KeyModifiers.None) { DragEffects = DragDropEffects.Copy };
            View.RaiseEvent(args);
            Dispatcher.UIThread.RunJobs();
            return args;
        }

        public void Dispose()
        {
            Library.Detach();
            SourceWindow.Close();
            TargetWindow.Close();
        }
    }
}
