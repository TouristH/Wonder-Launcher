// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Wonder.Workspace;

namespace Wonder.Desktop;

/// <summary>A comet settles into the original Wonder mark, entirely inside its square.</summary>
internal sealed class StartupCometLogo : Canvas
{
    internal const double FlightMilliseconds = 1600;
    internal const double SparkMilliseconds = 400;
    internal const double SettleMilliseconds = 180;
    internal const double FadeMilliseconds = 220;

    private readonly double _size;
    private readonly WonderMark.MarkParts _parts;
    private readonly Canvas _head;
    private readonly ScaleTransform _scale = new();
    private readonly RotateTransform _rotation = new();
    private readonly TranslateTransform _translation = new();
    private readonly TransformGroup _motion;
    private readonly ScaleTransform[] _sparkScales;
    private readonly RotateTransform[] _sparkRotations;
    private readonly TranslateTransform[] _sparkTranslations;
    private readonly TransformGroup[] _sparkMotions;
    private readonly Point[] _sparkCenters;
    private readonly double[] _sparkReaches;
    private readonly double _headReach;
    internal double Progress { get; private set; } = -1;
    internal StartupLogoAnimation Animation { get; }

    internal StartupCometLogo(double size, StartupLogoAnimation animation = StartupLogoAnimation.SettledTwinkle)
    {
        Animation = animation;
        _size = Width = Height = size;
        IsHitTestVisible = false;
        ClipToBounds = true;
        _parts = WonderMark.CreateParts(size);
        _headReach = _parts.Head.Data!.Bounds.Width / size * 32;
        _head = new Canvas
        {
            Width = size, Height = size, IsHitTestVisible = false,
            RenderTransformOrigin = new RelativePoint(WonderMark.HeadCenter.X / 64 * size,
                WonderMark.HeadCenter.Y / 64 * size, RelativeUnit.Absolute)
        };
        _motion = new TransformGroup();
        _motion.Children.Add(_scale);
        _motion.Children.Add(_rotation);
        _motion.Children.Add(_translation);
        _head.Children.Add(_parts.Head);
        _head.Children.Add(_parts.Core);
        Children.Add(_parts.Trail);
        Children.Add(_head);
        _sparkScales = _parts.Sparks.Select(_ => new ScaleTransform()).ToArray();
        _sparkRotations = _parts.Sparks.Select(_ => new RotateTransform()).ToArray();
        _sparkTranslations = _parts.Sparks.Select(_ => new TranslateTransform()).ToArray();
        _sparkMotions = _parts.Sparks.Select(_ => new TransformGroup()).ToArray();
        _sparkCenters = _parts.Sparks.Select(spark => spark.Data!.Bounds.Center / size * 64).ToArray();
        _sparkReaches = _parts.Sparks.Select(spark => spark.Data!.Bounds.Width / size * 32).ToArray();
        for (var index = 0; index < _parts.Sparks.Length; index++)
        {
            var spark = _parts.Sparks[index];
            var center = spark.Data!.Bounds.Center;
            spark.RenderTransformOrigin = new RelativePoint(center.X, center.Y, RelativeUnit.Absolute);
            _sparkMotions[index].Children.Add(_sparkScales[index]);
            _sparkMotions[index].Children.Add(_sparkRotations[index]);
            _sparkMotions[index].Children.Add(_sparkTranslations[index]);
            // Released pieces start behind the main star and emerge as their paths separate.
            if (animation == StartupLogoAnimation.FragmentRelease) spark.ZIndex = -1;
            Children.Add(spark);
        }
        if (animation == StartupLogoAnimation.FragmentRelease) _parts.Trail.ZIndex = -2;
        SetProgress(0);
    }

    internal void SetProgress(double progress) => SetElapsed(progress * (FlightMilliseconds + SparkMilliseconds));

    internal void SetElapsed(double elapsedMilliseconds)
    {
        var elapsed = Math.Clamp(elapsedMilliseconds, 0, FlightMilliseconds + SparkMilliseconds);
        var progress = elapsed / (FlightMilliseconds + SparkMilliseconds);
        if (Progress == progress) return;
        Progress = progress;
        var flight = Math.Clamp(elapsed / FlightMilliseconds, 0, 1);
        var travel = Smooth(flight);
        const double trailFraction = .83;
        var reveal = Math.Min(travel / trailFraction, 1);
        var position = HeadPosition(travel);
        _parts.Trail.Data = WonderMark.TrailGeometry(_size, reveal);
        _scale.ScaleX = _scale.ScaleY = .08 + .92 * travel;
        _rotation.Angle = -225 * (1 - travel);
        _translation.X = (position.X - WonderMark.HeadCenter.X) / 64 * _size;
        _translation.Y = (position.Y - WonderMark.HeadCenter.Y) / 64 * _size;
        _head.RenderTransform = flight == 1 ? null : _motion;
        _head.Opacity = _parts.Trail.Opacity = Smooth(Math.Clamp(flight / .12, 0, 1));
        if (Animation == StartupLogoAnimation.FragmentRelease) ReleaseFragments(elapsed);
        else TwinkleSparks(elapsed);
    }

    private void TwinkleSparks(double elapsed)
    {
        for (var index = 0; index < _parts.Sparks.Length; index++)
        {
            // The head settles first. Each small star then flashes once and returns to its original shape.
            var flash = Math.Clamp((elapsed - FlightMilliseconds - index * 60) / 260, 0, 1);
            var (opacity, scale) = flash switch
            {
                < .28 => (Smooth(flash / .28), .65 + .6 * Smooth(flash / .28)),
                < .58 => (1 - .72 * Smooth((flash - .28) / .30), 1.25 - .4 * Smooth((flash - .28) / .30)),
                _ => (.28 + .72 * Smooth((flash - .58) / .42), .85 + .15 * Smooth((flash - .58) / .42))
            };
            var spark = _parts.Sparks[index];
            spark.Opacity = opacity;
            _sparkScales[index].ScaleX = _sparkScales[index].ScaleY = scale;
            spark.RenderTransform = flash == 1 ? null : _sparkScales[index];
        }
    }

    private void ReleaseFragments(double elapsed)
    {
        for (var index = 0; index < _parts.Sparks.Length; index++)
        {
            var release = FlightMilliseconds * .38 + index * 160;
            var progress = Math.Clamp((elapsed - release) / 720, 0, 1);
            var travel = Smooth(progress);
            var initialTravel = Smooth(release / FlightMilliseconds);
            var start = HeadPosition(initialTravel);
            var target = _sparkCenters[index];
            var delta = new Vector(target.X - start.X, target.Y - start.Y);
            var direction = delta / delta.Length;
            var side = index % 2 == 0 ? 1 : -1;
            var arc = new Vector(-direction.Y, direction.X) * side;
            var position = Curve(start, start + direction * 8 + arc * 10,
                target - direction * 8 + arc * 8, target, travel);
            var headReach = _headReach * (.08 + .92 * initialTravel);
            var initialScale = Math.Min(Math.Max(1.8, headReach * .55 / _sparkReaches[index]),
                headReach * .85 / _sparkReaches[index]);
            _sparkScales[index].ScaleX = _sparkScales[index].ScaleY = initialScale + (1 - initialScale) * travel;
            _sparkRotations[index].Angle = -225 * (1 - initialTravel) * (1 - travel);
            _sparkTranslations[index].X = (position.X - target.X) / 64 * _size;
            _sparkTranslations[index].Y = (position.Y - target.Y) / 64 * _size;
            var spark = _parts.Sparks[index];
            spark.Opacity = Smooth(Math.Clamp(progress / .18, 0, 1));
            spark.RenderTransform = progress == 1 ? null : _sparkMotions[index];
        }
    }

    private static Point HeadPosition(double travel)
    {
        const double trailFraction = .83;
        return travel <= trailFraction
            ? WonderMark.TrailPoint(1 - travel / trailFraction)
            : WonderMark.TrailPoint(0) + (WonderMark.HeadCenter - WonderMark.TrailPoint(0)) *
                ((travel - trailFraction) / (1 - trailFraction));
    }

    private static Point Curve(Point start, Point first, Point last, Point end, double progress)
    {
        var remaining = 1 - progress;
        return new Point(remaining * remaining * remaining * start.X + 3 * remaining * remaining * progress * first.X +
            3 * remaining * progress * progress * last.X + progress * progress * progress * end.X,
            remaining * remaining * remaining * start.Y + 3 * remaining * remaining * progress * first.Y +
            3 * remaining * progress * progress * last.Y + progress * progress * progress * end.Y);
    }

    private static double Smooth(double progress) => progress * progress * (3 - 2 * progress);
}
