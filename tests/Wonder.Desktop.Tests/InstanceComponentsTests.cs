// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Wonder.Application;
using Wonder.Core;
using Wonder.Desktop.Components;
using Wonder.Infrastructure;
using Wonder.Workspace;
using Xunit;

namespace Wonder.Desktop.Tests;

public sealed class InstanceComponentsTests
{
    [AvaloniaFact]
    public async Task Launch_status_window_is_shared_and_closing_it_does_not_stop_the_game()
    {
        using var scene = new Scene(); await scene.Service.ImportAsync(scene.Directory);
        Assert.DoesNotContain(scene.Window.GetVisualDescendants().OfType<Button>(), b => AutomationProperties.GetName(b) == "从启动组件选择实例");
        Click(scene.Window, "查看启动状态");
        var status = Assert.Single(scene.Window.OwnedWindows.OfType<LaunchStatusWindow>());
        Click(scene.Window, "查看启动状态"); Assert.Same(status, Assert.Single(scene.Window.OwnedWindows.OfType<LaunchStatusWindow>()));
        Click(scene.Window, "离线启动游戏");
        Assert.Contains("游戏日志示例", Box(status, "最近启动日志").Text);
        status.Close(); Assert.True(scene.Service.IsBusy);
        scene.Launcher.Exit.SetResult(0);
        for (var i = 0; i < 30 && scene.Service.IsBusy; i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
        Click(scene.Window, "查看启动状态"); status = Assert.Single(scene.Window.OwnedWindows.OfType<LaunchStatusWindow>());
        Assert.Contains(status.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("已退出") == true);
        SaveFrame(status, "launch-status"); status.Close();
    }

    [AvaloniaFact]
    public async Task Manager_import_save_selection_and_launch_component_share_one_service()
    {
        using var scene = new Scene();
        var launcher = scene.Launcher;
        var manager = new InstanceManagerWindow(scene.Service, () => Task.FromResult<string?>(scene.Directory));
        manager.Show(scene.Window);
        Click(manager, "选择实例目录");
        Dispatcher.UIThread.RunJobs();
        Assert.Single(scene.Service.Catalog.Instances);
        SelectInstance(manager, 0);
        Box(manager, "实例名称").Text = "中文生存实例";
        Box(manager, "最大内存 MiB").Text = "6144";
        Click(manager, "保存实例设置");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("中文生存实例", scene.Service.SelectedInstance!.Name);
        Assert.Equal(6144, scene.Service.SelectedInstance.EffectiveSettings.MaximumMemoryMb);
        Assert.Contains(scene.Window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "中文生存实例");
        Assert.DoesNotContain(manager.GetVisualDescendants().OfType<Button>(), b => AutomationProperties.GetName(b) == "管理窗口离线启动");
        scene.Service.SetOfflineUsername("Wonder_Player");
        Assert.Equal("Wonder_Player", scene.Service.Catalog.OfflineUsername);
        SaveFrame(manager, "instance-manager");
        SaveFrame(scene.Window, "game-components");
        Click(scene.Window, "离线启动游戏");
        Click(scene.Window, "离线启动游戏");
        Assert.Equal(1, launcher.Starts);
        Assert.Equal(LaunchPhase.Running, scene.Service.LaunchPhase);
        scene.Window.Hide(); scene.Workspace.SetRuntimeVisible(false); manager.Close();
        Assert.True(scene.Service.IsBusy);
        launcher.Exit.SetResult(0);
        for (var i = 0; i < 30 && scene.Service.IsBusy; i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
        Assert.Equal(LaunchPhase.Exited, scene.Service.LaunchPhase);
        scene.Window.Show(); scene.Workspace.SetRuntimeVisible(true); Dispatcher.UIThread.RunJobs();
        Assert.Contains(scene.Window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("已退出") == true);
        Assert.True(scene.Window.GetVisualDescendants().OfType<Button>().Single(x => AutomationProperties.GetName(x) == "离线启动游戏").IsEnabled);
        var restored = new InstanceStore(Path.Combine(scene.Directory, "instances.json")).Load();
        Assert.Equal(6144, Assert.Single(restored.Instances).Overrides.MaximumMemoryMb);
        Assert.Equal("Wonder_Player", restored.OfflineUsername);
    }

    [AvaloniaFact]
    public void Invalid_override_keeps_saved_values_and_reset_restores_imported_values()
    {
        using var scene = new Scene(); scene.Service.ImportAsync(scene.Directory).GetAwaiter().GetResult();
        var manager = new InstanceManagerWindow(scene.Service);
        manager.Show(scene.Window); Dispatcher.UIThread.RunJobs();
        SelectInstance(manager, 0);
        Box(manager, "最小内存 MiB").Text = "8192";
        Box(manager, "最大内存 MiB").Text = "1024";
        Click(manager, "保存实例设置");
        Assert.Null(scene.Service.SelectedInstance!.Overrides.MaximumMemoryMb);
        Assert.Contains(manager.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("最小内存") == true);
        Box(manager, "最小内存 MiB").Text = "512";
        Box(manager, "最大内存 MiB").Text = "8192";
        Click(manager, "保存实例设置");
        Click(manager, "恢复来源设置");
        Assert.Equal(3072, scene.Service.SelectedInstance!.EffectiveSettings.MaximumMemoryMb);
        Assert.Null(scene.Service.SelectedInstance.Overrides.MaximumMemoryMb);
        manager.Close();
    }

    [AvaloniaFact]
    public void Browser_groups_roots_and_opens_details_only_after_child_selection_preserving_drafts()
    {
        using var scene = new Scene();
        var rootA = Path.Combine(scene.Directory, "游戏目录 A");
        var rootB = Path.Combine(scene.Directory, "游戏目录 B");
        scene.Service.ImportAsync(Path.Combine(rootA, "version-one")).GetAwaiter().GetResult();
        scene.Service.ImportAsync(Path.Combine(rootA, "version-two")).GetAwaiter().GetResult();
        scene.Service.ImportAsync(Path.Combine(rootB, "version-three")).GetAwaiter().GetResult();
        var selectedBeforeBrowsing = scene.Service.Catalog.SelectedInstanceId;
        var manager = new InstanceManagerWindow(scene.Service); manager.Show(scene.Window); Dispatcher.UIThread.RunJobs();
        Assert.False(HasVisibleBox(manager, "实例名称"));
        Assert.Equal(2, Cards(manager, "游戏根目录列表").Length);
        Assert.Equal(2, Cards(manager, "目录中的实例").Length);
        Activate(Cards(manager, "游戏根目录列表")[1]);
        Assert.Single(Cards(manager, "目录中的实例"));
        Assert.Equal(selectedBeforeBrowsing, scene.Service.Catalog.SelectedInstanceId);
        Assert.False(HasVisibleBox(manager, "实例名称"));
        Activate(Cards(manager, "游戏根目录列表")[0]);
        Assert.Contains(Cards(manager, "目录中的实例")[0].GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Minecraft 1.20.1 · Forge 47.3.0");
        SaveFrame(manager, "instance-browser");
        SelectInstance(manager, 0);
        Assert.True(Box(manager, "实例名称").IsEffectivelyVisible);
        Box(manager, "实例名称").Text = "尚未保存的自定义名称";
        Box(manager, "最大内存 MiB").Text = "6144";
        Click(manager, "返回游戏目录");
        Assert.Contains("selected", Cards(manager, "游戏根目录列表")[0].Classes);
        Assert.False(HasVisibleBox(manager, "实例名称"));
        SelectInstance(manager, 1);
        Assert.Equal("生存世界", Box(manager, "实例名称").Text);
        Click(manager, "返回游戏目录");
        SelectInstance(manager, 0);
        Assert.Equal("尚未保存的自定义名称", Box(manager, "实例名称").Text);
        Assert.Equal("6144", Box(manager, "最大内存 MiB").Text);
        Click(manager, "保存实例设置");
        Assert.Equal("尚未保存的自定义名称", scene.Service.SelectedInstance!.Name);
        Click(manager, "返回游戏目录");
        Assert.Contains(manager.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "尚未保存的自定义名称");
        SelectInstance(manager, 0);
        scene.Service.Remove((string)Cards(manager, "目录中的实例")[0].Tag!);
        Assert.False(HasVisibleBox(manager, "实例名称"));
        Assert.Single(Cards(manager, "目录中的实例"));
        SelectInstance(manager, 0);
        scene.Service.Remove((string)Cards(manager, "目录中的实例")[0].Tag!);
        Assert.Equal(2, Cards(manager, "游戏根目录列表").Length);
        Assert.Empty(Cards(manager, "目录中的实例"));
        Activate(Cards(manager, "游戏根目录列表")[1].SelectButton);
        Assert.Single(Cards(manager, "目录中的实例"));
        manager.Width = 800; manager.Height = 580;
        SaveFrame(manager, "instance-browser-compact");
        manager.Close();
    }

    [AvaloniaFact]
    public void Instance_cards_reflow_and_accept_mouse_and_keyboard_activation()
    {
        using var scene = new Scene();
        var root = Path.Combine(scene.Directory, "游戏目录");
        scene.Service.ImportAsync(Path.Combine(root, "version-one")).GetAwaiter().GetResult();
        scene.Service.ImportAsync(Path.Combine(root, "version-two")).GetAwaiter().GetResult();
        var manager = new InstanceManagerWindow(scene.Service); manager.Show(scene.Window); Dispatcher.UIThread.RunJobs();
        var cards = Cards(manager, "目录中的实例");
        Assert.InRange(cards[0].Bounds.Width, 180, 260);
        Assert.Equal(cards[0].Bounds.Y, cards[1].Bounds.Y);
        Assert.True(cards[1].Bounds.X > cards[0].Bounds.X);
        manager.Width = 800; Dispatcher.UIThread.RunJobs();
        Assert.InRange(cards[0].Bounds.Width, 180, 260);
        // The blank area below the right-side action buttons is also part of the selection surface.
        var point = cards[1].TranslatePoint(new Point(cards[1].Bounds.Width - 15, cards[1].Bounds.Height - 15), manager)!.Value;
        manager.MouseDown(point, MouseButton.Left); manager.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
        Assert.False(HasVisibleBox(manager, "实例名称"));
        Assert.EndsWith("version-two", scene.Service.SelectedInstance!.SourcePath);
        Assert.True(cards[0].SelectButton.Focus());
        manager.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); manager.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
        Assert.False(HasVisibleBox(manager, "实例名称"));
        Assert.EndsWith("version-one", scene.Service.SelectedInstance!.SourcePath);
        Assert.True(cards[0].Selected);
        Assert.False(cards[1].Selected);
        Activate(cards[1].SettingsButton);
        Assert.True(HasVisibleBox(manager, "实例名称"));
        Assert.EndsWith("version-one", scene.Service.SelectedInstance!.SourcePath);
        Box(manager, "实例名称").Text = "只修改第二个实例"; Click(manager, "保存实例设置");
        Assert.Equal("生存世界", scene.Service.SelectedInstance.Name);
        Assert.Equal("只修改第二个实例", scene.Service.Catalog.Instances.Single(x => x.SourcePath.EndsWith("version-two")).Name);
        manager.Close();
    }

    [AvaloniaFact]
    public void Layout_switch_reflows_cards_without_changing_selection_or_losing_instance_draft()
    {
        using var scene = new Scene();
        var root = Path.Combine(scene.Directory, "游戏目录");
        scene.Service.ImportAsync(Path.Combine(root, "version-one")).GetAwaiter().GetResult();
        scene.Service.ImportAsync(Path.Combine(root, "version-two")).GetAwaiter().GetResult();
        var choices = new List<InstanceLayout>();
        var manager = new InstanceManagerWindow(scene.Service, layoutChanged: choices.Add);
        manager.Show(scene.Window); Dispatcher.UIThread.RunJobs();
        SelectInstance(manager, 0);
        Box(manager, "实例名称").Text = "长条视图中的未保存草稿";
        Click(manager, "返回游戏目录");
        var selected = scene.Service.Catalog.SelectedInstanceId;
        var listButton = manager.GetVisualDescendants().OfType<ToggleButton>().Single(b => AutomationProperties.GetName(b) == "实例列表视图");
        Assert.True(listButton.Focus());
        manager.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); manager.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
        Assert.True(listButton.IsChecked);
        var cards = Cards(manager, "目录中的实例");
        Assert.Equal(cards[0].Bounds.X, cards[1].Bounds.X);
        Assert.True(cards[1].Bounds.Y > cards[0].Bounds.Y);
        Assert.True(cards[0].Bounds.Width > cards[0].Bounds.Height * 2);
        Assert.Equal(selected, scene.Service.Catalog.SelectedInstanceId);
        SaveFrame(manager, "instance-list");
        manager.Width = 800; manager.Height = 580; Dispatcher.UIThread.RunJobs();
        Assert.True(cards[0].Bounds.Width > cards[0].Bounds.Height * 2);
        SaveFrame(manager, "instance-list-small");
        Click(manager, "实例网格视图");
        Assert.InRange(cards[0].Bounds.Width, 180, 260);
        manager.Width = 1050; manager.Height = 820; Dispatcher.UIThread.RunJobs();
        Assert.Equal(cards[0].Bounds.Y, cards[1].Bounds.Y);
        Assert.InRange(cards[0].Bounds.Width, 180, 260);
        Click(manager, "实例网格视图"); // Clicking the active mode keeps it selected.
        Assert.Equal(new[] { InstanceLayout.List, InstanceLayout.Grid }, choices);
        SaveFrame(manager, "instance-grid");
        SelectInstance(manager, 0);
        Assert.Equal("长条视图中的未保存草稿", Box(manager, "实例名称").Text);
        manager.Close();
    }

    [AvaloniaFact]
    public void Layout_preference_survives_reopening_restart_and_changes_from_an_already_open_settings_window()
    {
        using var scene = new Scene();
        var document = WorkspaceDocument.CreateEmpty(); document.EnsureLibrary([ComponentIds.Instances]);
        document.AddArea().Components.Add(document.CreateShortcut(document.Library.Single(x => x.DefinitionId == ComponentIds.Instances)));
        var workspacePath = Path.Combine(scene.Directory, "workspace.json");
        new WorkspaceStore(workspacePath).Save(document);
        var settingsStore = new LauncherSettingsStore(Path.Combine(scene.Directory, "settings.json"));
        var main = new MainWindow(workspacePath);
        try
        {
            main.Show(); Dispatcher.UIThread.RunJobs();
            main.GetVisualDescendants().OfType<WorkspaceView>().Single().OpenSettings(); Dispatcher.UIThread.RunJobs();
            var settings = main.OwnedWindows.OfType<SettingsWindow>().Single();
            Click(main, "打开实例管理");
            var manager = main.OwnedWindows.OfType<InstanceManagerWindow>().Single();
            Click(manager, "实例列表视图");
            Assert.Equal(InstanceLayout.List, settingsStore.Load().InstanceLayout);
            Click(settings, "个性化设置");
            settings.GetVisualDescendants().OfType<ComboBox>().Single(x => AutomationProperties.GetName(x) == "强调色").SelectedIndex = (int)AccentColor.Blue;
            Assert.Equal(InstanceLayout.List, settingsStore.Load().InstanceLayout);
            manager.Close(); Click(main, "打开实例管理");
            manager = main.OwnedWindows.OfType<InstanceManagerWindow>().Single();
            Assert.True(manager.GetVisualDescendants().OfType<ToggleButton>().Single(b => AutomationProperties.GetName(b) == "实例列表视图").IsChecked);
            main.Close(); main = new MainWindow(workspacePath); main.Show(); Dispatcher.UIThread.RunJobs();
            Click(main, "打开实例管理"); manager = main.OwnedWindows.OfType<InstanceManagerWindow>().Single();
            Assert.True(manager.GetVisualDescendants().OfType<ToggleButton>().Single(b => AutomationProperties.GetName(b) == "实例列表视图").IsChecked);
        }
        finally { main.Close(); Palette.Apply(false, AccentColor.Mint); }
    }

    private static InstanceBrowserCard[] Cards(Window window, string name) => window.GetVisualDescendants().OfType<Panel>().Single(x => AutomationProperties.GetName(x) == name).Children.OfType<InstanceBrowserCard>().ToArray();
    private static bool HasVisibleBox(Window window, string name) => window.GetVisualDescendants().OfType<TextBox>().Any(x => AutomationProperties.GetName(x) == name && x.IsEffectivelyVisible);
    private static void SelectInstance(Window window, int index) => Activate(Cards(window, "目录中的实例")[index].SettingsButton);
    private static void Activate(InstanceBrowserCard card) => Activate(card.SelectButton);
    private static void Activate(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }

    private static TextBox Box(Window window, string name) => window.GetVisualDescendants().OfType<TextBox>().Single(x => AutomationProperties.GetName(x) == name);
    private static void Click(Window window, string name)
    {
        window.GetVisualDescendants().OfType<Button>().Single(x => AutomationProperties.GetName(x) == name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }
    private static void SaveFrame(Window window, string name)
    {
        Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, $"../../../../../artifacts/design-0.5.1/{name}.png"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); frame.Save(path, PngBitmapEncoderOptions.Default);
    }
    private sealed class Scene : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "Wonder.InstanceUi", Guid.NewGuid().ToString("N"));
        public InstanceService Service { get; }
        public FakeLauncher Launcher { get; } = new();
        public Window Window { get; }
        private readonly WorkspaceView _workspace;
        public WorkspaceView Workspace => _workspace;
        public Scene()
        {
            System.IO.Directory.CreateDirectory(Directory);
            Service = new(new InstanceStore(Path.Combine(Directory, "instances.json")), new Discovery(), Launcher, Path.Combine(Directory, "cache"));
            var registry = new ComponentRegistry(); GameComponents.Register(registry, Service, () => { });
            var document = WorkspaceDocument.CreateEmpty(); document.EnsureLibrary([ComponentIds.Instances, ComponentIds.Launch]);
            var area = document.AddArea();
            foreach (var id in new[] { ComponentIds.Instances, ComponentIds.Launch })
            {
                var shortcut = document.CreateShortcut(document.Library.Single(x => x.DefinitionId == id));
                shortcut.X = id == ComponentIds.Instances ? -180 : 180; area.Components.Add(shortcut);
            }
            _workspace = new(document, Directory, registry); Window = new Window { Width = 1000, Height = 600, Content = _workspace };
            Window.Show(); Dispatcher.UIThread.RunJobs();
        }
        public void Dispose() { Window.Close(); _workspace.DisposeComponents(); System.IO.Directory.Delete(Directory, true); }
        private sealed class Discovery : IInstanceDiscovery
        {
            public Task<DiscoveryResult> DiscoverAsync(string path, CancellationToken ct = default) => Task.FromResult(new DiscoveryResult([new()
            {
                Id = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(path)))[..32], Name = "生存世界", VersionId = "自定义版本目录", GameVersion = "1.20.1", SourcePath = path,
                ModLoaders = [new("Forge", "47.3.0")],
                RootDirectory = Path.GetFileName(path).StartsWith("version-") ? Path.GetDirectoryName(path)! : path, DefaultGameDirectory = path,
                LibraryDirectory = path, AssetsDirectory = path, VersionsDirectory = path, ImportedSettings = new() { MaximumMemoryMb = 3072 }, ConfigSource = "HMCL · 示例来源", Source = InstanceSource.Hmcl
            }], []));
        }
    }
    private sealed class FakeLauncher : IGameLauncher
    {
        public int Starts;
        public TaskCompletionSource<int> Exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<LaunchPlan> BuildPlanAsync(LaunchRequest request, CancellationToken ct = default) => Task.FromResult(new LaunchPlan("java", request.Instance.DefaultGameDirectory, ["Main"], request.Instance.Id));
        public Task<int> RunAsync(LaunchPlan plan, Action started, Action<string> output, CancellationToken ct = default) { Starts++; started(); output("游戏日志示例"); return Exit.Task; }
    }
}
