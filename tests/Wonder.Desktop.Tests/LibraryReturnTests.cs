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

public sealed class LibraryReturnTests
{
    [AvaloniaFact]
    public void Return_drop_removes_only_the_dragged_shortcut_and_retains_application_identity()
    {
        using var desktop = new ReturnDesktop();
        using var data = Payload(desktop.Workspace, desktop.Shortcut);
        var areaCount = desktop.Workspace.Document.Areas.Count();
        var libraryIds = desktop.Workspace.Document.Library.Select(item => item.Id).ToArray();

        var drop = desktop.Send(DragDrop.DropEvent, data);

        Assert.Equal(DragDropEffects.Move, drop.DragEffects);
        Assert.DoesNotContain(desktop.Shortcut, desktop.Shortcuts);
        Assert.Contains(desktop.OtherAlias, desktop.Shortcuts);
        Assert.Equal(areaCount, desktop.Workspace.Document.Areas.Count());
        Assert.Equal(libraryIds, desktop.Workspace.Document.Library.Select(item => item.Id));
        Assert.Same(desktop.Application, desktop.Workspace.Document.Library.Single(item => item.DefinitionId == ComponentIds.Settings));
        Assert.Equal(0, desktop.ActionCount);
        Assert.Same(desktop.Application, desktop.OtherAlias.Application);
        Assert.Equal(1, desktop.Changes);
    }

    [AvaloniaFact]
    public void Return_hover_and_leave_never_remove_shortcuts()
    {
        using var desktop = new ReturnDesktop();
        using var data = Payload(desktop.Workspace, desktop.Shortcut);
        var originalIds = desktop.Shortcuts.Select(item => item.Id).ToArray();

        var enter = desktop.Send(DragDrop.DragEnterEvent, data);
        var over = desktop.Send(DragDrop.DragOverEvent, data);
        desktop.Send(DragDrop.DragLeaveEvent, data);

        Assert.Equal(DragDropEffects.Move, enter.DragEffects);
        Assert.Equal(DragDropEffects.Move, over.DragEffects);
        Assert.Equal(originalIds, desktop.Shortcuts.Select(item => item.Id));
        Assert.Same(desktop.Application, desktop.Workspace.Document.Library.Single(item => item.DefinitionId == ComponentIds.Settings));
        Assert.Equal(0, desktop.ActionCount);
        Assert.Equal(0, desktop.Changes);
    }

    [AvaloniaFact]
    public void Library_source_payload_cannot_delete_a_workspace_shortcut()
    {
        using var desktop = new ReturnDesktop();
        using IDataTransfer data = new DataTransfer();
        ((DataTransfer)data).Add(DataTransferItem.Create(LibraryComponentTransfer.Format,
            new LibraryComponentTransfer(desktop.Application, default)));

        var drop = desktop.Send(DragDrop.DropEvent, data);

        Assert.Equal(DragDropEffects.None, drop.DragEffects);
        Assert.Contains(desktop.Shortcut, desktop.Shortcuts);
        Assert.Equal(0, desktop.Changes);
    }

    [AvaloniaFact]
    public void External_text_is_rejected_without_changing_applications_or_shortcuts()
    {
        using var desktop = new ReturnDesktop();
        using IDataTransfer data = new DataTransfer();
        ((DataTransfer)data).Add(DataTransferItem.CreateText(desktop.Shortcut.Id));

        var over = desktop.Send(DragDrop.DragOverEvent, data);
        var drop = desktop.Send(DragDrop.DropEvent, data);

        Assert.Equal(DragDropEffects.None, over.DragEffects);
        Assert.Equal(DragDropEffects.None, drop.DragEffects);
        Assert.Contains(desktop.Shortcut, desktop.Shortcuts);
        Assert.Same(desktop.Application, desktop.Workspace.Document.Library.Single(item => item.DefinitionId == ComponentIds.Settings));
        Assert.Equal(0, desktop.ActionCount);
        Assert.Equal(0, desktop.Changes);
    }

    [AvaloniaFact]
    public void Another_workspace_cannot_remove_a_shortcut_through_this_library()
    {
        using var desktop = new ReturnDesktop();
        var foreignWorkspace = new WorkspaceView(TestWorkspaceFactory.CreateThreeAreas());
        var foreignShortcut = foreignWorkspace.Document.Areas.First().Components.Single();
        using var data = Payload(foreignWorkspace, foreignShortcut);

        var drop = desktop.Send(DragDrop.DropEvent, data);

        Assert.Equal(DragDropEffects.None, drop.DragEffects);
        Assert.Contains(desktop.Shortcut, desktop.Shortcuts);
        Assert.Contains(foreignShortcut, foreignWorkspace.Document.Areas.First().Components);
        Assert.Equal(0, desktop.Changes);
    }

    [AvaloniaFact]
    public void Already_removed_shortcut_payload_is_rejected()
    {
        using var desktop = new ReturnDesktop();
        using var data = Payload(desktop.Workspace, desktop.Shortcut);
        desktop.Workspace.Document.Areas.Single(area => area.Components.Contains(desktop.Shortcut)).Components.Remove(desktop.Shortcut);

        var drop = desktop.Send(DragDrop.DropEvent, data);

        Assert.Equal(DragDropEffects.None, drop.DragEffects);
        Assert.Contains(desktop.OtherAlias, desktop.Shortcuts);
        Assert.Same(desktop.Application, desktop.Workspace.Document.Library.Single(item => item.DefinitionId == ComponentIds.Settings));
        Assert.Equal(0, desktop.ActionCount);
        Assert.Equal(0, desktop.Changes);
    }

    [AvaloniaFact]
    public void Same_id_on_an_unowned_object_cannot_remove_the_real_shortcut()
    {
        using var desktop = new ReturnDesktop();
        var impostor = new ComponentItem { Id = desktop.Shortcut.Id, DefinitionId = desktop.Shortcut.DefinitionId, ApplicationId = desktop.Shortcut.ApplicationId };
        using var data = Payload(desktop.Workspace, impostor);

        var drop = desktop.Send(DragDrop.DropEvent, data);

        Assert.Equal(DragDropEffects.None, drop.DragEffects);
        Assert.Contains(desktop.Shortcut, desktop.Shortcuts);
        Assert.Equal(0, desktop.Changes);
    }

    [AvaloniaFact]
    public void Return_drop_requires_move_permission()
    {
        using var desktop = new ReturnDesktop();
        using var data = Payload(desktop.Workspace, desktop.Shortcut);

        var drop = desktop.Send(DragDrop.DropEvent, data, effects: DragDropEffects.Copy);

        Assert.Equal(DragDropEffects.None, drop.DragEffects);
        Assert.Contains(desktop.Shortcut, desktop.Shortcuts);
        Assert.Equal(0, desktop.Changes);
    }

    [AvaloniaFact]
    public void Drop_on_library_settings_button_removes_shortcut_without_opening_settings()
    {
        using var desktop = new ReturnDesktop();
        var settingsButton = desktop.Library.GetVisualDescendants().OfType<Button>()
            .Single(button => AutomationProperties.GetName(button) == "打开设置");
        using var data = Payload(desktop.Workspace, desktop.Shortcut);

        var drop = desktop.Send(DragDrop.DropEvent, data, target: settingsButton);

        Assert.Equal(DragDropEffects.Move, drop.DragEffects);
        Assert.DoesNotContain(desktop.Shortcut, desktop.Shortcuts);
        Assert.Same(desktop.Application, desktop.Workspace.Document.Library.Single(item => item.DefinitionId == ComponentIds.Settings));
        Assert.Equal(0, desktop.ActionCount);
        Assert.Equal(1, desktop.Changes);
    }

    [AvaloniaTheory]
    [InlineData(1020)]
    [InlineData(600)]
    public async Task Long_pressed_workspace_shortcut_can_be_dragged_back_to_library_window(int libraryWidth)
    {
        using var desktop = new ReturnDesktop();
        desktop.LibraryWindow.Width = libraryWidth;
        Dispatcher.UIThread.RunJobs();
        var start = desktop.Workspace.GetComponentBounds(desktop.Shortcut.Id).Center;
        var target = new Point(100, 200);
        var sourceRelativeTarget = desktop.WorkspaceWindow.PointToClient(desktop.LibraryWindow.PointToScreen(target));

        desktop.WorkspaceWindow.MouseDown(start, MouseButton.Left);
        await Task.Delay(550);
        desktop.WorkspaceWindow.MouseMove(sourceRelativeTarget, RawInputModifiers.LeftMouseButton);
        await Task.Yield();
        desktop.LibraryWindow.MouseMove(target, RawInputModifiers.LeftMouseButton);
        desktop.LibraryWindow.MouseUp(target, MouseButton.Left);
        await Task.Yield();
        Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain(desktop.Shortcut, desktop.Shortcuts);
        Assert.Contains(desktop.OtherAlias, desktop.Shortcuts);
        Assert.Same(desktop.Application, desktop.Workspace.Document.Library.Single(item => item.DefinitionId == ComponentIds.Settings));
        Assert.Equal(0, desktop.ActionCount);
        Assert.Same(desktop.Application, desktop.OtherAlias.Application);
        Assert.Equal(1, desktop.Changes);
    }

    [AvaloniaFact]
    public async Task Escape_during_native_return_drag_preserves_shortcut_and_original_position()
    {
        using var desktop = new ReturnDesktop();
        // Headless screen conversion ignores window positions. Keep the initial
        // source point outside the library's simulated client rectangle.
        desktop.LibraryWindow.Width = 600;
        Dispatcher.UIThread.RunJobs();
        var originalPosition = (desktop.Shortcut.X, desktop.Shortcut.Y);
        var start = desktop.Workspace.GetComponentBounds(desktop.Shortcut.Id).Center;
        var target = new Point(100, 200);
        var sourceRelativeTarget = desktop.WorkspaceWindow.PointToClient(desktop.LibraryWindow.PointToScreen(target));

        desktop.WorkspaceWindow.MouseDown(start, MouseButton.Left);
        await Task.Delay(550);
        desktop.WorkspaceWindow.MouseMove(start + new Vector(40, 20), RawInputModifiers.LeftMouseButton);
        Assert.NotEqual(start, desktop.Workspace.GetComponentBounds(desktop.Shortcut.Id).Center);
        Assert.Equal(originalPosition, (desktop.Shortcut.X, desktop.Shortcut.Y));
        desktop.WorkspaceWindow.MouseMove(sourceRelativeTarget, RawInputModifiers.LeftMouseButton);
        await Task.Yield();
        desktop.LibraryWindow.MouseMove(target, RawInputModifiers.LeftMouseButton);
        desktop.LibraryWindow.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        desktop.LibraryWindow.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        desktop.LibraryWindow.MouseUp(target, MouseButton.Left);
        await Task.Yield();

        Assert.Contains(desktop.Shortcut, desktop.Shortcuts);
        Assert.Equal(originalPosition, (desktop.Shortcut.X, desktop.Shortcut.Y));
        Assert.Same(desktop.Application, desktop.Workspace.Document.Library.Single(item => item.DefinitionId == ComponentIds.Settings));
        Assert.Equal(0, desktop.ActionCount);
        Assert.Equal(0, desktop.Changes);
    }

    [AvaloniaFact]
    public void Drop_over_search_box_also_removes_shortcut_without_changing_query()
    {
        using var desktop = new ReturnDesktop();
        var search = desktop.Library.GetVisualDescendants().OfType<TextBox>()
            .Single(control => AutomationProperties.GetName(control) == "搜索组件");
        search.Text = "设置";
        Dispatcher.UIThread.RunJobs();
        var point = search.TranslatePoint(new Point(search.Bounds.Width / 2, search.Bounds.Height / 2), desktop.LibraryWindow)!.Value;
        using var data = Payload(desktop.Workspace, desktop.Shortcut);
        desktop.LibraryWindow.DragDrop(point, RawDragEventType.DragEnter, data, DragDropEffects.Move);
        desktop.LibraryWindow.DragDrop(point, RawDragEventType.Drop, data, DragDropEffects.Move);
        Assert.DoesNotContain(desktop.Shortcut, desktop.Shortcuts);
        Assert.Equal("设置", search.Text);
        Assert.Same(desktop.Application, desktop.Workspace.Document.Library.Single(item => item.DefinitionId == ComponentIds.Settings));
        Assert.Equal(0, desktop.ActionCount);
    }

    private static IDataTransfer Payload(WorkspaceView source, ComponentItem shortcut)
    {
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(WorkspaceComponentTransfer.Format,
            new WorkspaceComponentTransfer(source, shortcut)));
        return data;
    }

    private sealed class ReturnDesktop : IDisposable
    {
        public WorkspaceView Workspace { get; }
        public ComponentLibraryView Library { get; }
        public Window WorkspaceWindow { get; }
        public Window LibraryWindow { get; }
        public ComponentItem Shortcut { get; }
        public ComponentItem OtherAlias { get; }
        public ComponentItem Application { get; }
        public IEnumerable<ComponentItem> Shortcuts => Workspace.Document.Areas.SelectMany(area => area.Components);
        public int Changes { get; private set; }
        public int ActionCount { get; private set; }

        public ReturnDesktop()
        {
            var document = TestWorkspaceFactory.CreateThreeAreas();
            Application = document.Library.Single(item => item.DefinitionId == ComponentIds.Settings);
            Shortcut = document.Areas.ElementAt(1).Components.Single();
            OtherAlias = document.CreateShortcut(Application);
            OtherAlias.X = 120;
            document.Areas.First().Components.Add(OtherAlias);
            Workspace = new WorkspaceView(document);
            Workspace.Changed += (_, _) => Changes++;
            Workspace.SettingsRequested += () => ActionCount++;
            WorkspaceWindow = new Window
            {
                Width = 1100, Height = 740, Position = new PixelPoint(0, 0),
                WindowDecorations = WindowDecorations.None, Content = Workspace
            };
            Library = new ComponentLibraryView(Workspace, document.Library.ToArray(), () => { });
            LibraryWindow = new Window
            {
                Width = 1020, Height = 740, Position = new PixelPoint(1200, 0),
                WindowDecorations = WindowDecorations.None, Content = Library
            };
            WorkspaceWindow.Show();
            LibraryWindow.Show(WorkspaceWindow);
            Dispatcher.UIThread.RunJobs();
        }

        public DragEventArgs Send(RoutedEvent<DragEventArgs> routedEvent, IDataTransfer data,
            DragDropEffects effects = DragDropEffects.Move, Control? target = null)
        {
            target ??= Library;
            var point = new Point(target.Bounds.Width / 2, target.Bounds.Height / 2);
            var args = new DragEventArgs(routedEvent, data, target, point, KeyModifiers.None) { DragEffects = effects };
            target.RaiseEvent(args);
            Dispatcher.UIThread.RunJobs();
            return args;
        }

        public void Dispose()
        {
            Library.Detach();
            LibraryWindow.Close();
            WorkspaceWindow.Close();
        }
    }
}
