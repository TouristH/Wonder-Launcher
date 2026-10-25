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

public sealed class ComponentLibraryTests
{
    [AvaloniaFact]
    public void Library_has_no_add_buttons_or_target_picker()
    {
        using var desktop = new LibraryDesktop();
        desktop.OpenLibrary();
        Assert.DoesNotContain(desktop.Library.GetVisualDescendants().OfType<Button>(),
            button => AutomationProperties.GetName(button)?.StartsWith("添加", StringComparison.Ordinal) == true);
        Assert.Empty(desktop.Library.GetVisualDescendants().OfType<ComboBox>());
        Assert.Equal(5, desktop.Library.GetVisualDescendants().OfType<ComponentView>().Count());
    }

    [AvaloniaFact]
    public async Task Library_opens_in_its_own_window_without_hiding_workspace_and_adds_linked_shortcuts()
    {
        using var desktop = new LibraryDesktop();
        desktop.OpenLibrary();
        Assert.True(desktop.Workspace.IsVisible);
        Assert.Contains(desktop.Library.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "组件库");
        var target = desktop.Workspace.Document.Areas.First();
        var originalCount = target.Components.Count;

        await desktop.DragToWorkspace("设置");
        await desktop.DragToWorkspace("设置");

        Assert.Equal(originalCount + 2, target.Components.Count);
        var added = target.Components.TakeLast(2).ToArray();
        Assert.NotEqual(added[0].Id, added[1].Id);
        Assert.Equal(added[0].ApplicationId, added[1].ApplicationId);
        Assert.Same(added[0].Application, added[1].Application);
        Assert.Same(desktop.Workspace.Document.Library.Single(item => item.DefinitionId == ComponentIds.Settings), added[0].Application);
        desktop.ClickButton("返回工作区");
        Assert.True(desktop.Workspace.IsVisible);
        Assert.DoesNotContain(desktop.Window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "组件库");
        desktop.Window.Close();
        Assert.Equal(originalCount + 2, new WorkspaceStore(desktop.WorkspacePath).Load().Areas.First().Components.Count);
    }

    [AvaloniaFact]
    public async Task Library_settings_action_works_after_search_and_reopening()
    {
        using var desktop = new LibraryDesktop();
        desktop.OpenLibrary();
        desktop.ClickButton("打开设置");
        desktop.ClickButton("打开设置");
        var search = desktop.Library.GetVisualDescendants().OfType<TextBox>().Single(box => AutomationProperties.GetName(box) == "搜索组件");
        search.Text = "不存在";
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(desktop.Library.GetVisualDescendants().OfType<ComponentView>());
        search.Text = "";
        Dispatcher.UIThread.RunJobs();
        await desktop.DragToWorkspace("设置");
        var placed = desktop.Workspace.Document.Areas.First().Components.Last();
        Assert.Same(desktop.Workspace.Document.Library.Single(item => item.DefinitionId == ComponentIds.Settings), placed.Application);
        Assert.Equal(2, desktop.ActionCount);
        desktop.Library.Close();
        desktop.OpenLibrary();
        desktop.ClickButton("打开设置");
        await desktop.DragToWorkspace("设置");
        Assert.Equal(3, desktop.ActionCount);
        Assert.Same(placed.Application, desktop.Workspace.Document.Areas.First().Components.Last().Application);
    }

    [AvaloniaFact]
    public void Opening_library_again_reuses_window_and_closing_main_closes_library()
    {
        using var desktop = new LibraryDesktop();
        desktop.OpenLibrary();
        var library = desktop.Library;
        desktop.OpenLibrary();
        Assert.Same(library, desktop.Library);
        Assert.Single(desktop.Window.OwnedWindows);
        desktop.Window.Close();
        Assert.False(library.IsVisible);
    }

    [AvaloniaFact]
    public async Task Returning_a_shortcut_to_library_persists_removal_and_keeps_application()
    {
        using var desktop = new LibraryDesktop();
        desktop.OpenLibrary();
        desktop.ClickButton("打开设置");
        var shortcut = desktop.Workspace.Document.Areas.First().Components.Single();
        var applicationId = shortcut.ApplicationId;
        var start = desktop.Workspace.TranslatePoint(desktop.Workspace.GetComponentBounds(shortcut.Id).Center, desktop.Window)!.Value;
        var target = new Point(100, 200);
        desktop.Window.MouseDown(start, MouseButton.Left);
        await Task.Delay(550);
        desktop.Window.MouseMove(desktop.Window.PointToClient(desktop.Library.PointToScreen(target)), RawInputModifiers.LeftMouseButton);
        await Task.Yield();
        desktop.Library.MouseMove(target, RawInputModifiers.LeftMouseButton);
        desktop.Library.MouseUp(target, MouseButton.Left);
        await Task.Yield();
        Dispatcher.UIThread.RunJobs();
        // Read the automatic save before closing the main window.
        var saved = new WorkspaceStore(desktop.WorkspacePath).Load();
        Assert.DoesNotContain(saved.Areas.SelectMany(area => area.Components), item => item.Id == shortcut.Id);
        Assert.Equal(ComponentIds.Settings, saved.Library.Single(item => item.Id == applicationId).DefinitionId);
        Assert.Equal(1, desktop.ActionCount);
        desktop.ClickButton("打开设置");
        Assert.Equal(2, desktop.ActionCount);
        Assert.Same(shortcut.Application, desktop.Workspace.Document.Library.Single(item => item.Id == applicationId));
    }

    [AvaloniaFact]
    public void Library_and_shortcuts_open_settings_and_library_survives_clearing_and_reload()
    {
        using var desktop = new LibraryDesktop();
        desktop.OpenLibrary();
        var applicationId = desktop.Workspace.Document.Library.Single(item => item.DefinitionId == ComponentIds.Settings).Id;
        desktop.ClickButton("打开设置");
        var shortcutButton = desktop.Workspace.GetVisualDescendants().OfType<Button>()
            .First(button => AutomationProperties.GetName(button) == "打开设置");
        LibraryDesktop.Click(shortcutButton);
        Assert.Equal(2, desktop.ActionCount);
        desktop.CloseSettings();
        desktop.Workspace.ReplaceDocument(WorkspaceDocument.CreateEmpty());
        desktop.ClickButton("打开设置");
        desktop.Window.Close();
        var saved = new WorkspaceStore(desktop.WorkspacePath).Load();
        Assert.Empty(saved.Areas);
        Assert.Equal(applicationId, saved.Library.Single(item => item.DefinitionId == ComponentIds.Settings).Id);
        Assert.Equal(3, desktop.ActionCount);
    }

    [AvaloniaFact]
    public async Task Library_can_create_a_region_from_an_empty_frame()
    {
        using var desktop = new LibraryDesktop();
        desktop.Workspace.ReplaceDocument(WorkspaceDocument.CreateEmpty());
        desktop.OpenLibrary();

        await desktop.DragToWorkspace("设置");
        desktop.ClickButton("返回工作区");

        var area = Assert.Single(desktop.Workspace.Document.Areas);
        Assert.Equal(ComponentIds.Settings, Assert.Single(area.Components).DefinitionId);
    }

    [AvaloniaFact]
    public void Library_search_filters_catalog_and_renders_preview()
    {
        using var desktop = new LibraryDesktop();
        desktop.OpenLibrary();
        var search = desktop.Library.GetVisualDescendants().OfType<TextBox>()
            .Single(box => AutomationProperties.GetName(box) == "搜索组件");
        search.Text = "不存在的组件";
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(desktop.Library.GetVisualDescendants().OfType<ComponentView>());
        search.Text = "设置";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { ComponentIds.Settings, ComponentIds.Instances }, desktop.Library.GetVisualDescendants().OfType<ComponentView>().Select(x => x.Item.DefinitionId));
        search.Text = ComponentIds.Settings;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(ComponentIds.Settings, Assert.Single(desktop.Library.GetVisualDescendants().OfType<ComponentView>()).Item.DefinitionId);
        search.Text = "";
        using var frame = desktop.Library.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/wonder-component-library.png"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        frame.Save(path, PngBitmapEncoderOptions.Default);
    }

    private sealed class LibraryDesktop : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "Wonder.Library.Tests", Guid.NewGuid().ToString("N"));
        public string WorkspacePath => Path.Combine(_directory, "workspace.json");
        public MainWindow Window { get; }
        public WorkspaceView Workspace { get; }
        public Window Library => Assert.Single(Window.OwnedWindows.OfType<ComponentLibraryWindow>());
        public int ActionCount { get; private set; }
        public LibraryDesktop()
        {
            Directory.CreateDirectory(_directory);
            new WorkspaceStore(WorkspacePath).Save(TestWorkspaceFactory.CreateThreeAreas());
            Window = new MainWindow(WorkspacePath);
            Window.Show();
            Dispatcher.UIThread.RunJobs();
            Workspace = Window.GetVisualDescendants().OfType<WorkspaceView>().Single();
            Workspace.SettingsRequested += () => ActionCount++;
        }
        public void OpenLibrary()
        {
            Workspace.ContextMenu = Workspace.CreateWorkspaceMenu();
            Workspace.ContextMenu.Open(Workspace);
            Click(Workspace.ContextMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "组件库…")));
        }
        public void ClickButton(string name)
        {
            Click(Library.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button) == name));
            if (name == "打开设置") CloseSettings();
        }
        public void CloseSettings()
        {
            foreach (var window in Window.OwnedWindows.Where(window => window is not ComponentLibraryWindow).ToArray())
                window.Close();
        }
        public async Task DragToWorkspace(string title)
        {
            var handle = Library.GetVisualDescendants().OfType<Control>()
                .Single(control => AutomationProperties.GetName(control) == "拖动" + title);
            handle.BringIntoView();
            Dispatcher.UIThread.RunJobs();
            var start = handle.TranslatePoint(new Point(handle.Bounds.Width / 2, handle.Bounds.Height / 2), Library)!.Value;
            var target = Workspace.TranslatePoint(new Point(120, 220), Window)!.Value;
            Library.MouseDown(start, MouseButton.Left);
            Library.MouseMove(start + new Vector(10, 0), RawInputModifiers.LeftMouseButton);
            await Task.Yield();
            Window.MouseMove(target, RawInputModifiers.LeftMouseButton);
            Window.MouseUp(target, MouseButton.Left);
            await Task.Yield();
            Dispatcher.UIThread.RunJobs();
        }
        public static void Click(Control control)
        {
            control.BringIntoView();
            Dispatcher.UIThread.RunJobs();
            var top = TopLevel.GetTopLevel(control)!;
            var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), top)!.Value;
            top.MouseDown(point, MouseButton.Left);
            top.MouseUp(point, MouseButton.Left);
        }
        public void Dispose()
        {
            Window.Close();
            Directory.Delete(_directory, recursive: true);
        }
    }
}
