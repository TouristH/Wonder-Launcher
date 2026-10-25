// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Wonder.Workspace;
using Xunit;

namespace Wonder.Desktop.Tests;

public sealed partial class WorkspaceInteractionTests
{
    [AvaloniaFact]
    public async Task Rename_from_keyboard_context_menu_updates_only_the_selected_area()
    {
        using var desktop = new TestDesktop();
        var area = desktop.View.Document.Areas.Last();
        var otherNames = desktop.View.Document.Areas.Where(item => item != area).Select(item => item.Title).ToArray();
        var positions = area.Components.Select(item => (item.Id, item.X, item.Y)).ToArray();
        var menu = desktop.OpenComponentMenu(2);
        TestDesktop.ClickMenuItem(Assert.Single(menu.Items.OfType<MenuItem>(), item => Equals(item.Header, "重命名功能区…")));
        Dispatcher.UIThread.RunJobs();
        var dialog = Assert.Single(desktop.Window.OwnedWindows);
        var input = Assert.Single(dialog.GetVisualDescendants().OfType<TextBox>());
        Assert.True(input.IsFocused);
        Assert.Equal(area.Title, input.SelectedText);
        var previewPath = Environment.GetEnvironmentVariable("WONDER_RENAME_PREVIEW_PATH");
        if (!string.IsNullOrEmpty(previewPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(previewPath))!);
            using var frame = dialog.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame.Save(previewPath, PngBitmapEncoderOptions.Default);
        }

        dialog.KeyTextInput("  我的世界  ");
        dialog.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        desktop.Window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await Task.Yield();
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(desktop.Window.OwnedWindows);
        Assert.Equal("我的世界", area.Title);
        Assert.Equal(otherNames, desktop.View.Document.Areas.Where(item => item != area).Select(item => item.Title));
        Assert.Equal(positions, area.Components.Select(item => (item.Id, item.X, item.Y)));
        Assert.Contains(desktop.View.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "我的世界");
        Assert.Equal(1, desktop.ChangeCount);
        Assert.DoesNotContain(desktop.View.CreateWorkspaceMenu().Items.OfType<MenuItem>(), item => Equals(item.Header, "重命名功能区…"));
    }

    [AvaloniaTheory]
    [InlineData("escape")]
    [InlineData("close")]
    [InlineData("unchanged")]
    public async Task Rename_cancellation_or_unchanged_name_does_not_save(string action)
    {
        using var desktop = new TestDesktop();
        var area = desktop.View.Document.Areas.First();
        var title = area.Title;
        var dialog = OpenRename(desktop.View, desktop.Window, area);
        var input = Assert.Single(dialog.GetVisualDescendants().OfType<TextBox>());
        input.Text = action == "unchanged" ? "  " + title + "  " : "不应保存";
        if (action == "close") dialog.Close();
        else dialog.KeyPressQwerty(action == "escape" ? PhysicalKey.Escape : PhysicalKey.Enter, RawInputModifiers.None);
        await Task.Yield();
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(desktop.Window.OwnedWindows);
        Assert.Equal(title, area.Title);
        Assert.Equal(0, desktop.ChangeCount);
    }

    [AvaloniaTheory]
    [InlineData(" ", "请输入")]
    [InlineData("a\nb", "单行")]
    [InlineData("a\tb", "单行")]
    [InlineData("a\u2028b", "单行")]
    [InlineData("too-long", "120")]
    public async Task Rename_invalid_name_stays_editable_and_can_be_corrected(string name, string message)
    {
        using var desktop = new TestDesktop();
        var area = desktop.View.Document.Areas.First();
        var title = area.Title;
        var dialog = OpenRename(desktop.View, desktop.Window, area);
        var input = Assert.Single(dialog.GetVisualDescendants().OfType<TextBox>());
        input.Text = name == "too-long" ? new string('奇', 121) : name;
        dialog.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        dialog.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Assert.True(dialog.IsVisible);
        Assert.Equal(title, area.Title);
        Assert.Equal(0, desktop.ChangeCount);
        Assert.Contains(dialog.GetVisualDescendants().OfType<TextBlock>(), text => AutomationProperties.GetName(text) == "名称校验提示" && text.Text!.Contains(message));

        input.Text = new string('奇', 120);
        dialog.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await Task.Yield();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(input.Text, area.Title);
        Assert.Equal(1, desktop.ChangeCount);
    }

    [AvaloniaTheory]
    [InlineData(SidebarEdge.Left)]
    [InlineData(SidebarEdge.Right)]
    [InlineData(SidebarEdge.Top)]
    [InlineData(SidebarEdge.Bottom)]
    public async Task Rename_sidebar_updates_rail_label_without_changing_layout_or_shortcuts(SidebarEdge edge)
    {
        var (document, area) = SidebarDocument(edge, collapsed: true);
        using var desktop = new TestDesktop(document: document);
        var root = document.Root;
        var ratio = root!.Ratio;
        var component = Assert.Single(area.Components);
        var position = (component.X, component.Y);
        var dialog = OpenRename(desktop.View, desktop.Window, area);
        const string title = "🎮模组e\u0301工具";
        Assert.Single(dialog.GetVisualDescendants().OfType<TextBox>()).Text = title;
        dialog.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await Task.Yield();
        Dispatcher.UIThread.RunJobs();

        var tab = Assert.Single(desktop.View.Children.OfType<Border>(), item => AutomationProperties.GetName(item) == "侧边栏 " + title);
        Assert.Equal(edge is SidebarEdge.Left or SidebarEdge.Right ? "🎮\n模\n组\ne\u0301\n工\n具" : title, Assert.IsType<TextBlock>(tab.Child).Text);
        Assert.Contains(title, Assert.IsType<string>(ToolTip.GetTip(tab)));
        Assert.True(tab.IsVisible);
        Assert.Same(root, document.Root);
        Assert.Equal(ratio, root.Ratio);
        Assert.Equal(edge, area.SidebarEdge);
        Assert.Equal(320, area.SidebarExtent);
        Assert.Same(component, Assert.Single(area.Components));
        Assert.Equal(position, (component.X, component.Y));
        Assert.Equal(1, desktop.ChangeCount);
    }

    [AvaloniaFact]
    public void Rename_menu_is_available_from_the_collapsed_sidebar_itself()
    {
        var (document, area) = SidebarDocument(SidebarEdge.Left, collapsed: true);
        using var desktop = new TestDesktop(document: document);
        var tab = Assert.Single(desktop.View.Children.OfType<Border>(), item => AutomationProperties.GetName(item) == "侧边栏 " + area.Title);
        tab.RaiseEvent(new ContextRequestedEventArgs());
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(desktop.View.ContextMenu);
        var rename = Assert.Single(desktop.View.ContextMenu.Items.OfType<MenuItem>(), item => Equals(item.Header, "重命名功能区…"));
        TestDesktop.ClickMenuItem(rename);
        Dispatcher.UIThread.RunJobs();
        var dialog = Assert.Single(desktop.Window.OwnedWindows);
        Assert.Equal(area.Title, Assert.Single(dialog.GetVisualDescendants().OfType<TextBox>()).Text);
        dialog.Close();
    }

    [AvaloniaFact]
    public async Task Rename_saves_immediately_and_survives_main_window_restart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Wonder.Rename.Tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "workspace.json");
        var main = new MainWindow(path);
        try
        {
            main.Show();
            Dispatcher.UIThread.RunJobs();
            var workspace = Assert.Single(main.GetVisualDescendants().OfType<WorkspaceView>());
            var area = workspace.Document.Areas.First();
            var id = area.Id;
            var dialog = OpenRename(workspace, main, area);
            Assert.Single(dialog.GetVisualDescendants().OfType<TextBox>()).Text = "生存 / Survival";
            dialog.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            await Task.Yield();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("生存 / Survival", new WorkspaceStore(path).Load().FindArea(id)!.Title);

            main.Close();
            main = new MainWindow(path);
            main.Show();
            Dispatcher.UIThread.RunJobs();
            workspace = Assert.Single(main.GetVisualDescendants().OfType<WorkspaceView>());
            Assert.Equal("生存 / Survival", workspace.Document.FindArea(id)!.Title);
            Assert.Contains(workspace.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "生存 / Survival");
        }
        finally
        {
            main.Close();
            Directory.Delete(directory, true);
        }
    }

    private static Window OpenRename(WorkspaceView workspace, Window owner, Area area)
    {
        var entry = Assert.Single(workspace.CreateWorkspaceMenu(area.Id).Items.OfType<MenuItem>(), item => Equals(item.Header, "重命名功能区…"));
        entry.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        return Assert.Single(owner.OwnedWindows);
    }
}
