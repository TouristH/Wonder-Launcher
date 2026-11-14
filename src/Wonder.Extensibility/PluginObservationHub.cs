// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using Wonder.Extensibility.Contracts;

namespace Wonder.Extensibility;

/// <summary>Collects host-supplied launcher events and exposes immutable, thread-safe snapshots.</summary>
public sealed class PluginObservationHub(TimeProvider? timeProvider = null)
{
    private readonly object _gate = new();
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly long _start = (timeProvider ?? TimeProvider.System).GetTimestamp();
    private PluginPointerObservation? _pointer;
    private long _lastMovement;
    private long _sequence;
    private long _actionSequence;
    private readonly List<PluginClickObservation> _clicks = [];
    private readonly List<PluginInteractionObservation> _actions = [];
    private PluginInstanceObservation? _instance;

    public void MovePointer(double x, double y, string surface, double surfaceWidth = 0, double surfaceHeight = 0)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y)) return;
        lock (_gate)
        {
            if (_pointer is not { } previous || previous.X != x || previous.Y != y || previous.Surface != surface)
                _lastMovement = _clock.GetTimestamp();
            _pointer = new(x, y, TimeSpan.Zero, surface)
            {
                SurfaceWidth = double.IsFinite(surfaceWidth) ? Math.Max(0, surfaceWidth) : 0,
                SurfaceHeight = double.IsFinite(surfaceHeight) ? Math.Max(0, surfaceHeight) : 0
            };
        }
    }

    public void ClearPointer() { lock (_gate) _pointer = null; }

    public void RecordClick(string target, string button = "left")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        lock (_gate)
        {
            _clicks.Insert(0, new(++_sequence, target, _clock.GetElapsedTime(_start)) { Button = button });
            if (_clicks.Count > 5) _clicks.RemoveAt(5);
            RecordInteraction("pointer.press", target, button);
        }
    }

    public void RecordInteraction(string kind, string target, string? detail = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        lock (_gate)
        {
            _actions.Insert(0, new(++_actionSequence, kind, target, detail, _clock.GetElapsedTime(_start)));
            if (_actions.Count > 64) _actions.RemoveAt(64);
        }
    }

    public void SetSelectedInstance(PluginInstanceObservation? instance)
    {
        lock (_gate)
        {
            if (_instance == instance) return;
            _instance = instance;
            RecordInteraction("instance.select", instance?.Name ?? "未选择实例", instance?.Id);
        }
    }

    public PluginObservationSnapshot Read()
    {
        lock (_gate)
        {
            var now = _clock.GetTimestamp();
            return new()
            {
                TimestampUtc = _clock.GetUtcNow(), Elapsed = _clock.GetElapsedTime(_start, now),
                Pointer = _pointer is null ? null : _pointer with { StationaryDuration = _clock.GetElapsedTime(_lastMovement, now) },
                RecentClicks = Array.AsReadOnly(_clicks.ToArray()),
                RecentActions = Array.AsReadOnly(_actions.ToArray()), SelectedInstance = _instance
            };
        }
    }
}
