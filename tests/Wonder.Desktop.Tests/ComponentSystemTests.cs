// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Wonder.Desktop.Components;
using Wonder.Extensibility.Contracts;
using Wonder.Workspace;
using Xunit;

namespace Wonder.Desktop.Tests;

public sealed class ComponentSystemTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Throwing_input_callbacks_are_contained_for_live_views_and_previews(bool selection, bool preview)
    {
        var throwCallbacks = false;
        var created = 0;
        var registry = new ComponentRegistry();
        registry.Register(new ComponentDefinition("test.input-failure", "输入故障", "", 240, 110),
            state => { created++; return new TextRuntime(state); },
            context => new ComponentContent(new Border(),
                canStartDrag: _ => throwCallbacks ? throw new InvalidOperationException("input failed") : true,
                select: _ => { if (throwCallbacks) throw new InvalidOperationException("input failed"); }));
        registry.Register(new ComponentDefinition("test.healthy-input", "健康组件", "", 240, 110),
            state => new TextRuntime(state), context => ComponentContent.Text(context, "仍然可用"));
        using var host = new ComponentHost(() => { }, () => { }, registry: registry);
        var application = new ComponentItem { DefinitionId = "test.input-failure" };
        using var view = new ComponentView(application, host, preview);
        using var healthy = new ComponentView(new ComponentItem { DefinitionId = "test.healthy-input" }, host);
        var window = Show(new StackPanel { Children = { view, healthy } });
        try
        {
            throwCallbacks = true;
            var canDrag = false;
            var exception = Record.Exception(() =>
            {
                if (selection) view.SetSelected(true);
                else canDrag = view.CanStartDrag(new Point(20, 20));
            });
            Assert.Null(exception);
            Assert.False(canDrag);
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "input failed");
            Assert.Contains(healthy.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "仍然可用");
            Assert.Equal(preview ? 0 : 1, created);
            using var observer = host.Attach(application, visible: false);
            Assert.Equal(preview ? ComponentRunStatus.Loading : ComponentRunStatus.Faulted, observer.Status);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Registered_component_controls_own_footprint_without_workspace_type_branch()
    {
        var registry = new ComponentRegistry();
        registry.Register(new ComponentDefinition("test.rectangle", "自定义", "无需修改工作区", 310, 130),
            state => new TextRuntime(state), context => ComponentContent.Text(context, "测试内容"));
        using var host = new ComponentHost(() => { }, () => { }, registry: registry);
        using var view = new ComponentView(new ComponentItem { DefinitionId = "test.rectangle" }, host);
        Assert.Equal(new Size(310, 130), view.Footprint);
        var window = Show(view);
        Assert.True(view.IsHitAt(new Point(100, 90)));
        Assert.False(view.IsHitAt(new Point(-1, 90)));
        Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), x => x.Text == "测试内容");
        window.Close();
    }

    [AvaloniaFact]
    public void Preview_does_not_create_runtime_even_when_attached_to_a_window()
    {
        var created = 0;
        var registry = new ComponentRegistry();
        registry.Register(new ComponentDefinition("test.preview", "预览", "", 210, 150),
            state => { created++; return new TextRuntime(state); },
            context => ComponentContent.Text(context, "预览"));
        using var host = new ComponentHost(() => { }, () => { }, registry: registry);
        using var view = new ComponentView(new ComponentItem { DefinitionId = "test.preview" }, host, preview: true);
        var window = Show(view);
        view.SetRuntimeVisible(true);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, created);
        window.Close();
    }

    [AvaloniaFact]
    public void Notes_editor_syncs_without_replacing_input_and_only_title_allows_drag()
    {
        using var host = new ComponentHost(() => { }, () => { });
        var document = WorkspaceDocument.CreateEmpty();
        document.EnsureLibrary(host.Registry.Definitions.Select(x => x.Id));
        var application = document.Library.Single(x => x.DefinitionId == ComponentIds.Notes);
        using var first = new ComponentView(application, host);
        using var second = new ComponentView(document.CreateShortcut(application), host);
        var window = new Window { Width = 700, Height = 300, Content = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal, Children = { first, second }
        } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var firstInput = Assert.Single(first.GetVisualDescendants().OfType<TextBox>());
        var secondInput = Assert.Single(second.GetVisualDescendants().OfType<TextBox>());
        firstInput.Text = "自动保存与同步";
        Assert.Equal("自动保存与同步", secondInput.Text);
        Assert.Same(firstInput, Assert.Single(first.GetVisualDescendants().OfType<TextBox>()));
        Assert.Equal("自动保存与同步", application.State["text"]);
        Assert.True(first.CanStartDrag(new Point(50, 18)));
        Assert.False(first.CanStartDrag(new Point(50, 90)));
        window.Close();
    }

    [AvaloniaFact]
    public void Unknown_definition_renders_recoverable_placeholder_without_losing_state()
    {
        using var host = new ComponentHost(() => { }, () => { });
        var item = new ComponentItem { DefinitionId = "extension.not-installed", State = new() { ["saved"] = "still here" } };
        using var view = new ComponentView(item, host);
        var window = Show(view);
        Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), x => x.Text?.Contains("extension.not-installed") == true);
        Assert.Contains(view.GetVisualDescendants().OfType<Button>(), x => x.Content as string == "重试");
        Assert.Equal("still here", item.State["saved"]);
        window.Close();
    }

    [AvaloniaFact]
    public void Closing_note_before_dispatcher_runs_preserves_last_edit()
    {
        var saves = 0;
        using var host = new ComponentHost(() => { }, () => saves++);
        var item = new ComponentItem { DefinitionId = ComponentIds.Notes };
        using var view = new ComponentView(item, host);
        var window = Show(view);
        var input = Assert.Single(view.GetVisualDescendants().OfType<TextBox>());
        input.Text = "最后一个字";
        window.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("最后一个字", item.State.GetValueOrDefault("text"));
        Assert.Equal(1, saves);
    }

    [AvaloniaFact]
    public void Unknown_view_retry_uses_newly_registered_dimensions_and_presentation()
    {
        var registry = new ComponentRegistry();
        using var host = new ComponentHost(() => { }, () => { }, registry: registry);
        using var view = new ComponentView(new ComponentItem { DefinitionId = "test.recovered" }, host);
        var window = Show(view);
        registry.Register(new ComponentDefinition("test.recovered", "已恢复", "", 230, 110),
            state => new TextRuntime(state), context => ComponentContent.Text(context, "恢复的内容"));
        view.GetVisualDescendants().OfType<Button>().Single().RaiseEvent(
            new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new Size(230, 110), view.Footprint);
        Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "恢复的内容");
        window.Close();
    }

    [AvaloniaFact]
    public void Native_presentation_failure_is_retryable_and_does_not_break_another_view()
    {
        var fail = true;
        var registry = new ComponentRegistry();
        registry.Register(new ComponentDefinition("test.presentation", "展示", "", 240, 180),
            state => new TextRuntime(state), context =>
            {
                if (fail) throw new InvalidOperationException("presentation failed");
                return ComponentContent.Text(context, "恢复显示");
            });
        using var host = new ComponentHost(() => { }, () => { }, registry: registry);
        using var view = new ComponentView(new ComponentItem { DefinitionId = "test.presentation" }, host);
        var window = Show(view);
        Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "presentation failed");
        fail = false;
        view.GetVisualDescendants().OfType<Button>().Single().RaiseEvent(
            new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "恢复显示");
        window.Close();
    }

    private static Window Show(Control content)
    {
        var window = new Window { Width = 340, Height = 220, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void Failed_presenter_is_disposed_once_even_when_cleanup_throws_and_other_views_survive()
    {
        var failRefresh = false;
        var disposals = 0;
        TextRuntime? runtime = null;
        var registry = new ComponentRegistry();
        registry.Register(new ComponentDefinition("test.refresh", "刷新失败", "", 240, 180),
            state => runtime = new TextRuntime(state), context => new ComponentContent(
                new TextBlock { Text = "等待刷新" },
                refresh: () => { if (failRefresh) throw new InvalidOperationException("refresh failed"); },
                dispose: () => { disposals++; throw new InvalidOperationException("cleanup failed"); }));
        registry.Register(new ComponentDefinition("test.healthy", "正常", "", 240, 180),
            state => new TextRuntime(state), context => ComponentContent.Text(context, "仍然可用"));
        using var host = new ComponentHost(() => { }, () => { }, registry: registry);
        using var failing = new ComponentView(new ComponentItem { DefinitionId = "test.refresh" }, host);
        using var healthy = new ComponentView(new ComponentItem { DefinitionId = "test.healthy" }, host);
        var window = Show(new StackPanel { Children = { failing, healthy } });
        failRefresh = true;
        runtime!.Notify();
        Assert.Equal(1, disposals);
        Assert.Contains(failing.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "refresh failed");
        Assert.Contains(healthy.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "仍然可用");
        failing.Dispose();
        failing.Dispose();
        Assert.Equal(1, disposals);
        window.Close();
    }

    [AvaloniaFact]
    public void Disposing_presenter_with_throwing_cleanup_still_releases_runtime_visibility()
    {
        var registry = new ComponentRegistry();
        registry.Register(new ComponentDefinition("test.cleanup", "释放", "", 240, 180),
            state => new TextRuntime(state),
            context => new ComponentContent(new Border(), dispose: () => throw new InvalidOperationException("cleanup failed")));
        using var host = new ComponentHost(() => { }, () => { }, registry: registry);
        var item = new ComponentItem { DefinitionId = "test.cleanup" };
        using var view = new ComponentView(item, host);
        var window = Show(view);
        using var observer = host.Attach(item, visible: false);
        view.Dispose();
        Assert.Equal(ComponentRunStatus.Suspended, observer.Status);
        window.Close();
    }

    private sealed class TextRuntime(IComponentState state) : IComponentRuntime
    {
        public IReadOnlyDictionary<string, string> Values => state.Snapshot;
        public event EventHandler? Changed;
        public void Notify() => Changed?.Invoke(this, EventArgs.Empty);
        public void Resume() { }
        public void Suspend() { }
        public void Invoke(string actionId, string? value) { }
        public void Dispose() { }
    }

    [AvaloniaFact]
    public void Offscreen_initial_view_and_reattachment_wait_for_visible_viewport_before_starting()
    {
        var created = 0;
        var registry = new ComponentRegistry();
        registry.Register(new ComponentDefinition("test.viewport", "视口", "", 240, 110),
            state => { created++; return new TextRuntime(state); }, context => ComponentContent.Text(context, "可见时运行"));
        using var host = new ComponentHost(() => { }, () => { }, registry: registry);
        var item = new ComponentItem { DefinitionId = "test.viewport" };
        using var view = new ComponentView(item, host);
        var stack = new StackPanel { Children = { new Border { Height = 400 }, view } };
        var scroll = new ScrollViewer { Height = 120, Content = stack };
        var window = Show(scroll);
        using var observer = host.Attach(item, visible: false);
        Assert.Equal(0, created);
        Assert.Equal(ComponentRunStatus.Loading, observer.Status);
        stack.Children.Remove(view);
        stack.Children.Add(view);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, created);
        scroll.Offset = new Vector(0, 400);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, created);
        Assert.Equal(ComponentRunStatus.Ready, observer.Status);
        scroll.Offset = default;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(ComponentRunStatus.Suspended, observer.Status);
        window.Close();
    }

    [AvaloniaFact]
    public void Hiding_parent_suspends_runtime_until_parent_is_visible_again()
    {
        var registry = new ComponentRegistry();
        registry.Register(new ComponentDefinition("test.parent", "父容器", "", 240, 110),
            state => new TextRuntime(state), context => ComponentContent.Text(context, "跟随可见性"));
        using var host = new ComponentHost(() => { }, () => { }, registry: registry);
        var item = new ComponentItem { DefinitionId = "test.parent" };
        using var view = new ComponentView(item, host);
        var parent = new Border { Child = view };
        var window = Show(parent);
        using var observer = host.Attach(item, visible: false);
        Assert.Equal(ComponentRunStatus.Ready, observer.Status);
        parent.IsVisible = false;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(ComponentRunStatus.Suspended, observer.Status);
        parent.IsVisible = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(ComponentRunStatus.Ready, observer.Status);
        window.Close();
    }
}

