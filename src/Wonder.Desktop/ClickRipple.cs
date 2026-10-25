// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Wonder.Desktop;

internal sealed class ClickRipple : Control
{
    private readonly Geometry _clip;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _elapsed = new();
    private Point _origin;
    private double _progress = 1;
    private double _radius;

    public ClickRipple(Geometry clip)
    {
        _clip = clip;
        _timer.Tick += (_, _) =>
        {
            _progress = Math.Min(1, _elapsed.Elapsed.TotalMilliseconds / 350);
            if (_progress >= 1) Stop();
            InvalidateVisual();
        };
        DetachedFromVisualTree += (_, _) => Stop();
    }

    public void Start(Point origin)
    {
        if (VisualRoot == null) return;
        _origin = origin;
        var x = Math.Max(origin.X, Bounds.Width - origin.X);
        var y = Math.Max(origin.Y, Bounds.Height - origin.Y);
        _radius = Math.Sqrt(x * x + y * y);
        _progress = 0;
        _elapsed.Restart();
        _timer.Start();
        InvalidateVisual();
    }

    private void Stop()
    {
        _timer.Stop();
        _elapsed.Reset();
        _progress = 1;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        if (_progress >= 1) return;
        var radius = 4 + (_radius - 4) * (1 - Math.Pow(1 - _progress, 3));
        using (context.PushGeometryClip(_clip))
        using (context.PushOpacity(.30 * Math.Pow(1 - _progress, 1.5)))
            context.DrawEllipse(Palette.Accent, new Pen(Palette.Accent, 1.5), _origin, radius, radius);
    }
}
