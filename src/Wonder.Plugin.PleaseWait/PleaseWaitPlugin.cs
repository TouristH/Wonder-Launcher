// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Wonder.Extensibility.Contracts;

namespace Wonder.Plugin.PleaseWait;

public sealed class PleaseWaitPlugin : IWonderPlugin
{
    private PleaseWaitSession? _session;

    public void Activate(IPluginContext context)
    {
        var configuration = PleaseWaitConfiguration.Read(context.ConfigurationJson);
        var session = PleaseWaitSession.Open(context.DataDirectory, context.GetObservation, context.Log);
        _session = session;
        var showObservations = configuration.ShowObservations && session.HasObservations;
        ComponentDesktopOptions Desktop(int order, bool stretch = false) => new()
        {
            PlaceOnFirstActivation = true, SnapToGrid = false,
            DragDelayMilliseconds = configuration.DragDelayMilliseconds,
            MaximumDragTiltDegrees = configuration.MaximumDragTiltDegrees,
            StretchToAreaWidth = stretch, HorizontalMargin = 24, TopInset = 16,
            FirstActivationOrder = order, RevealDelayMilliseconds = order * 300,
            RevealDurationMilliseconds = 180, RevealOffsetY = 4
        };
        void Register(string id, string title, double width, double height, int order, ComponentCard card, Scene kind)
        {
            context.RegisterComponent(new(
                new ComponentDefinition("wonder.please-wait." + id, title, "请稍候 · " + title, width, height)
                { Desktop = Desktop(order, kind == Scene.Narration) }, card, _ => new SceneRuntime(session, kind)));
        }

        if (session.HasObservations)
            Register("narration", "解说条", 520, 56, 0, new ComponentCard
            {
                TextKey = "narration",
                Narration = new()
                {
                    TextOpacityKey = "narration-opacity", IndicatorOpacityKey = "narration-indicator",
                    IndicatorActiveKey = "narration-active"
                },
                Style = new() { TextFontSize = 15 }
            }, Scene.Narration);

        Register("attendance", "出勤表", 220, showObservations ? 180 : 96, 1, new ComponentCard
        {
            Title = "出 勤 表", Rows = showObservations ? PleaseWaitObservation.Rows : [], Scrollable = true,
            Style = new() { TextFontSize = 11, FooterFontSize = 10 }
        }, Scene.Attendance);

        if (!session.HasObservations) return;
        Register("tools", "工具面板", 200, 132, 2, new ComponentCard
        {
            Title = "它 会 的", TextKey = "empty", FooterKey = "empty", RowLineHeight = 22,
            Style = new() { TitleColor = "#4A5058", TextColor = "#4A5058" },
            Rows = [new("观察", "empty") { SecondaryValueKey = "tool-dot" },
                new("记录", "empty") { SecondaryValueKey = "tool-dot" },
                new("解说", "empty") { SecondaryValueKey = "tool-dot" },
                new("搭建", "empty") { SecondaryValueKey = "tool-dot" }]
        }, Scene.Tools);
        Register("notebook", "记录本", 200, 140, 3, new ComponentCard
        {
            Title = "记 录", TextKey = "empty", FooterKey = "notebook-day",
            RowLineHeight = 16, Style = new() { TextFontSize = 11 }
        }, Scene.Notebook);
        Register("instances", "实例列表", 320, 120, 4, new ComponentCard
        {
            Title = "实 例", TextKey = "empty", FooterKey = "instance-status",
            Rows = [new("", "scene-instance") { SecondaryValueKey = "scene-version", AccentColor = "#E0A458", ActionId = "instance-info" }]
        }, Scene.Instances);
    }

    public void Dispose()
    {
        var session = _session;
        _session = null;
        session?.Dispose();
    }

    private enum Scene { Attendance, Narration, Tools, Notebook, Instances }

    private sealed class SceneRuntime : IComponentRuntime
    {
        private readonly PleaseWaitSession _session;
        private readonly object _gate = new();
        private readonly Scene _scene;
        private bool _instanceInfo;
        private bool _resumed;
        private bool _disposed;

        public SceneRuntime(PleaseWaitSession session, Scene scene)
        {
            _session = session;
            _scene = scene;
        }

        public IReadOnlyDictionary<string, string> Values
        {
            get
            {
                if (_scene is Scene.Attendance or Scene.Narration) return _session.Values;
                return new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["empty"] = "", ["tool-dot"] = "·", ["notebook-day"] = "第 1 天",
                    ["scene-instance"] = "请稍候", ["scene-version"] = "施工中",
                    ["instance-status"] = _instanceInfo ? "关卡尚未接入" : ""
                };
            }
        }
        public event EventHandler? Changed;

        public void Resume()
        {
            lock (_gate)
            {
                if (_disposed || _resumed) return;
                if (_scene == Scene.Attendance) _session.RecordFirstDisplay();
                _resumed = true;
                _session.Changed += SessionChanged;
                _session.AttachView(_scene == Scene.Narration);
            }
        }

        private void SessionChanged(object? sender, EventArgs args)
        {
            lock (_gate)
                if (_resumed && !_disposed) Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Suspend()
        {
            lock (_gate)
            {
                if (!_resumed) return;
                _resumed = false;
                _session.Changed -= SessionChanged;
                _session.DetachView(_scene == Scene.Narration);
            }
        }

        public void Invoke(string actionId, string? value)
        {
            lock (_gate)
            {
                if (_disposed || _scene != Scene.Instances || actionId != "instance-info") return;
                _instanceInfo = true;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                Suspend();
                _disposed = true;
                Changed = null;
            }
        }
    }
}
