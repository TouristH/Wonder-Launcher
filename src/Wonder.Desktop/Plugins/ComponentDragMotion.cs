// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia;
using Wonder.Extensibility;

namespace Wonder.Desktop;

internal sealed class ComponentDragMotion(Point origin, double delayMilliseconds, double maximumTiltDegrees)
{
    private readonly ComponentDragSampler _sampler = new(origin.X, origin.Y, delayMilliseconds, maximumTiltDegrees);

    public Point Position
    {
        get
        {
            var (x, y) = _sampler.Position;
            return new Point(x, y);
        }
    }

    public double TiltDegrees => _sampler.TiltDegrees;

    public void Record(Point position, double elapsedMilliseconds) =>
        _sampler.Record(position.X, position.Y, elapsedMilliseconds);

    public void Advance(double elapsedMilliseconds) => _sampler.Advance(elapsedMilliseconds);
}
