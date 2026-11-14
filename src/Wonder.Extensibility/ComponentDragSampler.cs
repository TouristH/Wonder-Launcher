// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

namespace Wonder.Extensibility;

/// <summary>Replays pointer samples behind a monotonic clock and calculates tilt from the horizontal offset.</summary>
public sealed class ComponentDragSampler(double originX, double originY, double delayMilliseconds, double maximumTiltDegrees)
{
    private readonly Queue<(double Time, double X, double Y)> _pending = new();
    private (double Time, double X, double Y) _previous = (0, originX, originY);
    private double _latestX = originX;

    public (double X, double Y) Position { get; private set; } = (originX, originY);
    public double TiltDegrees { get; private set; }

    public void Record(double x, double y, double elapsedMilliseconds)
    {
        _latestX = x;
        _pending.Enqueue((elapsedMilliseconds, x, y));
    }

    public void Advance(double elapsedMilliseconds)
    {
        var playbackTime = Math.Max(0, elapsedMilliseconds - delayMilliseconds);
        while (_pending.TryPeek(out var next) && next.Time <= playbackTime)
            _previous = _pending.Dequeue();

        var (time, x, y) = _previous;
        if (_pending.TryPeek(out var future) && future.Time > time)
        {
            var progress = (playbackTime - time) / (future.Time - time);
            x += (future.X - x) * progress;
            y += (future.Y - y) * progress;
        }
        Position = (x, y);
        TiltDegrees = Math.Clamp((_latestX - x) * .08, -maximumTiltDegrees, maximumTiltDegrees);
    }
}
