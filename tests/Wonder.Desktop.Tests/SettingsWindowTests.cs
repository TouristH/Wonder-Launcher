// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Wonder.Workspace;
using Wonder.Infrastructure;
using Xunit;

namespace Wonder.Desktop.Tests;

public sealed class SettingsWindowTests
{
    [AvaloniaFact]
    public void Offline_username_saves_independently_and_invalid_input_preserves_draft()
    {
        using var host = new SettingsDesktop(); var settings = host.Open();
        Click(Raw<Button>(settings, "游戏全局设置"));
        Raw<TextBox>(settings, "全局最大内存").Text = "6144";
        var username = Raw<TextBox>(settings, "离线用户名"); username.Text = "x";
        Click(Raw<Button>(settings, "保存离线用户名"));
        Assert.Equal("Player", host.GameStore.Load().OfflineUsername); Assert.Equal("x", username.Text);
        username.Text = "Wonder_Player"; Click(Raw<Button>(settings, "保存离线用户名"));
        Assert.Equal("Wonder_Player", host.GameStore.Load().OfflineUsername);
        Assert.Null(host.GameStore.Load().GlobalSettings.MaximumMemoryMb);
        Assert.Equal("6144", Raw<TextBox>(settings, "全局最大内存").Text);
        host.Restart(); settings = host.Open(); Click(Raw<Button>(settings, "游戏全局设置"));
        Assert.Equal("Wonder_Player", Raw<TextBox>(settings, "离线用户名").Text);
    }

    [AvaloniaFact]
    public void Categories_switch_visible_pages_and_global_draft_persists_until_save()
    {
        using var host = new SettingsDesktop();
        var settings = host.Open();
        Assert.True(Raw<Slider>(settings, "长按拖动时间").IsEffectivelyVisible);
        Assert.False(Raw<ComboBox>(settings, "主题").IsEffectivelyVisible);
        Click(Raw<Button>(settings, "游戏全局设置"));
        Assert.False(Raw<Slider>(settings, "长按拖动时间").IsEffectivelyVisible);
        var memory = Raw<TextBox>(settings, "全局最大内存");
        Assert.True(memory.IsEffectivelyVisible);
        memory.Text = "6144";
        Click(Raw<Button>(settings, "个性化设置"));
        Assert.True(Raw<ComboBox>(settings, "主题").IsEffectivelyVisible);
        Assert.False(memory.IsEffectivelyVisible);
        var gameCard = Raw<Button>(settings, "游戏全局设置");
        Assert.True(gameCard.Focus());
        settings.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        settings.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("6144", memory.Text);
        Assert.True(memory.IsEffectivelyVisible);
        Assert.Null(host.GameStore.Load().GlobalSettings.MaximumMemoryMb);
        Click(Raw<Button>(settings, "保存全局设置"));
        Assert.Equal(6144, host.GameStore.Load().GlobalSettings.MaximumMemoryMb);
        host.Restart(); settings = host.Open(); Click(Raw<Button>(settings, "游戏全局设置"));
        Assert.Equal("6144", Raw<TextBox>(settings, "全局最大内存").Text);
        Raw<TextBox>(settings, "全局最小内存").Text = "9000";
        Click(Raw<Button>(settings, "保存全局设置"));
        Assert.Null(host.GameStore.Load().GlobalSettings.MinimumMemoryMb);
        Assert.Equal("9000", Raw<TextBox>(settings, "全局最小内存").Text);
        Assert.Contains(settings.GetVisualDescendants().OfType<TextBlock>(), t => t.Name == "GlobalSettingsStatus" && t.Text!.Contains("最小内存"));
        Click(Raw<Button>(settings, "恢复全局继承设置"));
        Assert.Equal(new Wonder.Core.InstanceSettings(), host.GameStore.Load().GlobalSettings);
        Assert.Equal("", Raw<TextBox>(settings, "全局最大内存").Text);
    }

    [AvaloniaFact]
    public void Failed_global_save_preserves_draft_and_launcher_reset_leaves_global_settings_intact()
    {
        using var host = new SettingsDesktop();
        var settings = host.Open(); Click(Raw<Button>(settings, "游戏全局设置"));
        Directory.CreateDirectory(host.InstancesPath);
        Raw<TextBox>(settings, "全局最大内存").Text = "6144";
        Click(Raw<Button>(settings, "保存全局设置"));
        Assert.Equal("6144", Raw<TextBox>(settings, "全局最大内存").Text);
        Assert.Contains(settings.GetVisualDescendants().OfType<TextBlock>(), t => t.Name == "GlobalSettingsStatus" && t.Text!.Contains("修改未保存"));
        Directory.Delete(host.InstancesPath);
        Click(Raw<Button>(settings, "保存全局设置"));
        Assert.Equal(6144, host.GameStore.Load().GlobalSettings.MaximumMemoryMb);
        Click(Raw<Button>(settings, "个性化设置"));
        Raw<ComboBox>(settings, "主题").SelectedIndex = (int)LauncherTheme.Light;
        Click(Raw<Button>(settings, "恢复默认设置"));
        Click(Raw<Button>(settings, "确认恢复默认设置"));
        Assert.Equal(6144, host.GameStore.Load().GlobalSettings.MaximumMemoryMb);
    }

    [AvaloniaFact]
    public void Global_setting_read_failure_blocks_writes_and_recovers_after_repair()
    {
        using var host = new SettingsDesktop();
        File.WriteAllText(host.InstancesPath, "{broken"); host.Restart();
        var settings = host.Open(); Click(Raw<Button>(settings, "游戏全局设置"));
        Assert.False(Raw<TextBox>(settings, "全局最大内存").IsEffectivelyEnabled);
        Assert.Equal("{broken", File.ReadAllText(host.InstancesPath));
        File.WriteAllText(host.InstancesPath, "{\"version\":1,\"globalSettings\":{\"maximumMemoryMb\":7168}}");
        Click(Raw<Button>(settings, "重试全局设置读取"));
        Assert.True(Raw<TextBox>(settings, "全局最大内存").IsEffectivelyEnabled);
        Assert.Equal("7168", Raw<TextBox>(settings, "全局最大内存").Text);
    }

    [AvaloniaFact]
    public void Area_numbers_are_opt_in_apply_immediately_and_survive_restart()
    {
        using var host = new SettingsDesktop();
        var settings = host.Open();
        var toggle = host.Find<ToggleSwitch>(settings, "显示功能区序号");
        var area = host.View.Document.Areas.Single();
        var title = host.View.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Text == area.Title);
        var titleStart = title.TranslatePoint(default, host.View)!.Value.X;
        Assert.False(toggle.IsChecked);
        Assert.DoesNotContain(host.View.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "01" && block.IsEffectivelyVisible);

        Click(toggle);

        Assert.True(toggle.IsChecked);
        Assert.Single(host.View.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "01" && block.IsEffectivelyVisible);
        Assert.True(title.TranslatePoint(default, host.View)!.Value.X > titleStart);
        Assert.False(File.Exists(host.WorkspacePath));
        host.Restart();
        settings = host.Open();
        toggle = host.Find<ToggleSwitch>(settings, "显示功能区序号");
        Assert.True(toggle.IsChecked);
        Assert.Single(host.View.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "01" && block.IsEffectivelyVisible);

        Click(toggle);

        title = host.View.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Text == area.Title);
        Assert.Equal(titleStart, title.TranslatePoint(default, host.View)!.Value.X);
        Assert.DoesNotContain(host.View.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "01" && block.IsEffectivelyVisible);
        host.Restart();
        Assert.False(host.Find<ToggleSwitch>(host.Open(), "显示功能区序号").IsChecked);
    }

    [AvaloniaFact]
    public void Shortcut_and_library_open_same_window_without_writing_settings_or_layout()
    {
        using var host = new SettingsDesktop();
        Click(host.Find<Button>(host.Main, "打开设置"));
        var settings = Assert.Single(host.Main.OwnedWindows.OfType<SettingsWindow>());
        Assert.False(File.Exists(host.Path));
        Assert.False(File.Exists(host.WorkspacePath));
        var libraryItem = host.View.CreateWorkspaceMenu().Items.OfType<MenuItem>().Single(item => Equals(item.Header, "组件库…"));
        libraryItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        var library = Assert.Single(host.Main.OwnedWindows.OfType<ComponentLibraryWindow>());
        Click(host.Find<Button>(library, "打开设置"));
        Assert.Same(settings, Assert.Single(host.Main.OwnedWindows.OfType<SettingsWindow>()));
        Assert.False(File.Exists(host.Path));
        Assert.False(File.Exists(host.WorkspacePath));
    }

    [AvaloniaFact]
    public void Theme_accent_and_padding_apply_to_open_windows_and_survive_restart()
    {
        using var host = new SettingsDesktop();
        var settings = host.Open();
        var originalBackground = ((ISolidColorBrush)host.Main.Background!).Color;
        host.Find<ComboBox>(settings, "主题").SelectedIndex = (int)LauncherTheme.Light;
        host.Find<ComboBox>(settings, "强调色").SelectedIndex = (int)AccentColor.Rose;
        host.Find<Slider>(settings, "工作区留白").Value = 36;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(ThemeVariant.Light, host.Main.ActualThemeVariant);
        Assert.Equal(ThemeVariant.Light, settings.ActualThemeVariant);
        Assert.NotEqual(originalBackground, ((ISolidColorBrush)host.Main.Background!).Color);
        Assert.Equal(Palette.AccentFor(AccentColor.Rose, true), Palette.Accent.Color);
        Assert.Equal(new Thickness(36), host.View.Margin);
        var saved = new LauncherSettingsStore(host.Path).Load();
        Assert.Equal(LauncherTheme.Light, saved.Theme);
        Assert.Equal(AccentColor.Rose, saved.Accent);
        Assert.Equal(36, saved.WorkspacePadding);
        host.Restart();
        Assert.Equal(ThemeVariant.Light, host.Main.ActualThemeVariant);
        Assert.Equal(new Thickness(36), host.View.Margin);
        Assert.Equal((int)LauncherTheme.Light, host.Find<ComboBox>(host.Open(), "主题").SelectedIndex);
    }

    [AvaloniaFact]
    public void System_theme_clears_explicit_override_and_slider_keyboard_changes_are_saved()
    {
        using var host = new SettingsDesktop();
        var settings = host.Open();
        host.Find<ComboBox>(settings, "主题").SelectedIndex = (int)LauncherTheme.System;
        Assert.Equal(ThemeVariant.Default, Avalonia.Application.Current!.RequestedThemeVariant);
        var slider = host.Find<Slider>(settings, "工作区留白");
        slider.BringIntoView();
        Assert.True(slider.Focus());
        settings.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        settings.KeyReleaseQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        Assert.Equal(24, new LauncherSettingsStore(host.Path).Load().WorkspacePadding);
        Assert.Equal(new Thickness(24), host.View.Margin);
    }

    [AvaloniaFact]
    public void Unreadable_settings_stay_untouched_until_explicit_reset_confirmation()
    {
        using var host = new SettingsDesktop("{broken");
        var settings = host.Open();
        Assert.False(host.Find<ComboBox>(settings, "主题").IsEffectivelyEnabled);
        Assert.Contains("原文件已保留", Status(settings));
        Assert.Equal("{broken", File.ReadAllText(host.Path));
        Click(host.Find<Button>(settings, "恢复默认设置"));
        Assert.Equal("{broken", File.ReadAllText(host.Path));
        Click(host.Find<Button>(settings, "确认恢复默认设置"));
        Assert.Equal(new LauncherSettings(), new LauncherSettingsStore(host.Path).Load());
        Assert.True(host.Find<ComboBox>(settings, "主题").IsEffectivelyEnabled);
        Assert.Single(host.View.Document.Areas.Single().Components);
    }

    [AvaloniaFact]
    public void Read_failure_can_retry_after_file_is_repaired()
    {
        using var host = new SettingsDesktop("{broken");
        var settings = host.Open();
        new LauncherSettingsStore(host.Path).Save(new LauncherSettings { Theme = LauncherTheme.Light, WorkspacePadding = 32 });
        Click(host.Find<Button>(settings, "重试设置存储"));
        Assert.True(host.Find<ComboBox>(settings, "主题").IsEffectivelyEnabled);
        Assert.Equal(ThemeVariant.Light, settings.ActualThemeVariant);
        Assert.Equal(new Thickness(32), host.View.Margin);
        Assert.DoesNotContain("失败", Status(settings));
    }

    [AvaloniaFact]
    public void Confirmed_reset_retries_writing_after_a_readonly_corrupt_file_becomes_writable()
    {
        using var host = new SettingsDesktop("{broken");
        var settings = host.Open();
        File.SetAttributes(host.Path, FileAttributes.ReadOnly);
        try
        {
            Click(host.Find<Button>(settings, "恢复默认设置"));
            Click(host.Find<Button>(settings, "确认恢复默认设置"));
            Assert.Equal("{broken", File.ReadAllText(host.Path));
        }
        finally { File.SetAttributes(host.Path, FileAttributes.Normal); }
        Click(host.Find<Button>(settings, "重试设置存储"));
        Assert.Equal(new LauncherSettings(), new LauncherSettingsStore(host.Path).Load());
        Assert.True(host.Find<ComboBox>(settings, "主题").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void Save_failure_reports_unsaved_changes_and_retry_persists_same_values()
    {
        using var host = new SettingsDesktop();
        var settings = host.Open();
        Directory.CreateDirectory(host.Path);
        host.Find<Slider>(settings, "工作区留白").Value = 30;
        Assert.Equal(new Thickness(30), host.View.Margin);
        Assert.Contains("尚未保存", Status(settings));
        Assert.Empty(Directory.GetFiles(System.IO.Path.GetDirectoryName(host.Path)!, "*.tmp"));
        Directory.Delete(host.Path);
        Click(host.Find<Button>(settings, "重试设置存储"));
        Assert.Equal(30, new LauncherSettingsStore(host.Path).Load().WorkspacePadding);
        Assert.DoesNotContain("尚未保存", Status(settings));
    }

    [AvaloniaFact]
    public void Reset_can_be_cancelled_and_confirmed_without_changing_workspace()
    {
        using var host = new SettingsDesktop();
        var settings = host.Open();
        host.Find<Slider>(settings, "工作区留白").Value = 40;
        Click(host.Find<ToggleSwitch>(settings, "显示功能区序号"));
        var areaId = host.View.Document.Areas.Single().Id;
        Click(host.Find<Button>(settings, "恢复默认设置"));
        settings.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        settings.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.True(settings.IsVisible);
        Assert.Equal(40, new LauncherSettingsStore(host.Path).Load().WorkspacePadding);
        Click(host.Find<Button>(settings, "恢复默认设置"));
        Click(host.Find<Button>(settings, "确认恢复默认设置"));
        Assert.Equal(new LauncherSettings(), new LauncherSettingsStore(host.Path).Load());
        Assert.Equal(areaId, host.View.Document.Areas.Single().Id);
        Assert.Equal(new Thickness(22), host.View.Margin);
        Assert.False(host.Find<ToggleSwitch>(settings, "显示功能区序号").IsChecked);
        Assert.DoesNotContain(host.View.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "01" && block.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void Settings_render_in_dark_light_and_small_window()
    {
        using var host = new SettingsDesktop();
        var settings = host.Open();
        var directory = Environment.GetEnvironmentVariable("WONDER_SETTINGS_PREVIEW_DIRECTORY") ?? System.IO.Path.Combine(AppContext.BaseDirectory, "artifacts");
        Directory.CreateDirectory(directory);
        SavePreview(settings, System.IO.Path.Combine(directory, "settings-launcher.png"));
        Click(Raw<Button>(settings, "游戏全局设置"));
        SavePreview(settings, System.IO.Path.Combine(directory, "settings-game.png"));
        Click(Raw<Button>(settings, "个性化设置"));
        SavePreview(settings, System.IO.Path.Combine(directory, "settings-personalization.png"));
        SavePreview(settings, System.IO.Path.Combine(directory, "wonder-settings-dark.png"));
        host.Find<ComboBox>(settings, "主题").SelectedIndex = (int)LauncherTheme.Light;
        SavePreview(settings, System.IO.Path.Combine(directory, "wonder-settings-light.png"));
        settings.Width = 800;
        settings.Height = 520;
        Dispatcher.UIThread.RunJobs();
        var slider = host.Find<Slider>(settings, "侧边栏收起延迟");
        slider.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        Assert.True(slider.IsEffectivelyVisible);
        SavePreview(settings, System.IO.Path.Combine(directory, "wonder-settings-small.png"));
    }

    private static string Status(Window window) => window.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Name == "SettingsStatus").Text!;
    private static T Raw<T>(Window window, string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(control => AutomationProperties.GetName(control) == name);

    private static void SavePreview(Window window, string path)
    {
        Dispatcher.UIThread.RunJobs();
        using var bitmap = window.CaptureRenderedFrame();
        Assert.NotNull(bitmap);
        bitmap.Save(path, PngBitmapEncoderOptions.Default);
        Assert.True(new FileInfo(path).Length > 1000);
    }

    private static void Click(Control control)
    {
        control.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        var top = TopLevel.GetTopLevel(control)!;
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), top)!.Value;
        top.MouseDown(point, MouseButton.Left);
        top.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class SettingsDesktop : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Wonder.Settings.Tests", Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(_directory, "settings.json");
        public string WorkspacePath => System.IO.Path.Combine(_directory, "workspace.json");
        public string InstancesPath => System.IO.Path.Combine(_directory, "instances.json");
        public InstanceStore GameStore => new(InstancesPath);
        public MainWindow Main { get; private set; }
        public WorkspaceView View => Main.GetVisualDescendants().OfType<WorkspaceView>().Single();

        public SettingsDesktop(string? initialFile = null)
        {
            Directory.CreateDirectory(_directory);
            if (initialFile != null) File.WriteAllText(Path, initialFile);
            Main = new MainWindow(WorkspacePath);
            Main.Show();
            Dispatcher.UIThread.RunJobs();
        }

        public SettingsWindow Open()
        {
            View.OpenSettings();
            Dispatcher.UIThread.RunJobs();
            return Main.OwnedWindows.OfType<SettingsWindow>().Single();
        }

        public T Find<T>(Window window, string name) where T : Control
        {
            if (window is SettingsWindow)
            {
                var category = name switch
                {
                    "主题" or "强调色" or "工作区留白" or "显示功能区序号" => "个性化设置",
                    "长按拖动时间" or "侧边栏展开尺寸" or "侧边栏收起延迟" => "启动器设置",
                    _ => null
                };
                if (category is not null) Click(Raw<Button>(window, category));
            }
            return Raw<T>(window, name);
        }

        public void Restart()
        {
            Main.Close();
            Main = new MainWindow(WorkspacePath);
            Main.Show();
            Dispatcher.UIThread.RunJobs();
        }

        public void Dispose()
        {
            Main.Close();
            Directory.Delete(_directory, true);
            Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
            Palette.Apply(false, AccentColor.Mint);
        }
    }

    [AvaloniaFact]
    public void Workspace_menu_opens_one_settings_window_even_on_an_empty_desktop()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Wonder.Settings.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var main = new MainWindow(Path.Combine(directory, "workspace.json"));
        try
        {
            main.Show();
            var workspace = Assert.Single(main.GetVisualDescendants().OfType<WorkspaceView>());
            workspace.ReplaceDocument(Wonder.Workspace.WorkspaceDocument.CreateEmpty());
            var menu = workspace.CreateWorkspaceMenu();
            var entry = Assert.Single(menu.Items.OfType<MenuItem>(), item => Equals(item.Header, "设置…"));
            entry.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var settings = Assert.Single(main.OwnedWindows);
            Assert.Equal("设置 · Wonder Launcher", settings.Title);
            entry.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Same(settings, Assert.Single(main.OwnedWindows));
        }
        finally
        {
            main.Close();
            Directory.Delete(directory, true);
        }
    }
}
