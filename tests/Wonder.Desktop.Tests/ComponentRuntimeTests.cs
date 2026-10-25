// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia.Headless.XUnit;
using Wonder.Desktop.Components;
using Wonder.Extensibility.Contracts;
using Wonder.Workspace;
using Xunit;

namespace Wonder.Desktop.Tests;

public sealed class ComponentRuntimeTests
{
    [AvaloniaFact]
    public void Shared_application_reuses_runtime_and_suspends_only_after_last_visible_view()
    {
        var runtimes = new List<RecordingRuntime>();
        var registry = Registry("test.shared", state =>
        {
            var runtime = new RecordingRuntime(state);
            runtimes.Add(runtime);
            return runtime;
        });
        using var host = new ComponentHost(() => { }, () => { }, registry: registry);
        var document = WorkspaceDocument.CreateEmpty();
        var application = new ComponentItem { DefinitionId = "test.shared" };
        document.Library.Add(application);
        using var library = host.Attach(application);
        using var shortcut = host.Attach(document.CreateShortcut(application));
        var runtime = Assert.Single(runtimes);
        Assert.Equal(1, runtime.Resumes);
        library.SetVisible(false);
        Assert.Equal(0, runtime.Suspends);
        shortcut.Dispose();
        Assert.Equal(1, runtime.Suspends);
        using var remount = host.Attach(application);
        Assert.Single(runtimes);
        Assert.Equal(2, runtime.Resumes);
        host.Dispose();
        Assert.Equal(1, runtime.Disposals);
        Assert.Equal(2, runtime.Suspends);
    }

    [AvaloniaFact]
    public void Notes_share_application_state_but_keep_independent_applications_separate()
    {
        var saves = 0;
        using var host = new ComponentHost(() => { }, () => saves++);
        var document = WorkspaceDocument.CreateEmpty();
        document.EnsureLibrary(host.Registry.Definitions.Select(x => x.Id));
        var application = document.Library.Single(x => x.DefinitionId == ComponentIds.Notes);
        using var first = host.Attach(application);
        using var second = host.Attach(document.CreateShortcut(application));
        using var independent = host.Attach(new ComponentItem { DefinitionId = ComponentIds.Notes });
        var notifications = 0;
        second.Changed += (_, _) => notifications++;
        first.Invoke("set-text", "共享便笺");
        Assert.Equal("共享便笺", second.Values["text"]);
        Assert.Equal("共享便笺", application.State["text"]);
        Assert.False(independent.Values.ContainsKey("text"));
        Assert.Equal(1, saves);
        Assert.Equal(1, notifications);
        second.Dispose();
        first.Invoke("set-text", "更新内容");
        Assert.Equal(1, notifications);
    }

    [AvaloniaFact]
    public void Creation_and_action_failures_are_isolated_and_retry_releases_old_runtime()
    {
        var attempts = 0;
        var runtime = new RecordingRuntime(null);
        var registry = Registry("test.fault", _ =>
        {
            if (++attempts == 1) throw new InvalidOperationException("initialization failed");
            return runtime = new RecordingRuntime(null);
        });
        using var host = new ComponentHost(() => { }, () => { }, registry: registry);
        using var failed = host.Attach(new ComponentItem { DefinitionId = "test.fault" });
        Assert.Equal(ComponentRunStatus.Faulted, failed.Status);
        Assert.Contains("initialization failed", failed.Error);
        failed.Retry();
        Assert.Equal(ComponentRunStatus.Ready, failed.Status);
        using var healthy = host.Attach(new ComponentItem { DefinitionId = "test.fault" });
        var failedRuntime = (RecordingRuntime)failed.Runtime!;
        failed.Invoke("throw");
        Assert.Equal(ComponentRunStatus.Faulted, failed.Status);
        Assert.Equal(ComponentRunStatus.Ready, healthy.Status);
        failed.Retry();
        Assert.Equal(1, failedRuntime.Disposals);
        Assert.Equal(ComponentRunStatus.Ready, failed.Status);
    }

    [AvaloniaFact]
    public void Unknown_definition_keeps_state_and_can_recover_after_registration()
    {
        var registry = new ComponentRegistry();
        using var host = new ComponentHost(() => { }, () => { }, registry: registry);
        var item = new ComponentItem { DefinitionId = "extension.missing", State = new() { ["text"] = "保留数据" } };
        using var session = host.Attach(item);
        Assert.Equal(ComponentRunStatus.Unavailable, session.Status);
        Assert.Equal("保留数据", session.Values["text"]);
        registry.Register(new ComponentDefinition("extension.missing", "已恢复", "测试", 220, 180),
            state => new RecordingRuntime(state), context => ComponentContent.Text(context, "已恢复"));
        session.Retry();
        Assert.Equal(ComponentRunStatus.Ready, session.Status);
        Assert.Equal("保留数据", session.Values["text"]);
    }

    [AvaloniaFact]
    public void Settings_action_uses_host_service_and_status_contains_actual_environment()
    {
        var settingsOpened = 0;
        using var host = new ComponentHost(() => settingsOpened++, () => { }, "C:/Wonder/TestData");
        using var settings = host.Attach(new ComponentItem { DefinitionId = ComponentIds.Settings });
        using var status = host.Attach(new ComponentItem { DefinitionId = ComponentIds.LauncherStatus });
        settings.Invoke("open");
        Assert.Equal(1, settingsOpened);
        Assert.NotEmpty(status.Values["version"]);
        Assert.Contains(".NET", status.Values["runtime"]);
        Assert.Equal("C:/Wonder/TestData", status.Values["data-directory"]);
        Assert.NotEmpty(status.Values["uptime"]);
    }

    private static ComponentRegistry Registry(string id, Func<IComponentState, IComponentRuntime> factory)
    {
        var registry = new ComponentRegistry();
        registry.Register(new ComponentDefinition(id, "测试", "测试组件", 240, 170), factory,
            context => ComponentContent.Text(context, "测试"));
        return registry;
    }

    [AvaloniaFact]
    public void Initially_hidden_attachment_starts_only_when_shown_and_keeps_same_runtime_after_suspend()
    {
        var created = 0;
        var registry = Registry("test.hidden", state => { created++; return new RecordingRuntime(state); });
        using var host = new ComponentHost(() => { }, () => { }, registry: registry);
        using var session = host.Attach(new ComponentItem { DefinitionId = "test.hidden" }, visible: false);
        Assert.Equal(0, created);
        session.SetVisible(true);
        var runtime = session.Runtime;
        Assert.Equal(ComponentRunStatus.Ready, session.Status);
        session.SetVisible(false);
        Assert.Equal(ComponentRunStatus.Suspended, session.Status);
        session.SetVisible(true);
        Assert.Equal(1, created);
        Assert.Same(runtime, session.Runtime);
    }

    [AvaloniaFact]
    public void Fault_cleanup_still_disposes_runtime_when_event_unsubscription_throws()
    {
        var runtime = new BrokenCleanupRuntime();
        using var host = new ComponentHost(() => { }, () => { },
            registry: Registry("test.broken-cleanup", _ => runtime));
        using var session = host.Attach(new ComponentItem { DefinitionId = "test.broken-cleanup" });
        Assert.Equal(ComponentRunStatus.Faulted, session.Status);
        Assert.True(runtime.Disposed);
    }

    [AvaloniaFact]
    public void Retried_runtime_cannot_write_state_through_its_previous_access()
    {
        IComponentState? state = null;
        var registry = Registry("test.revoked", access => { state = access; return new RecordingRuntime(access); });
        var saves = 0;
        using var host = new ComponentHost(() => { }, () => saves++, registry: registry);
        var item = new ComponentItem { DefinitionId = "test.revoked" };
        using var session = host.Attach(item);
        var previous = state!;
        previous.Set("text", "before retry");
        session.Retry();
        Assert.Throws<ObjectDisposedException>(() => previous.Set("text", "stale"));
        state!.Set("text", "after retry");
        Assert.Equal("after retry", item.State["text"]);
        Assert.Equal(2, saves);
        host.Dispose();
        Assert.Throws<ObjectDisposedException>(() => state.Set("text", "after close"));
    }

    [AvaloniaFact]
    public async Task Status_timer_stops_when_last_view_hides_and_refreshes_when_shown_again()
    {
        using var host = new ComponentHost(() => { }, () => { });
        using var session = host.Attach(new ComponentItem { DefinitionId = ComponentIds.LauncherStatus });
        var changes = 0;
        session.Changed += (_, _) => changes++;
        await Task.Delay(1150);
        Assert.True(changes > 0);
        session.SetVisible(false);
        var suspendedChanges = changes;
        var suspendedUptime = session.Values["uptime"];
        await Task.Delay(1150);
        Assert.Equal(suspendedChanges, changes);
        Assert.Equal(suspendedUptime, session.Values["uptime"]);
        session.SetVisible(true);
        Assert.NotEqual(suspendedUptime, session.Values["uptime"]);
        host.Dispose();
        var disposedChanges = changes;
        await Task.Delay(1100);
        Assert.Equal(disposedChanges, changes);
    }

    private sealed class BrokenCleanupRuntime : IComponentRuntime
    {
        public bool Disposed { get; private set; }
        public IReadOnlyDictionary<string, string> Values => throw new InvalidOperationException("snapshot failed");
        public event EventHandler? Changed { add { } remove { throw new InvalidOperationException("unsubscribe failed"); } }
        public void Resume() { }
        public void Suspend() { }
        public void Invoke(string actionId, string? value) { }
        public void Dispose() => Disposed = true;
    }

    [AvaloniaFact]
    public void Throwing_observer_does_not_abort_other_views_or_state_persistence()
    {
        var saves = 0;
        using var host = new ComponentHost(() => { }, () => saves++);
        var item = new ComponentItem { DefinitionId = ComponentIds.Notes };
        using var first = host.Attach(item);
        using var second = host.Attach(item);
        first.Changed += (_, _) => throw new InvalidOperationException("bad observer");
        var sameSessionNotifications = 0;
        var otherSessionNotifications = 0;
        first.Changed += (_, _) => sameSessionNotifications++;
        second.Changed += (_, _) => otherSessionNotifications++;
        host.Changed += (_, _) => throw new InvalidOperationException("bad host observer");
        first.Invoke("set-text", "仍可保存");
        Assert.Equal("仍可保存", second.Values["text"]);
        Assert.Equal(1, saves);
        Assert.Equal(1, sameSessionNotifications);
        Assert.Equal(1, otherSessionNotifications);
        Assert.Equal(ComponentRunStatus.Ready, first.Status);
    }

    private sealed class RecordingRuntime(IComponentState? state) : IComponentRuntime
    {
        public int Resumes { get; private set; }
        public int Suspends { get; private set; }
        public int Disposals { get; private set; }
        public IReadOnlyDictionary<string, string> Values => state?.Snapshot ?? new Dictionary<string, string>();
        public event EventHandler? Changed { add { } remove { } }
        public void Resume() => Resumes++;
        public void Suspend() => Suspends++;
        public void Invoke(string actionId, string? value)
        {
            if (actionId == "throw") throw new InvalidOperationException("action failed");
        }
        public void Dispose() => Disposals++;
    }
}

