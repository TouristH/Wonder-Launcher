// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ShapePath = Avalonia.Controls.Shapes.Path;
using Wonder.Core;

namespace Wonder.Desktop;

/// <summary>
/// The Wonder mark: a spark with a bright core and a paler trail sweeping up to the left,
/// so wonder reads as both a sudden surprise and a question setting off toward something.
/// Geometry lives in a 64-unit box and is scaled to the requested size; every fill comes from
/// <see cref="Palette"/> so the mark follows the active accent, while the packaged icon in
/// Assets/Brand is the same drawing rasterised at seven sizes.
/// </summary>
internal static class WonderMark
{
    // Head centre and reach, in the shared 64-unit box.
    private const double HeadX = 43, HeadY = 41, HeadReach = 20.5, CoreReach = 9.8;

    // Concavity of the star's sides: 0 is a diamond, 1 collapses to the centre.
    private const double Waist = .20;

    private static readonly List<WeakReference<Window>> Windows = [];
    private static WindowIcon? _windowIcon;
    private static MarkColors? _iconColors;

    /// <summary>
    /// One rendered icon for the current mark colours, shared by all running windows.
    /// The executable's packaged icon remains the fallback if rendering is unavailable.
    /// </summary>
    public static WindowIcon? WindowIcon
    {
        get
        {
            var colors = new MarkColors(Palette.MarkTop.Color, Palette.MarkLeft.Color,
                Palette.MarkRight.Color, Palette.MarkCore.Color, Palette.MarkTrail.Color);
            if (_windowIcon is not null && _iconColors == colors) return _windowIcon;
            try
            {
                using var stream = new MemoryStream(RenderIconPng(), writable: false);
                var icon = new WindowIcon(stream);
                _windowIcon = icon;
                _iconColors = colors;
            }
            catch (Exception error)
            {
                LauncherDiagnostics.Warn("RenderWindowIcon", error);
            }
            return _windowIcon;
        }
    }

    /// <summary>Registers a window without retaining it after its owner releases it.</summary>
    public static void ApplyIcon(Window window)
    {
        RemoveWindows(null);
        if (!Windows.Any(reference => reference.TryGetTarget(out var existing) && ReferenceEquals(existing, window)))
        {
            Windows.Add(new WeakReference<Window>(window));
            window.Closed += WindowClosed;
        }
        if (WindowIcon is { } icon) window.Icon = icon;
    }

    /// <summary>Called once after the complete palette update, never for individual brush changes.</summary>
    public static void RefreshWindowIcons()
    {
        RemoveWindows(null);
        if (Windows.Count == 0 || WindowIcon is not { } icon) return;
        foreach (var reference in Windows)
            if (reference.TryGetTarget(out var window) && !ReferenceEquals(window.Icon, icon))
                window.Icon = icon;
    }

    private static void WindowClosed(object? sender, EventArgs args)
    {
        if (sender is not Window window) return;
        window.Closed -= WindowClosed;
        RemoveWindows(window);
    }

    private static void RemoveWindows(Window? closed) => Windows.RemoveAll(reference =>
        !reference.TryGetTarget(out var window) || ReferenceEquals(window, closed));

    private readonly record struct MarkColors(Color Top, Color Left, Color Right, Color Core, Color Trail);

    internal static byte[] RenderIconPng()
    {
        const int size = 128;
        var mark = Create(size);
        mark.Measure(new Size(size, size));
        mark.Arrange(new Rect(0, 0, size, size));
        using var bitmap = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        bitmap.Render(mark);
        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
    }

    /// <summary>Draws the mark at the given size, following the current accent.</summary>
    public static Control Create(double size)
    {
        var canvas = new Canvas { Width = size, Height = size, IsHitTestVisible = false };
        canvas.Children.Add(Trail(size));
        canvas.Children.Add(Star(HeadX, HeadY, HeadReach, Waist, Palette.MarkTop, size));
        canvas.Children.Add(Star(HeadX, HeadY, CoreReach, Waist, Palette.MarkCore, size));
        canvas.Children.Add(Star(14, 49, 5.2, .24, Palette.MarkLeft, size));
        canvas.Children.Add(Star(56, 12, 4.8, .24, Palette.MarkCore, size));
        canvas.Children.Add(Star(28, 57, 2.8, .24, Palette.MarkRight, size));
        return canvas;
    }

    /// <summary>The tapered comet trail, sampled along a quadratic spine from the head to the tail tip.</summary>
    private static ShapePath Trail(double size)
    {
        // Start at the head, control point in the middle, end at the tail tip.
        (double X, double Y) p0 = (36, 34), c = (22, 22), p1 = (3, 12);
        const int steps = 26;
        const double width = 5.0, taper = 1.15;

        var left = new List<Point>(steps + 1);
        var right = new List<Point>(steps + 1);
        for (var i = 0; i <= steps; i++)
        {
            var t = i / (double)steps;
            var mt = 1 - t;
            var x = mt * mt * p0.X + 2 * mt * t * c.X + t * t * p1.X;
            var y = mt * mt * p0.Y + 2 * mt * t * c.Y + t * t * p1.Y;
            var dx = 2 * mt * (c.X - p0.X) + 2 * t * (p1.X - c.X);
            var dy = 2 * mt * (c.Y - p0.Y) + 2 * t * (p1.Y - c.Y);
            var length = Math.Sqrt(dx * dx + dy * dy);
            var nx = -dy / length;
            var ny = dx / length;
            var w = width * Math.Pow(1 - t, taper);
            left.Add(new Point((x + nx * w) / 64 * size, (y + ny * w) / 64 * size));
            right.Add(new Point((x - nx * w) / 64 * size, (y - ny * w) / 64 * size));
        }

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(left[0], true);
            foreach (var point in left.Skip(1)) ctx.LineTo(point);
            ctx.LineTo(new Point(p1.X / 64 * size, p1.Y / 64 * size));
            for (var i = right.Count - 1; i >= 0; i--) ctx.LineTo(right[i]);
            ctx.EndFigure(true);
        }

        return new ShapePath { Data = geometry, Fill = Palette.MarkTrail, IsHitTestVisible = false };
    }

    /// <summary>A four-pointed star with concave sides, drawn as four quadratic arcs.</summary>
    private static ShapePath Star(double cx, double cy, double reach, double waist, IBrush fill, double size)
    {
        var k = reach * waist;
        Point P(double x, double y) => new(x / 64 * size, y / 64 * size);

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(P(cx, cy - reach), true);
            ctx.QuadraticBezierTo(P(cx + k, cy - k), P(cx + reach, cy));
            ctx.QuadraticBezierTo(P(cx + k, cy + k), P(cx, cy + reach));
            ctx.QuadraticBezierTo(P(cx - k, cy + k), P(cx - reach, cy));
            ctx.QuadraticBezierTo(P(cx - k, cy - k), P(cx, cy - reach));
            ctx.EndFigure(true);
        }

        return new ShapePath { Data = geometry, Fill = fill, IsHitTestVisible = false };
    }
}
