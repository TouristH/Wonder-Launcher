// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Wonder.Workspace;

namespace Wonder.Workspace.Tests;

public sealed class PolygonGeometryTests
{
    [Fact]
    public void DiamondTouchesTheFourNormalizedEdgeMidpoints()
    {
        var points = PolygonGeometry.Diamond();
        var expected = new[] { new PolygonPoint(.5, 0), new PolygonPoint(1, .5), new PolygonPoint(.5, 1), new PolygonPoint(0, .5) };

        Assert.Equal(4, points.Count);
        for (var index = 0; index < expected.Length; index++)
        {
            Assert.Equal(expected[index].X, points[index].X, 10);
            Assert.Equal(expected[index].Y, points[index].Y, 10);
        }
    }

    [Fact]
    public void PolygonNormalizesLargeRotationsAndStaysInsideUnitBounds()
    {
        var expected = PolygonGeometry.RegularPolygon(6);
        var rotated = PolygonGeometry.RegularPolygon(6, 360 * 1000 - 90);

        Assert.Equal(expected, rotated);
        Assert.All(rotated, point => { Assert.InRange(point.X, 0, 1); Assert.InRange(point.Y, 0, 1); });
    }

    [Theory]
    [InlineData(2, -90, .5)]
    [InlineData(65, -90, .5)]
    [InlineData(6, double.NaN, .5)]
    [InlineData(6, -90, 0)]
    [InlineData(6, -90, .6)]
    public void InvalidPolygonParametersAreRejected(int sides, double rotation, double radius)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PolygonGeometry.RegularPolygon(sides, rotation, radius));
    }
}
