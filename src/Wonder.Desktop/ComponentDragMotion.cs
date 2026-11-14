// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia;

namespace Wonder.Desktop;

/// <summary>Replays pointer samples behind a monotonic clock, without changing the final drop position.</summary>
internal sealed class ComponentDragMotion(Point origin, double delayMilliseconds, double maximumTiltDegrees)
{
    private readonly Queue<(double Time, Point Position)> _pending = new();
    private (double Time, Point Position) _previous = (0, origin);
    private Point _latest = origin;

    public Point Position { get; private set; } = origin;
    public double TiltDegrees { get; private set; }

    public void Record(Point position, double elapsedMilliseconds)
    {
        _latest = position;
        _pending.Enqueue((elapsedMilliseconds, position));
    }

    public void Advance(double elapsedMilliseconds)
    {
        var playbackTime = Math.Max(0, elapsedMilliseconds - delayMilliseconds);
        while (_pending.TryPeek(out var next) && next.Time <= playbackTime)
            _previous = _pending.Dequeue();

        Position = _previous.Position;
        if (_pending.TryPeek(out var future) && future.Time > _previous.Time)
        {
            var progress = (playbackTime - _previous.Time) / (future.Time - _previous.Time);
            Position += (future.Position - _previous.Position) * progress;
        }
        TiltDegrees = Math.Clamp((_latest.X - Position.X) * .08, -maximumTiltDegrees, maximumTiltDegrees);
    }
}
