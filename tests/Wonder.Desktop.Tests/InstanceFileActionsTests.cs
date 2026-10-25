// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Wonder.Application;
using Wonder.Core;
using Wonder.Infrastructure;
using Xunit;

namespace Wonder.Desktop.Tests;

public sealed class InstanceFileActionsTests
{
    [AvaloniaFact]
    public async Task Delete_button_does_not_select_and_confirmation_requires_exact_word_or_cancel()
    {
        using var f = new Browser();
        var selected = f.Service.SelectedInstance!.Id;
        var cards = Cards(f.Manager, "目录中的实例");
        Click(cards[1].DeleteButton);
        var dialog = Assert.Single(f.Manager.OwnedWindows.OfType<DeleteConfirmationWindow>());
        Assert.Equal(selected, f.Service.SelectedInstance!.Id);
        var word = Find<TextBox>(dialog, "删除确认词"); var confirm = Find<Button>(dialog, "确认移入回收站");
        foreach (var text in new[] { "", "Sure", "sure ", "SURE" }) { word.Text = text; Assert.False(confirm.IsEnabled); }
        Click(Find<Button>(dialog, "取消删除")); Assert.Equal(0, f.Files.Recycles); Assert.Equal(2, f.Service.Catalog.Instances.Count);
        Click(cards[1].DeleteButton); dialog = Assert.Single(f.Manager.OwnedWindows.OfType<DeleteConfirmationWindow>());
        Find<TextBox>(dialog, "删除确认词").Text = "sure";
        SaveFrame(dialog, "delete-confirmation"); Click(Find<Button>(dialog, "确认移入回收站"));
        await WaitFor(() => f.Manager.OwnedWindows.Count == 0);
        Assert.Equal(1, f.Files.Recycles); Assert.Single(f.Service.Catalog.Instances); Assert.Equal(selected, f.Service.SelectedInstance!.Id);
        Assert.False(Directory.Exists(Path.Combine(f.Root, "versions", "two")));
        Assert.True(Directory.Exists(Path.Combine(f.Root, "versions", "one")));
        Click(Assert.Single(Cards(f.Manager, "目录中的实例")).DeleteButton);
        dialog = Assert.Single(f.Manager.OwnedWindows.OfType<DeleteConfirmationWindow>());
        Find<TextBox>(dialog, "删除确认词").Text = "sure"; Click(Find<Button>(dialog, "确认移入回收站"));
        await WaitFor(() => f.Manager.OwnedWindows.Count == 0);
        Assert.Empty(Cards(f.Manager, "目录中的实例")); Assert.Single(Cards(f.Manager, "游戏根目录列表"));
        Assert.Null(f.Service.SelectedInstance);
        f.Service.Reload(); Assert.Single(Cards(f.Manager, "游戏根目录列表"));
    }

    [AvaloniaFact]
    public async Task Directory_settings_really_rename_folder_and_root_delete_uses_the_same_confirmation()
    {
        using var f = new Browser();
        Click(Cards(f.Manager, "游戏根目录列表")[0].SettingsButton);
        var dialog = Assert.Single(f.Manager.OwnedWindows.OfType<DirectorySettingsWindow>());
        Find<TextBox>(dialog, "目录昵称").Text = "../invalid"; Click(Find<Button>(dialog, "保存目录设置"));
        Assert.True(dialog.IsVisible); Assert.True(Directory.Exists(f.Root));
        Find<TextBox>(dialog, "目录昵称").Text = "我的游戏目录";
        SaveFrame(dialog, "directory-settings"); Click(Find<Button>(dialog, "保存目录设置"));
        await WaitFor(() => f.Manager.OwnedWindows.Count == 0);
        var destination = Path.Combine(f.Base, "我的游戏目录");
        Assert.False(Directory.Exists(f.Root)); Assert.True(Directory.Exists(destination));
        Assert.All(f.Service.Catalog.Instances, instance => Assert.Equal(destination, instance.RootDirectory));
        var card = Assert.Single(Cards(f.Manager, "游戏根目录列表")); Assert.Equal(destination, card.Tag);
        Click(card.DeleteButton); var deletion = Assert.Single(f.Manager.OwnedWindows.OfType<DeleteConfirmationWindow>());
        Assert.Contains(deletion.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains("2 个实例登记") == true);
        Find<TextBox>(deletion, "删除确认词").Text = "sure"; Click(Find<Button>(deletion, "确认移入回收站"));
        await WaitFor(() => f.Manager.OwnedWindows.Count == 0);
        Assert.Empty(f.Service.Catalog.Instances); Assert.Empty(Cards(f.Manager, "游戏根目录列表"));
    }

    [AvaloniaFact]
    public void Long_instance_names_versions_and_paths_wrap_inside_compact_cards_without_clipping()
    {
        using var f = new Browser();
        var item = f.Service.Catalog.Instances[1];
        var longName = string.Concat(Enumerable.Repeat("这是一份完整显示名称的生存实例", 4)); f.Service.Rename(item.Id, longName);
        Dispatcher.UIThread.RunJobs();
        var card = Cards(f.Manager, "目录中的实例")[1];
        Assert.InRange(card.Bounds.Width, 200, 260);
        var title = card.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == longName);
        Assert.True(title.Bounds.Height > 30);
        foreach (var text in card.SelectButton.GetVisualDescendants().OfType<TextBlock>())
        {
            var origin = text.TranslatePoint(default, card)!.Value;
            Assert.True(origin.Y + text.Bounds.Height <= card.Bounds.Height + 1);
            Assert.True(origin.X + text.Bounds.Width <= card.Bounds.Width + 1);
        }
        Click(card.SettingsButton); Assert.NotEqual(item.Id, f.Service.SelectedInstance!.Id);
        Click(Find<Button>(f.Manager, "返回游戏目录")); SaveFrame(f.Manager, "compact-complete-text");
    }

    private static T Find<T>(Window window, string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => AutomationProperties.GetName(c) == name);
    private static InstanceBrowserCard[] Cards(Window window, string name) => Find<Panel>(window, name).Children.OfType<InstanceBrowserCard>().ToArray();
    private static void Click(Control control)
    {
        control.BringIntoView(); Dispatcher.UIThread.RunJobs(); var window = (Window)TopLevel.GetTopLevel(control)!;
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
    }
    private static async Task WaitFor(Func<bool> complete)
    { for (var i = 0; i < 100 && !complete(); i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); } Assert.True(complete()); }
    private static void SaveFrame(Window window, string name)
    {
        Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, $"../../../../../artifacts/design-0.5.1/{name}.png")); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        frame.Save(path, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }
    private sealed class Browser : IDisposable
    {
        public string Base { get; } = Path.Combine(Path.GetTempPath(), "Wonder.FileUi", Guid.NewGuid().ToString("N"));
        public string Root => Path.Combine(Base, "game");
        public InstanceService Service { get; }
        public Files Files { get; }
        public Window Owner { get; } = new() { Width = 1100, Height = 850 };
        public InstanceManagerWindow Manager { get; }
        public Browser()
        {
            foreach (var name in new[] { "one", "two" })
            {
                var directory = Path.Combine(Root, "versions", name); Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, name + ".json"), "{\"id\":\"" + name + "\",\"clientVersion\":\"1.20.1\"}");
            }
            Files = new(Base);
            Service = new(new InstanceStore(Path.Combine(Base, "wonder", "instances.json")), new InstanceDiscovery(), new Launcher(), Path.Combine(Base, "wonder", "cache"), Files);
            Task.Run(() => Service.ImportAsync(Root)).GetAwaiter().GetResult(); Assert.Null(Service.Error);
            Owner.Show(); Manager = new(Service); Manager.Show(Owner); Dispatcher.UIThread.RunJobs();
        }
        public void Dispose() { Owner.Close(); Directory.Delete(Base, true); }
    }
    private sealed class Files(string root) : IInstanceFileOperations
    {
        private readonly InstanceFileOperations _real = new(); public int Recycles;
        public string InspectDirectory(string path) => _real.InspectDirectory(path);
        public void EnsureGameRoot(string path) => _real.EnsureGameRoot(path);
        public void EnsureNoVersionDependents(GameInstance item, string path) => _real.EnsureNoVersionDependents(item, path);
        public Task MoveDirectoryAsync(string path, string destination, string identity) => _real.MoveDirectoryAsync(path, destination, identity);
        public Task RecycleDirectoryAsync(string path, string identity) { Assert.Equal(identity, InspectDirectory(path)); Recycles++; Directory.Move(path, Path.Combine(root, "recycled-" + Guid.NewGuid().ToString("N"))); return Task.CompletedTask; }
    }
    private sealed class Launcher : IGameLauncher
    {
        public Task<LaunchPlan> BuildPlanAsync(LaunchRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> RunAsync(LaunchPlan plan, Action started, Action<string> output, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
