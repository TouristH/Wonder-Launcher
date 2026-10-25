// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Wonder.Workspace;

namespace Wonder.Workspace.Tests;

public sealed class WorkspaceLayoutTests
{
    [Theory]
    [InlineData(600, 400, 210, 185)]
    [InlineData(200, 150, 10, 60)]
    [InlineData(80, 60, -50, 15)]
    public void FixedCenterOffsetFollowsAreaCenterAcrossResizesWithoutClamping(double areaWidth, double areaHeight, double expectedX, double expectedY)
    {
        var position = WorkspaceLayout.FromCenterOffset(areaWidth, areaHeight, 100, 80, -40, 25);

        Assert.Equal((expectedX, expectedY), position);
    }

    [Theory]
    [InlineData(600, 400, 210, 185)]
    [InlineData(200, 150, 10, 60)]
    [InlineData(80, 60, -50, 15)]
    public void TopLeftConversionRetainsSignedCenterOffsetAcrossAreaSizes(double areaWidth, double areaHeight, double topLeftX, double topLeftY)
    {
        var offset = WorkspaceLayout.ToCenterOffset(areaWidth, areaHeight, 100, 80, topLeftX, topLeftY);

        Assert.Equal((-40d, 25d), offset);
    }

    [Fact]
    public void CenterConversionsNormalizeInvalidLengthsAndNonfiniteCoordinates()
    {
        Assert.Equal((0d, 0d), WorkspaceLayout.FromCenterOffset(double.NaN, -10, double.PositiveInfinity, -20, double.NaN, double.PositiveInfinity));
        Assert.Equal((0d, 0d), WorkspaceLayout.ToCenterOffset(double.NaN, -10, double.PositiveInfinity, -20, double.NaN, double.PositiveInfinity));
    }

    [Fact]
    public void NestedPartitionsFillViewportWithoutOverlap()
    {
        var document = WorkspaceDocument.CreateEmpty();
        var left = document.AddArea();
        var right = document.AddArea();
        var bottom = document.AddArea(right.Id, SplitAxis.Vertical);

        var layout = WorkspaceLayout.Measure(document, 810, 610);

        Assert.Equal(new LayoutRect(0, 0, 400, 610), layout.Areas[left.Id]);
        Assert.Equal(new LayoutRect(410, 0, 400, 300), layout.Areas[right.Id]);
        Assert.Equal(new LayoutRect(410, 310, 400, 300), layout.Areas[bottom.Id]);
        Assert.Equal(new LayoutRect(400, 0, 10, 610), layout.Splits[document.Root!.Id]);
        Assert.Equal(810 * 610, layout.Areas.Values.Concat(layout.Splits.Values).Sum(rect => rect.Width * rect.Height));
    }

    [Fact]
    public void ResizeReservesRecursiveMinimumWidthForNestedChildren()
    {
        var document = WorkspaceDocument.CreateEmpty();
        var first = document.AddArea();
        var middle = document.AddArea();
        var last = document.AddArea(middle.Id);
        document.Root!.Ratio = .95;
        document.Root.Second!.Ratio = .01;

        var layout = WorkspaceLayout.Measure(document, 810, 500);

        Assert.Equal(470, layout.Areas[first.Id].Width);
        Assert.Equal(160, layout.Areas[middle.Id].Width);
        Assert.Equal(160, layout.Areas[last.Id].Width);
        Assert.Equal(.95, document.Root.Ratio);
        Assert.Equal(.01, document.Root.Second.Ratio);
    }

    [Fact]
    public void ResizeReservesMinimumHeightAndSmallViewportKeepsSavedRatio()
    {
        var document = WorkspaceDocument.CreateEmpty();
        var first = document.AddArea();
        var second = document.AddArea(first.Id, SplitAxis.Vertical);
        document.Root!.Ratio = .9;

        var normal = WorkspaceLayout.Measure(document, 800, 410);
        var small = WorkspaceLayout.Measure(document, 70, 60);

        Assert.Equal(280, normal.Areas[first.Id].Height);
        Assert.Equal(120, normal.Areas[second.Id].Height);
        Assert.Equal(25, small.Areas[first.Id].Height);
        Assert.Equal(25, small.Areas[second.Id].Height);
        Assert.Equal(.9, document.Root.Ratio);
    }

    [Theory]
    [InlineData(-10, 900, 100, 80, 300, 200, 0, 120)]
    [InlineData(50, 60, 100, 80, 300, 200, 50, 60)]
    [InlineData(25, 25, 100, 80, 30, 20, 0, 0)]
    [InlineData(double.NaN, double.PositiveInfinity, 100, 80, 300, 200, 0, 0)]
    public void ComponentPositionsStayInsideTheAvailableTravel(double x, double y, double width, double height, double areaWidth, double areaHeight, double expectedX, double expectedY)
    {
        Assert.Equal((expectedX, expectedY), WorkspaceLayout.ClampComponent(x, y, width, height, areaWidth, areaHeight));
    }

    [Fact]
    public void ZeroViewportNeverProducesNegativeOrNonfiniteRects()
    {
        var layout = WorkspaceLayout.Measure(TestWorkspaceFactory.CreateThreeAreas(), double.NaN, -5);

        Assert.All(layout.Areas.Values.Concat(layout.Splits.Values), rect => Assert.Equal(new LayoutRect(0, 0, 0, 0), rect));
    }
}
