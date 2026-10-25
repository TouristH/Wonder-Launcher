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
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Wonder.Workspace;
using Xunit;

namespace Wonder.Desktop.Tests;

public sealed partial class WorkspaceInteractionTests
{
    [AvaloniaFact]
    public void Short_click_opens_settings_without_moving_shortcut()
    {
        using var desktop = new TestDesktop();
        var component = desktop.Component(0);
        var originalPosition = (component.X, component.Y);
        var originalActions = desktop.ActionCount;

        desktop.Click(desktop.View.GetComponentBounds(component.Id).Center);

        Assert.Equal(originalActions + 1, desktop.ActionCount);
        Assert.Equal(originalPosition, (component.X, component.Y));
        Assert.Equal(0, desktop.ChangeCount);
    }

    [AvaloniaFact]
    public void Short_click_on_another_shortcut_opens_settings_once()
    {
        using var desktop = new TestDesktop();
        var component = desktop.Component(1);
        var originalActions = desktop.ActionCount;

        desktop.Click(desktop.View.GetComponentBounds(component.Id).Center);

        Assert.Equal(originalActions + 1, desktop.ActionCount);
        Assert.Equal(0, desktop.ChangeCount);
    }

    [AvaloniaFact]
    public void Settings_shortcuts_share_application_and_each_opens_settings()
    {
        using var desktop = new TestDesktop();
        var shortcuts = Enumerable.Range(0, 3).Select(desktop.Component).ToArray();
        Assert.All(shortcuts, item => Assert.Same(shortcuts[0].Application, item.Application));
        foreach (var shortcut in shortcuts)
            desktop.Click(desktop.View.GetComponentBounds(shortcut.Id).Center);
        Assert.Equal(3, desktop.ActionCount);
        Assert.Equal(0, desktop.ChangeCount);
    }

    [AvaloniaFact]
    public async Task Long_press_drag_moves_component_without_activating_it()
    {
        using var desktop = new TestDesktop();
        var component = desktop.Component(0);
        var originalActions = desktop.ActionCount;
        var originalPosition = (component.X, component.Y);
        var start = desktop.View.GetComponentBounds(component.Id).Center;
        var end = start + new Vector(42, 28);

        await desktop.Hold(start);
        desktop.Move(end);
        desktop.Release(end);

        Assert.NotEqual(originalPosition, (component.X, component.Y));
        Assert.Equal(originalActions, desktop.ActionCount);
        Assert.Equal(1, desktop.ChangeCount);
    }

    [AvaloniaFact]
    public async Task Long_press_on_settings_button_moves_shortcut_without_opening_settings()
    {
        using var desktop = new TestDesktop();
        var component = desktop.Component(2);
        var originalActions = desktop.ActionCount;
        var originalPosition = (component.X, component.Y);
        var start = desktop.ButtonPoint("打开设置", 2);
        var end = start + new Vector(35, 22);

        await desktop.Hold(start);
        desktop.Move(end);
        desktop.Release(end);

        Assert.NotEqual(originalPosition, (component.X, component.Y));
        Assert.Equal(originalActions, desktop.ActionCount);
        Assert.Equal(1, desktop.ChangeCount);
    }

    [AvaloniaFact]
    public async Task Early_movement_on_settings_button_does_not_open_settings()
    {
        using var desktop = new TestDesktop();
        var component = desktop.Component(2);
        var originalActions = desktop.ActionCount;
        var originalPosition = (component.X, component.Y);
        var start = desktop.ButtonPoint("打开设置", 2);
        var end = start + new Vector(10, 0);

        desktop.Press(start);
        desktop.Move(end);
        await Task.Delay(550);
        desktop.Release(end);

        Assert.Equal(originalPosition, (component.X, component.Y));
        Assert.Equal(originalActions, desktop.ActionCount);
        Assert.Equal(0, desktop.ChangeCount);
    }

    [AvaloniaFact]
    public async Task Long_press_without_movement_suppresses_click()
    {
        using var desktop = new TestDesktop();
        var component = desktop.Component(0);
        var originalActions = desktop.ActionCount;
        var originalPosition = (component.X, component.Y);
        var start = desktop.View.GetComponentBounds(component.Id).Center;

        await desktop.Hold(start);
        desktop.Release(start);

        Assert.Equal(originalActions, desktop.ActionCount);
        Assert.Equal(originalPosition, (component.X, component.Y));
    }

    [AvaloniaFact]
    public async Task Movement_before_long_press_threshold_cancels_both_drag_and_click()
    {
        using var desktop = new TestDesktop();
        var component = desktop.Component(0);
        var originalActions = desktop.ActionCount;
        var originalPosition = (component.X, component.Y);
        var start = desktop.View.GetComponentBounds(component.Id).Center;
        var end = start + new Vector(25, 16);

        desktop.Press(start);
        desktop.Move(end);
        await Task.Delay(550);
        desktop.Release(end);

        Assert.Equal(originalPosition, (component.X, component.Y));
        Assert.Equal(originalActions, desktop.ActionCount);
        Assert.Equal(0, desktop.ChangeCount);
    }

    [AvaloniaFact]
    public async Task Dragging_area_header_swaps_the_whole_area_payload()
    {
        using var desktop = new TestDesktop();
        var areas = desktop.View.Document.Areas.Take(2).ToArray();
        var firstBounds = desktop.View.Snapshot.Areas[areas[0].Id];
        var secondBounds = desktop.View.Snapshot.Areas[areas[1].Id];
        var originalPayload = areas[0].Components.Select(c => (c.Id, c.DefinitionId, c.ApplicationId)).ToArray();
        var originalSecondPayload = areas[1].Components.Select(c => (c.Id, c.DefinitionId, c.ApplicationId)).ToArray();
        var target = HeaderPoint(secondBounds);

        await desktop.Hold(HeaderPoint(firstBounds));
        desktop.Move(target);
        desktop.Release(target);

        Assert.Equal(secondBounds, desktop.View.Snapshot.Areas[areas[0].Id]);
        Assert.Equal(firstBounds, desktop.View.Snapshot.Areas[areas[1].Id]);
        Assert.Equal(originalPayload, desktop.View.Document.FindArea(areas[0].Id)!.Components.Select(c => (c.Id, c.DefinitionId, c.ApplicationId)));
        Assert.Equal(originalSecondPayload, desktop.View.Document.FindArea(areas[1].Id)!.Components.Select(c => (c.Id, c.DefinitionId, c.ApplicationId)));
        Assert.Equal(1, desktop.ChangeCount);
    }

    [AvaloniaFact]
    public void Press_drag_on_shared_divider_immediately_resizes_the_adjacent_areas()
    {
        using var desktop = new TestDesktop();
        var divider = desktop.View.Snapshot.Splits.First();
        var initialAreas = desktop.View.Snapshot.Areas.ToDictionary(pair => pair.Key, pair => pair.Value);
        var start = Center(divider.Value);
        var end = start + (divider.Value.Width < divider.Value.Height ? new Vector(70, 0) : new Vector(0, 70));

        desktop.Press(start);
        desktop.Move(end);
        desktop.Release(end);

        Assert.NotEqual(divider.Value, desktop.View.Snapshot.Splits[divider.Key]);
        Assert.Contains(desktop.View.Snapshot.Areas, pair => pair.Value != initialAreas[pair.Key]);
        Assert.All(desktop.View.Snapshot.Areas.Values, bounds =>
        {
            Assert.True(bounds.Width > 0);
            Assert.True(bounds.Height > 0);
        });
        Assert.Equal(1, desktop.ChangeCount);
    }

    [AvaloniaFact]
    public async Task Escape_rolls_back_a_component_drag_without_clicking()
    {
        using var desktop = new TestDesktop();
        var component = desktop.Component(0);
        var originalPosition = (component.X, component.Y);
        var originalActions = desktop.ActionCount;
        var start = desktop.View.GetComponentBounds(component.Id).Center;
        var end = start + new Vector(50, 32);

        await desktop.Hold(start);
        desktop.Move(end);
        desktop.Escape();
        desktop.Release(end);

        var restored = desktop.FindComponent(component.Id);
        Assert.Equal(originalPosition, (restored.X, restored.Y));
        Assert.Equal(originalActions, desktop.ActionCount);
        Assert.Equal(0, desktop.ChangeCount);
    }

    [AvaloniaFact]
    public void Escape_rolls_back_immediate_divider_resize()
    {
        using var desktop = new TestDesktop();
        var divider = desktop.View.Snapshot.Splits.First();
        var start = Center(divider.Value);
        var end = start + (divider.Value.Width < divider.Value.Height ? new Vector(60, 0) : new Vector(0, 60));

        desktop.Press(start);
        desktop.Move(end);
        Assert.NotEqual(divider.Value, desktop.View.Snapshot.Splits[divider.Key]);
        desktop.Escape();
        desktop.Release(end);

        Assert.Equal(divider.Value, desktop.View.Snapshot.Splits[divider.Key]);
        Assert.Equal(0, desktop.ChangeCount);
    }

    [AvaloniaFact]
    public async Task Losing_pointer_capture_cancels_component_drag()
    {
        using var desktop = new TestDesktop();
        var component = desktop.Component(0);
        var originalPosition = (component.X, component.Y);
        var originalActions = desktop.ActionCount;
        var start = desktop.View.GetComponentBounds(component.Id).Center;
        var end = start + new Vector(50, 32);

        await desktop.Hold(start);
        desktop.Move(end);
        Assert.NotNull(desktop.LastPointer);
        desktop.LastPointer.Capture(desktop.Window);
        desktop.Release(end);

        var restored = desktop.FindComponent(component.Id);
        Assert.Equal(originalPosition, (restored.X, restored.Y));
        Assert.Equal(originalActions, desktop.ActionCount);
        Assert.Equal(0, desktop.ChangeCount);
    }

    [AvaloniaFact]
    public async Task Releasing_component_drag_outside_workspace_rolls_it_back()
    {
        using var desktop = new TestDesktop();
        var component = desktop.Component(0);
        var originalPosition = (component.X, component.Y);
        var originalActions = desktop.ActionCount;
        var outside = new Point(-30, -30);

        await desktop.Hold(desktop.View.GetComponentBounds(component.Id).Center);
        desktop.Move(outside);
        desktop.Release(outside);

        var restored = desktop.FindComponent(component.Id);
        Assert.Equal(originalPosition, (restored.X, restored.Y));
        Assert.Equal(originalActions, desktop.ActionCount);
        Assert.Equal(0, desktop.ChangeCount);
    }

    [AvaloniaFact]
    public async Task Releasing_pending_header_press_outside_workspace_does_not_start_late_drag()
    {
        using var desktop = new TestDesktop(boundedWorkspace: true);
        var header = HeaderPoint(desktop.View.Snapshot.Areas.Values.First());
        var outside = new Point(1070, 710);

        desktop.Press(header);
        desktop.Move(outside);
        desktop.Release(outside);
        await Task.Delay(550);
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(desktop.LastPointer);
        Assert.Null(desktop.LastPointer.Captured);
        Assert.Equal(0, desktop.ChangeCount);
        var component = desktop.Component(0);
        var originalActions = desktop.ActionCount;
        desktop.Click(desktop.View.GetComponentBounds(component.Id).Center);
        Assert.Equal(originalActions + 1, desktop.ActionCount);
    }

    [AvaloniaFact]
    public void Escape_during_pending_button_press_suppresses_click_on_release()
    {
        using var desktop = new TestDesktop();
        var component = desktop.Component(0);
        var originalActions = desktop.ActionCount;
        var point = desktop.View.GetComponentBounds(component.Id).Center;

        desktop.Press(point);
        desktop.Escape();
        desktop.Release(point);

        Assert.Equal(originalActions, desktop.ActionCount);
        Assert.Equal(0, desktop.ChangeCount);
    }

    [AvaloniaFact]
    public void Keyboard_context_menu_removes_the_focused_component()
    {
        using var desktop = new TestDesktop();
        var focusedShortcut = desktop.Component(2);
        var firstShortcut = desktop.Component(0);
        var menu = desktop.OpenComponentMenu(2);
        var remove = Assert.Single(menu.Items.OfType<MenuItem>(), item => Equals(item.Header, "移除这个组件"));

        TestDesktop.ClickMenuItem(remove);

        Assert.DoesNotContain(desktop.View.Document.Areas.SelectMany(area => area.Components), item => item.Id == focusedShortcut.Id);
        Assert.Contains(desktop.View.Document.Areas.SelectMany(area => area.Components), item => item.Id == firstShortcut.Id);
    }

    [AvaloniaFact]
    public void Keyboard_context_menu_removes_the_focused_area_instead_of_first_area()
    {
        using var desktop = new TestDesktop();
        var focusedShortcut = desktop.Component(2);
        var focusedAreaId = desktop.View.Document.Areas.Single(area => area.Components.Contains(focusedShortcut)).Id;
        var firstAreaId = desktop.View.Document.Areas.First().Id;
        var menu = desktop.OpenComponentMenu(2);
        var remove = Assert.Single(menu.Items.OfType<MenuItem>(), item => Equals(item.Header, "移除此功能区"));

        TestDesktop.ClickMenuItem(remove);

        Assert.Null(desktop.View.Document.FindArea(focusedAreaId));
        Assert.NotNull(desktop.View.Document.FindArea(firstAreaId));
        Assert.Equal(2, desktop.View.Document.Areas.Count());
    }

    [AvaloniaFact]
    public void Replacing_document_with_empty_workspace_removes_old_hit_targets()
    {
        using var desktop = new TestDesktop();
        var component = desktop.Component(0);
        var oldPosition = desktop.View.GetComponentBounds(component.Id).Center;
        var originalActions = desktop.ActionCount;

        desktop.View.ReplaceDocument(WorkspaceDocument.CreateEmpty());
        Dispatcher.UIThread.RunJobs();
        desktop.Click(oldPosition);

        Assert.Empty(desktop.View.Document.Areas);
        Assert.Empty(desktop.View.Snapshot.Areas);
        Assert.Equal(originalActions, desktop.ActionCount);
    }

    [AvaloniaFact]
    public void Three_region_workspace_renders_a_preview_at_desktop_size()
    {
        using var desktop = new TestDesktop();
        using var frame = desktop.Window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Assert.Equal(new PixelSize(1100, 740), frame.PixelSize);
        Assert.Equal(3, desktop.View.Snapshot.Areas.Count);
        var configuredPath = Environment.GetEnvironmentVariable("WONDER_UI_PREVIEW_PATH");
        var path = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(AppContext.BaseDirectory, "artifacts", "wonder-ui-preview.png")
            : Path.GetFullPath(configuredPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        frame.Save(path, PngBitmapEncoderOptions.Default);
        Assert.True(new FileInfo(path).Length > 1000, "The preview should contain rendered pixels.");
    }

    private static Point HeaderPoint(LayoutRect rectangle) => new(rectangle.X + 40, rectangle.Y + 20);
    private static Point Center(LayoutRect rectangle) => new(rectangle.X + rectangle.Width / 2, rectangle.Y + rectangle.Height / 2);

    private sealed class TestDesktop : IDisposable
    {
        public WorkspaceView View { get; }
        public Window Window { get; }
        public int ChangeCount { get; private set; }
        public int ActionCount { get; private set; }
        public IPointer? LastPointer { get; private set; }

        public TestDesktop(bool boundedWorkspace = false, WorkspaceDocument? document = null, bool animateSidebars = false)
        {
            View = new WorkspaceView(document ?? TestWorkspaceFactory.CreateThreeAreas());
            // Geometry/gesture tests isolate their subject; animation tests opt into real frames.
            if (!animateSidebars) View.SidebarTransitionDuration = TimeSpan.Zero;
            if (boundedWorkspace)
            {
                View.Width = 900;
                View.Height = 600;
                View.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
                View.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
            }
            Window = new Window
            {
                Width = 1100,
                Height = 740,
                WindowDecorations = WindowDecorations.None,
                Background = new SolidColorBrush(Color.Parse("#151820")),
                Content = View
            };
            Window.AddHandler(InputElement.PointerPressedEvent, (_, args) => LastPointer = args.Pointer, RoutingStrategies.Tunnel);
            View.Changed += (_, _) => ChangeCount++;
            View.SettingsRequested += () => ActionCount++;
            Window.Show();
            View.Focus();
            Dispatcher.UIThread.RunJobs();
        }

        public ComponentItem Component(int slot = 0) => View.Document.Areas.SelectMany(area => area.Components).ElementAt(slot);
        public ComponentItem FindComponent(string id) => View.Document.Areas.SelectMany(area => area.Components).Single(item => item.Id == id);
        public void Press(Point position) => Window.MouseDown(position, MouseButton.Left);
        public void Move(Point position) => Window.MouseMove(position, RawInputModifiers.LeftMouseButton);
        public void Release(Point position) => Window.MouseUp(position, MouseButton.Left);

        public void Click(Point position)
        {
            Press(position);
            Release(position);
        }

        public async Task Hold(Point position)
        {
            Press(position);
            await Task.Delay(550);
        }

        public void ClickButton(string automationName)
        {
            Click(ButtonPoint(automationName));
        }

        public Point ButtonPoint(string automationName, int slot = 0)
        {
            var component = Component(slot);
            var button = View.GetVisualDescendants().OfType<ComponentView>().Single(view => view.Item.Id == component.Id)
                .GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button) == automationName);
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), View);
            Assert.NotNull(point);
            return point.Value;
        }

        public ContextMenu OpenComponentMenu(int slot)
        {
            var component = Component(slot);
            var button = View.GetVisualDescendants().OfType<ComponentView>().Single(view => view.Item.Id == component.Id)
                .GetVisualDescendants().OfType<Button>().Single();
            Assert.True(button.Focus());
            Window.KeyPressQwerty(PhysicalKey.F10, RawInputModifiers.Shift);
            Window.KeyReleaseQwerty(PhysicalKey.F10, RawInputModifiers.Shift);
            Assert.NotNull(View.ContextMenu);
            return View.ContextMenu;
        }

        public static void ClickMenuItem(MenuItem item)
        {
            var topLevel = TopLevel.GetTopLevel(item);
            Assert.NotNull(topLevel);
            var position = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), topLevel);
            Assert.NotNull(position);
            topLevel.MouseDown(position.Value, MouseButton.Left);
            topLevel.MouseUp(position.Value, MouseButton.Left);
        }

        public void Escape()
        {
            Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        }

        public void Dispose() => Window.Close();
    }
}
