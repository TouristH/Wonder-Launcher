// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using System.Globalization;
using Wonder.Extensibility.Contracts;

namespace Wonder.Plugin.PleaseWait;

/// <summary>A deterministic subtitle state machine driven only by observed monotonic time.</summary>
internal sealed class PleaseWaitNarration
{
    private readonly Dictionary<string, TimeSpan> _spoken = new(StringComparer.Ordinal);
    private PluginPointerObservation? _previousPointer;
    private string? _previousInstance;
    private long _lastAction;
    private TimeSpan? _started;
    private TimeSpan _notBefore;
    private TimeSpan _phaseAt;
    private Cue? _pending;
    private Cue? _visible;
    private bool _idleSpoken;
    private bool _opening = true;
    private bool _fadingOut;
    private string _text = "";
    private double _opacity;

    internal IReadOnlyDictionary<string, string> Update(PluginObservationSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            Clear();
            _previousPointer = null;
            return Values(TimeSpan.Zero);
        }
        var now = snapshot.Elapsed;
        if (_started is null)
        {
            _started = now;
            _previousPointer = snapshot.Pointer;
            _previousInstance = snapshot.SelectedInstance?.Id;
            _lastAction = snapshot.RecentActions.FirstOrDefault()?.Sequence ?? 0;
            _pending = new("opening", "玩家打开了启动器。");
            _notBefore = now + TimeSpan.FromMilliseconds(200);
            return Values(now);
        }

        var moved = DifferentPosition(_previousPointer, snapshot.Pointer);
        _previousPointer = snapshot.Pointer;
        var newActions = snapshot.RecentActions.Where(action => action.Sequence > _lastAction).ToArray();
        if (newActions.Length > 0) _lastAction = newActions.Max(action => action.Sequence);
        var action = newActions.FirstOrDefault(PleaseWaitActions.IsNarratable);
        var selected = snapshot.SelectedInstance;
        var changedInstance = selected?.Id != _previousInstance;
        _previousInstance = selected?.Id;

        if (moved || newActions.Length > 0 || changedInstance)
        {
            // Interruptions have no fade-out; even a half-faded sentence is removed this update.
            Clear();
            _opening = false;
            _notBefore = now + TimeSpan.FromMilliseconds(300);
            if (moved) _idleSpoken = false;
            if (action is not null) _pending = new(action.Kind, PleaseWaitActions.Sentence(action));
            else if (changedInstance && selected is not null)
                _pending = new("instance.selected", $"玩家选中了实例「{selected.Name}」。");
        }

        if (snapshot.Pointer is { StationaryDuration.TotalSeconds: < 1 } && !_opening)
        {
            _text = "";
            _opacity = 0;
            _visible = null;
            return Values(now);
        }

        if (_visible is not null)
        {
            var age = (now - _phaseAt).TotalMilliseconds;
            if (_fadingOut)
            {
                _opacity = Math.Clamp(1 - age / 120, 0, 1);
                if (age >= 120) { _visible = null; _text = ""; _opacity = 0; _fadingOut = false; _opening = false; }
            }
            else
            {
                _opacity = Math.Clamp(age / 180, 0, 1);
                if (age >= 3180) { _phaseAt = now; _fadingOut = true; }
            }
        }

        if (_pending is null && _visible is null && !_idleSpoken && snapshot.Pointer is { StationaryDuration.TotalSeconds: >= 1 } pointer)
            _pending = new("pointer.idle", DescribePointer(pointer));

        if (_pending is not null && _visible is null && now >= _notBefore)
        {
            var cue = _pending;
            _pending = null;
            if (!_spoken.TryGetValue(cue.Kind, out var previous) || now - previous >= TimeSpan.FromSeconds(8))
            {
                if (cue.Kind == "pointer.idle")
                {
                    if (snapshot.Pointer is not { } currentPointer) return Values(now);
                    cue = cue with { Text = DescribePointer(currentPointer) };
                    _idleSpoken = true;
                }
                _visible = cue;
                _text = cue.Text;
                _opacity = 0;
                _phaseAt = now;
                _spoken[cue.Kind] = now;
                if (cue.Kind != "opening") _opening = false;
            }
        }
        return Values(now);
    }

    private void Clear()
    {
        _pending = null;
        _visible = null;
        _text = "";
        _opacity = 0;
        _fadingOut = false;
    }

    private IReadOnlyDictionary<string, string> Values(TimeSpan now)
    {
        var active = _visible is not null || _pending is not null;
        var phase = _started is { } start ? Math.Max(0, (now - start).TotalSeconds) : 0;
        var breathing = .8 + .2 * Math.Cos(phase * Math.PI / 2);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["narration"] = _text,
            ["narration-opacity"] = _opacity.ToString("0.###", CultureInfo.InvariantCulture),
            ["narration-indicator"] = (_opening ? 1 : breathing).ToString("0.###", CultureInfo.InvariantCulture),
            ["narration-active"] = active ? "true" : "false"
        };
    }

    private static bool DifferentPosition(PluginPointerObservation? before, PluginPointerObservation? after) =>
        before is null != (after is null) || before is not null && after is not null &&
        (before.X != after.X || before.Y != after.Y || before.Surface != after.Surface);

    private static string DescribePointer(PluginPointerObservation pointer)
    {
        string place;
        if (pointer.SurfaceWidth > 0 && pointer.SurfaceHeight > 0)
        {
            var horizontal = pointer.X < pointer.SurfaceWidth / 3 ? "左" : pointer.X >= pointer.SurfaceWidth * 2 / 3 ? "右" : "";
            var vertical = pointer.Y < pointer.SurfaceHeight / 3 ? "上" : pointer.Y >= pointer.SurfaceHeight * 2 / 3 ? "下" : "";
            place = horizontal + vertical;
            place = place.Length == 0 ? "中间" : place + "方";
        }
        else place = FormattableString.Invariant($"坐标 {pointer.X:0}, {pointer.Y:0}");
        return $"玩家的光标在「{pointer.Surface}」的{place}。停了{(int)pointer.StationaryDuration.TotalSeconds}秒。";
    }

    private sealed record Cue(string Kind, string Text);
}
