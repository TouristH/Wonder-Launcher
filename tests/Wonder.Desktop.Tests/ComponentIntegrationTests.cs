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
using Wonder.Desktop.Components;
using Wonder.Extensibility.Contracts;
using Wonder.Workspace;
using Xunit;

namespace Wonder.Desktop.Tests;

public sealed class ComponentIntegrationTests
{
    [AvaloniaFact]
    public void Replacing_layout_keeps_distinct_application_identity_and_reset_reuses_settings()
    {
        using var desktop = new Desktop();
        var original = desktop.Workspace.Document.Library.Single(item => item.DefinitionId == ComponentIds.Notes);
        original.State["text"] = "原便笺";
        var incoming = WorkspaceDocument.CreateEmpty();
        var added = incoming.CreateApplication(ComponentIds.Notes);
        added.State["text"] = "另一份独立便笺";
        var shortcut = incoming.CreateShortcut(added);
        incoming.AddArea().Components.Add(shortcut);

        desktop.Workspace.ReplaceDocument(incoming);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(added, shortcut.Application);
        Assert.Equal("另一份独立便笺", shortcut.State["text"]);
        Assert.Equal("原便笺", original.State["text"]);
        Assert.Equal(2, desktop.Workspace.Document.Library.Count(item => item.DefinitionId == ComponentIds.Notes));
        var settingsId = desktop.Workspace.Document.Library.Single(item => item.DefinitionId == ComponentIds.Settings).Id;
        var menu = desktop.Workspace.CreateWorkspaceMenu();
        menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "恢复默认布局"))
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal(settingsId, desktop.Workspace.Document.Areas.Single().Components.Single().ApplicationId);
        Assert.Equal(2, desktop.Workspace.Document.Library.Count(item => item.DefinitionId == ComponentIds.Notes));
    }

    [AvaloniaFact]
    public void Registered_custom_component_appears_in_workspace_and_library_without_type_branches()
    {
        var registry = new ComponentRegistry();
        registry.Register(new ComponentDefinition("test.independent", "独立扩展", "注册即可接入", 310, 130),
            _ => new CustomRuntime(), context => ComponentContent.Text(context, "来自注册表的内容"));
        var document = WorkspaceDocument.CreateEmpty();
        var application = document.CreateApplication("test.independent");
        var shortcut = document.CreateShortcut(application);
        document.AddArea().Components.Add(shortcut);
        var workspace = new WorkspaceView(document, null, registry);
        var window = new Window { Width = 900, Height = 600, Content = workspace };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new Size(310, 130), workspace.GetComponentBounds(shortcut.Id).Size);
            Assert.Contains(workspace.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "来自注册表的内容");
            var library = new ComponentLibraryWindow(workspace, document.Library.ToArray());
            library.Show(window);
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(library.GetVisualDescendants().OfType<ComponentView>(), view => view.Item.Application == application);
            Assert.Contains(library.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "来自注册表的内容");
        }
        finally { window.Close(); workspace.DisposeComponents(); }
    }

    [AvaloniaFact]
    public void Notes_input_is_shared_with_library_autosaved_and_survives_clearing_and_restart()
    {
        using var desktop = new Desktop();
        desktop.OpenLibrary();
        var inputs = desktop.Workspace.GetVisualDescendants().OfType<ComponentView>()
            .Where(view => view.Item.DefinitionId == ComponentIds.Notes)
            .Select(view => view.GetVisualDescendants().OfType<TextBox>().Single()).ToArray();
        var libraryInput = desktop.Library.GetVisualDescendants().OfType<ComponentView>()
            .Single(view => view.Item.DefinitionId == ComponentIds.Notes)
            .GetVisualDescendants().OfType<TextBox>().Single();
        inputs[0].Text = "下次游玩：修建钟楼\n准备材料：石砖、玻璃 🧱";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(inputs[0].Text, inputs[1].Text);
        Assert.Equal(inputs[0].Text, libraryInput.Text);
        SaveFrame(desktop.Window, "notes-workspace");
        var saved = new WorkspaceStore(desktop.Path).Load();
        var notes = saved.Library.Single(item => item.DefinitionId == ComponentIds.Notes);
        Assert.Equal(inputs[0].Text, notes.State["text"]);
        var applicationId = notes.Id;

        desktop.Workspace.ReplaceDocument(WorkspaceDocument.CreateEmpty());
        libraryInput.Text = "清空桌面后仍可编辑";
        Dispatcher.UIThread.RunJobs();
        desktop.Window.Close();
        saved = new WorkspaceStore(desktop.Path).Load();
        Assert.Empty(saved.Areas);
        Assert.Equal("清空桌面后仍可编辑", saved.Library.Single(item => item.Id == applicationId).State["text"]);
        using var reopened = new Desktop(desktop.Path);
        reopened.OpenLibrary();
        Assert.Empty(reopened.Workspace.Document.Areas);
        Assert.Equal("清空桌面后仍可编辑", reopened.Library.GetVisualDescendants().OfType<ComponentView>()
            .Single(view => view.Item.DefinitionId == ComponentIds.Notes).GetVisualDescendants().OfType<TextBox>().Single().Text);
    }

    [AvaloniaFact]
    public async Task Holding_and_selecting_text_does_not_move_a_note_or_create_drag_preview()
    {
        using var desktop = new Desktop();
        var view = desktop.Workspace.GetVisualDescendants().OfType<ComponentView>()
            .First(component => component.Item.DefinitionId == ComponentIds.Notes);
        var input = view.GetVisualDescendants().OfType<TextBox>().Single();
        input.Text = "可以长按和选择这些文字，不移动组件";
        Dispatcher.UIThread.RunJobs();
        var before = desktop.Workspace.GetComponentBounds(view.Item.Id);
        var point = input.TranslatePoint(new Point(25, 25), desktop.Window)!.Value;
        desktop.Window.MouseDown(point, MouseButton.Left);
        await Task.Delay(550);
        desktop.Window.MouseMove(point + new Vector(80, 5), RawInputModifiers.LeftMouseButton);
        desktop.Window.MouseUp(point + new Vector(80, 5), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(before, desktop.Workspace.GetComponentBounds(view.Item.Id));
        Assert.Empty(desktop.Workspace.Children.OfType<ComponentView>());
        Assert.Equal("可以长按和选择这些文字，不移动组件", input.Text);
    }

    [AvaloniaFact]
    public void Six_registered_components_render_in_library_and_data_path_is_real()
    {
        using var desktop = new Desktop();
        desktop.OpenLibrary();
        // Give the catalogue enough height to inspect all six natural-sized views.
        desktop.Library.Height = 1500;
        Dispatcher.UIThread.RunJobs();
        var components = desktop.Library.GetVisualDescendants().OfType<ComponentView>().ToArray();
        Assert.Equal(new[] { ComponentIds.Settings, ComponentIds.Notes, ComponentIds.LauncherStatus, ComponentIds.Instances, ComponentIds.Launch, ComponentIds.Accounts },
            components.Select(view => view.Item.DefinitionId));
        var status = components.Single(view => view.Item.DefinitionId == ComponentIds.LauncherStatus);
        Assert.Contains(status.GetVisualDescendants().OfType<TextBlock>(),
            block => block.Text?.Contains(System.IO.Path.GetDirectoryName(desktop.Path)!, StringComparison.Ordinal) == true);
        SaveFrame(desktop.Library, "library");
    }

    private static void SaveFrame(Window window, string name)
    {
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var target = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, $"../../../../../artifacts/design-0.5.0/{name}.png"));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
        frame.Save(target, PngBitmapEncoderOptions.Default);
    }

    private sealed class CustomRuntime : IComponentRuntime
    {
        public IReadOnlyDictionary<string, string> Values { get; } = new Dictionary<string, string>();
        public event EventHandler? Changed { add { } remove { } }
        public void Resume() { }
        public void Suspend() { }
        public void Invoke(string actionId, string? value) { }
        public void Dispose() { }
    }

    private sealed class Desktop : IDisposable
    {
        private readonly string? _ownedDirectory;
        public string Path { get; }
        public MainWindow Window { get; }
        public WorkspaceView Workspace { get; }
        public Window Library => Window.OwnedWindows.OfType<ComponentLibraryWindow>().Single();

        public Desktop(string? existingPath = null)
        {
            if (existingPath is null)
            {
                _ownedDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Wonder.ComponentIntegration", Guid.NewGuid().ToString("N"));
                Path = System.IO.Path.Combine(_ownedDirectory, "workspace.json");
                var document = WorkspaceDocument.CreateEmpty();
                document.EnsureLibrary([ComponentIds.Settings, ComponentIds.Notes, ComponentIds.LauncherStatus]);
                var application = document.Library.Single(item => item.DefinitionId == ComponentIds.Notes);
                var area = document.AddArea();
                var first = document.CreateShortcut(application);
                var second = document.CreateShortcut(application);
                first.X = -170;
                second.X = 170;
                area.Components.AddRange([first, second]);
                new WorkspaceStore(Path).Save(document);
            }
            else Path = existingPath;
            Window = new MainWindow(Path);
            Window.Show();
            Dispatcher.UIThread.RunJobs();
            Workspace = Window.GetVisualDescendants().OfType<WorkspaceView>().Single();
        }

        public void OpenLibrary()
        {
            var menu = Workspace.CreateWorkspaceMenu();
            menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "组件库…"))
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
            Dispatcher.UIThread.RunJobs();
        }

        public void Dispose()
        {
            Window.Close();
            if (_ownedDirectory is not null && Directory.Exists(_ownedDirectory)) Directory.Delete(_ownedDirectory, true);
        }
    }
}
