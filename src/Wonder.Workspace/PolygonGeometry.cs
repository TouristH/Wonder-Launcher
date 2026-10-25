// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

namespace Wonder.Workspace;

public readonly record struct PolygonPoint(double X, double Y);

public static class PolygonGeometry
{
    /// <summary>Creates a regular polygon in normalized coordinates centered at (0.5, 0.5).</summary>
    public static IReadOnlyList<PolygonPoint> RegularPolygon(int sides, double rotationDegrees = -90, double radius = .5)
    {
        if (sides is < 3 or > 64)
            throw new ArgumentOutOfRangeException(nameof(sides), "边数必须介于 3 与 64 之间。");
        if (!double.IsFinite(rotationDegrees))
            throw new ArgumentOutOfRangeException(nameof(rotationDegrees), "旋转角度必须是有限数值。");
        if (!double.IsFinite(radius) || radius <= 0 || radius > .5)
            throw new ArgumentOutOfRangeException(nameof(radius), "半径必须是 (0, 0.5] 范围内的有限数值。");

        var rotation = Math.IEEERemainder(rotationDegrees, 360) * Math.PI / 180;
        var points = new PolygonPoint[sides];
        for (var index = 0; index < sides; index++)
        {
            var angle = rotation + Math.PI * 2 * index / sides;
            points[index] = new PolygonPoint(.5 + Math.Cos(angle) * radius, .5 + Math.Sin(angle) * radius);
        }
        return Array.AsReadOnly(points);
    }

    public static IReadOnlyList<PolygonPoint> Diamond() => RegularPolygon(4);
}
