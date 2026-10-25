// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Wonder.Workspace;

namespace Wonder.Workspace.Tests;

public sealed class SidebarLayoutTests
{
    [Theory]
    [InlineData(SidebarEdge.Left, SplitAxis.Horizontal, true, 0, 0, 28, 610)]
    [InlineData(SidebarEdge.Right, SplitAxis.Horizontal, false, 782, 0, 28, 610)]
    [InlineData(SidebarEdge.Top, SplitAxis.Vertical, true, 0, 0, 810, 28)]
    [InlineData(SidebarEdge.Bottom, SplitAxis.Vertical, false, 0, 582, 810, 28)]
    public void CollapsedOuterLeafUsesFixedRailAndReflowsSiblingWithoutGap(SidebarEdge edge, SplitAxis axis, bool first, double x, double y, double width, double height)
    {
        var document = WorkspaceDocument.CreateEmpty();
        var firstArea = document.AddArea();
        var secondArea = document.AddArea(firstArea.Id, axis);
        var sidebar = first ? firstArea : secondArea;
        var before = WorkspaceLayout.Measure(document, 810, 610);
        Assert.Equal(edge, WorkspaceLayout.GetSidebarEdge(document.Root!, first, before.Areas[sidebar.Id], 810, 610));
        sidebar.SidebarEdge = edge;

        var snapshot = WorkspaceLayout.Measure(document, 810, 610);

        Assert.Equal(new LayoutRect(x, y, width, height), snapshot.Areas[sidebar.Id]);
        Assert.Empty(snapshot.Splits);
        Assert.Equal(810 * 610, snapshot.Areas.Values.Sum(rect => rect.Width * rect.Height));
        Assert.Equal(SidebarEdge.None, WorkspaceLayout.GetSidebarEdge(document.Root!, first, snapshot.Areas[sidebar.Id], 810, 610));
    }

    [Fact]
    public void NestedTopSidebarRetainsNormalWidthAndOnlyRemovesItsOwnDivider()
    {
        var document = WorkspaceDocument.CreateEmpty();
        var left = document.AddArea();
        var topRight = document.AddArea();
        var bottomRight = document.AddArea(topRight.Id, SplitAxis.Vertical);
        topRight.SidebarEdge = SidebarEdge.Top;

        var snapshot = WorkspaceLayout.Measure(document, 810, 610);

        Assert.Equal(new LayoutRect(0, 0, 400, 610), snapshot.Areas[left.Id]);
        Assert.Equal(new LayoutRect(410, 0, 400, 28), snapshot.Areas[topRight.Id]);
        Assert.Equal(new LayoutRect(410, 28, 400, 582), snapshot.Areas[bottomRight.Id]);
        Assert.Single(snapshot.Splits);
        Assert.Contains(document.Root!.Id, snapshot.Splits.Keys);
    }

    [Fact]
    public void MatchingNestedRailsPropagateTheirFixedWidth()
    {
        var document = WorkspaceDocument.CreateEmpty();
        var topLeft = document.AddArea();
        var right = document.AddArea();
        var bottomLeft = document.AddArea(topLeft.Id, SplitAxis.Vertical);
        topLeft.SidebarEdge = bottomLeft.SidebarEdge = SidebarEdge.Left;

        var snapshot = WorkspaceLayout.Measure(document, 810, 610);

        Assert.Equal(new LayoutRect(0, 0, 28, 300), snapshot.Areas[topLeft.Id]);
        Assert.Equal(new LayoutRect(0, 310, 28, 300), snapshot.Areas[bottomLeft.Id]);
        Assert.Equal(new LayoutRect(28, 0, 782, 610), snapshot.Areas[right.Id]);
        Assert.DoesNotContain(document.Root!.Id, snapshot.Splits.Keys);
    }

    [Fact]
    public void TwoCollapsedChildrenStayOnOppositeEdgesWithAnEmptyCenter()
    {
        var document = WorkspaceDocument.CreateEmpty();
        var left = document.AddArea();
        var right = document.AddArea();
        left.SidebarEdge = SidebarEdge.Left;
        right.SidebarEdge = SidebarEdge.Right;

        var snapshot = WorkspaceLayout.Measure(document, 810, 610);

        Assert.Equal(new LayoutRect(0, 0, 28, 610), snapshot.Areas[left.Id]);
        Assert.Equal(new LayoutRect(782, 0, 28, 610), snapshot.Areas[right.Id]);
        Assert.Empty(snapshot.Splits);
    }

    [Fact]
    public void RestoreUsesOriginalRatioAndDoesNotChangeComponentCenterOffsets()
    {
        var document = TestWorkspaceFactory.CreateThreeAreas();
        document.Root!.Ratio = .3;
        var area = document.Root.First!.Area!;
        var component = area.Components.Single();
        component.X = -90;
        component.Y = 25;
        var original = WorkspaceLayout.Measure(document, 810, 610).Areas[area.Id];
        area.SidebarEdge = SidebarEdge.Left;
        WorkspaceLayout.Measure(document, 810, 610);
        WorkspaceLayout.Measure(document, 20, 10);
        area.SidebarEdge = SidebarEdge.None;

        var restored = WorkspaceLayout.Measure(document, 810, 610);

        Assert.Equal(original, restored.Areas[area.Id]);
        Assert.Equal(.3, document.Root.Ratio);
        Assert.Equal(-90, component.X);
        Assert.Equal(25, component.Y);
    }

    [Fact]
    public void SmallViewportDoesNotAutomaticallyCollapseNormalAreas()
    {
        var document = TestWorkspaceFactory.CreateThreeAreas();

        WorkspaceLayout.Measure(document, 40, 40);

        Assert.All(document.Areas, area => Assert.Equal(SidebarEdge.None, area.SidebarEdge));
    }

    [Fact]
    public void CollapseCandidateRequiresDirectLeafOnTheRequestedOuterEdge()
    {
        var document = WorkspaceDocument.CreateEmpty();
        document.AddArea();
        var middle = document.AddArea();
        document.AddArea(middle.Id);
        var snapshot = WorkspaceLayout.Measure(document, 1010, 610);

        Assert.Equal(SidebarEdge.None, WorkspaceLayout.GetSidebarEdge(document.Root!, false, new LayoutRect(0, 0, 1010, 610), 1010, 610));
        Assert.Equal(SidebarEdge.None, WorkspaceLayout.GetSidebarEdge(document.Root!.Second!, true, snapshot.Areas[middle.Id], 1010, 610));
    }

    [Fact]
    public void SplittingASidebarRestoresItBeforeCreatingNormalLeaves()
    {
        var document = WorkspaceDocument.CreateEmpty();
        var left = document.AddArea();
        document.AddArea();
        left.SidebarEdge = SidebarEdge.Left;

        document.AddArea(left.Id, SplitAxis.Vertical);

        Assert.All(document.Areas, area => Assert.Equal(SidebarEdge.None, area.SidebarEdge));
    }

    [Fact]
    public void SwappingASidebarRestoresBothAreasAndPreservesTheirState()
    {
        var document = TestWorkspaceFactory.CreateThreeAreas();
        var first = document.Areas.First();
        var last = document.Areas.Last();
        first.SidebarEdge = SidebarEdge.Left;
        var component = first.Components.Single();
        component.X = 8;

        document.SwapAreas(first.Id, last.Id);

        Assert.Equal(SidebarEdge.None, first.SidebarEdge);
        Assert.Equal(SidebarEdge.None, last.SidebarEdge);
        Assert.Same(component, first.Components.Single());
        Assert.Equal(8, component.X);
        Assert.Same(document.Library.Single(), component.Application);
    }

    [Fact]
    public void SwappingSidebarWithItselfReturnsFalseAndPreservesItsInstanceAndMetadata()
    {
        var document = TestWorkspaceFactory.CreateThreeAreas();
        var leaf = document.Root!.First!;
        var area = leaf.Area!;
        var component = area.Components.Single();
        area.SidebarEdge = SidebarEdge.Left;
        area.SidebarExtent = 375;
        component.X = -45;
        component.Y = 20;

        Assert.False(document.SwapAreas(area.Id, area.Id));

        Assert.Same(leaf, document.Root.First);
        Assert.Same(area, leaf.Area);
        Assert.Same(component, area.Components.Single());
        Assert.Equal(SidebarEdge.Left, area.SidebarEdge);
        Assert.Equal(375, area.SidebarExtent);
        Assert.Equal(-45, component.X);
        Assert.Equal(20, component.Y);
        Assert.Same(document.Library.Single(), component.Application);
    }

    [Fact]
    public void RemovingTheOnlySiblingRestoresALoneSidebarToTheWholeWorkspace()
    {
        var document = WorkspaceDocument.CreateEmpty();
        var sidebar = document.AddArea();
        var other = document.AddArea();
        sidebar.SidebarEdge = SidebarEdge.Left;
        sidebar.SidebarExtent = 375;

        Assert.True(document.RemoveArea(other.Id));

        Assert.Same(sidebar, document.Root!.Area);
        Assert.Equal(SidebarEdge.None, sidebar.SidebarEdge);
        Assert.Equal(375, sidebar.SidebarExtent);
        Assert.Equal(new LayoutRect(0, 0, 810, 610), WorkspaceLayout.Measure(document, 810, 610).Areas[sidebar.Id]);
    }

    [Fact]
    public void RemovingNestedSiblingRestoresSidebarPromotedUnderTheOtherSplitAxis()
    {
        var document = WorkspaceDocument.CreateEmpty();
        document.AddArea();
        var topRight = document.AddArea();
        var bottomRight = document.AddArea(topRight.Id, SplitAxis.Vertical);
        topRight.SidebarEdge = SidebarEdge.Top;

        Assert.True(document.RemoveArea(bottomRight.Id));

        Assert.Same(topRight, document.Root!.Second!.Area);
        Assert.Equal(SidebarEdge.None, topRight.SidebarEdge);
        Assert.Equal(new LayoutRect(410, 0, 400, 610), WorkspaceLayout.Measure(document, 810, 610).Areas[topRight.Id]);
    }

    [Fact]
    public void RemovingUnrelatedAreaPreservesAValidOuterSidebar()
    {
        var document = WorkspaceDocument.CreateEmpty();
        var left = document.AddArea();
        var topRight = document.AddArea();
        var bottomRight = document.AddArea(topRight.Id, SplitAxis.Vertical);
        left.SidebarEdge = SidebarEdge.Left;

        Assert.True(document.RemoveArea(bottomRight.Id));

        Assert.Equal(SidebarEdge.Left, left.SidebarEdge);
        Assert.Equal(new LayoutRect(0, 0, 28, 610), WorkspaceLayout.Measure(document, 810, 610).Areas[left.Id]);
    }

    [Theory]
    [InlineData(SidebarEdge.Left, SplitAxis.Horizontal, true, 0, 0, 320, 610, 330, 0, 480, 610)]
    [InlineData(SidebarEdge.Right, SplitAxis.Horizontal, false, 490, 0, 320, 610, 0, 0, 480, 610)]
    [InlineData(SidebarEdge.Top, SplitAxis.Vertical, true, 0, 0, 810, 320, 0, 330, 810, 280)]
    [InlineData(SidebarEdge.Bottom, SplitAxis.Vertical, false, 0, 290, 810, 320, 0, 0, 810, 280)]
    public void ExpandedSidebarParticipatesInLayoutAndCompressesItsSibling(
        SidebarEdge edge, SplitAxis axis, bool first,
        double x, double y, double width, double height,
        double otherX, double otherY, double otherWidth, double otherHeight)
    {
        var document = WorkspaceDocument.CreateEmpty();
        var firstArea = document.AddArea();
        var secondArea = document.AddArea(firstArea.Id, axis);
        var sidebar = first ? firstArea : secondArea;
        var other = first ? secondArea : firstArea;
        sidebar.SidebarEdge = edge;

        var snapshot = WorkspaceLayout.Measure(document, 810, 610, expandedSidebarId: sidebar.Id);

        Assert.Equal(new LayoutRect(x, y, width, height), snapshot.Areas[sidebar.Id]);
        Assert.Equal(new LayoutRect(otherX, otherY, otherWidth, otherHeight), snapshot.Areas[other.Id]);
        Assert.Empty(snapshot.Splits);
    }

    [Theory]
    [InlineData(80, 80)]
    [InlineData(240, 240)]
    [InlineData(375, 320)]
    public void DefaultExpansionUsesBoundedPreferredSize(double preferred, double expected)
    {
        var document = WorkspaceDocument.CreateEmpty();
        var sidebar = document.AddArea();
        document.AddArea();
        sidebar.SidebarEdge = SidebarEdge.Left;
        sidebar.SidebarExtent = preferred;

        var snapshot = WorkspaceLayout.Measure(document, 810, 610, expandedSidebarId: sidebar.Id);

        Assert.Equal(expected, snapshot.Areas[sidebar.Id].Width);
        Assert.Equal(preferred, sidebar.SidebarExtent);
    }

    [Fact]
    public void ExplicitExpansionPreviewCanExceedDefaultWithoutChangingPreferredSize()
    {
        var document = WorkspaceDocument.CreateEmpty();
        var sidebar = document.AddArea();
        var other = document.AddArea();
        sidebar.SidebarEdge = SidebarEdge.Left;

        var snapshot = WorkspaceLayout.Measure(document, 810, 610, expandedSidebarId: sidebar.Id, expandedSidebarExtent: 500);

        Assert.Equal(new LayoutRect(0, 0, 500, 610), snapshot.Areas[sidebar.Id]);
        Assert.Equal(new LayoutRect(510, 0, 300, 610), snapshot.Areas[other.Id]);
        Assert.Equal(320, sidebar.SidebarExtent);
    }

    [Fact]
    public void NestedExpansionUsesItsOwnParentSpaceAndReservesSiblingMinimum()
    {
        var document = WorkspaceDocument.CreateEmpty();
        var left = document.AddArea();
        var middle = document.AddArea();
        var rightSidebar = document.AddArea(middle.Id);
        rightSidebar.SidebarEdge = SidebarEdge.Right;

        var snapshot = WorkspaceLayout.Measure(document, 810, 610, expandedSidebarId: rightSidebar.Id);

        Assert.Equal(new LayoutRect(0, 0, 400, 610), snapshot.Areas[left.Id]);
        Assert.Equal(new LayoutRect(410, 0, 160, 610), snapshot.Areas[middle.Id]);
        Assert.Equal(new LayoutRect(580, 0, 230, 610), snapshot.Areas[rightSidebar.Id]);
        Assert.Single(snapshot.Splits);
        Assert.Contains(document.Root!.Id, snapshot.Splits.Keys);
    }

    [Theory]
    [InlineData(true, 0, 72)]
    [InlineData(false, 38, 0)]
    public void ExpansionInSmallViewportReservesTheOtherCollapsedRail(bool expandFirst, double expandedX, double railX)
    {
        var document = WorkspaceDocument.CreateEmpty();
        var left = document.AddArea();
        var right = document.AddArea();
        left.SidebarEdge = SidebarEdge.Left;
        right.SidebarEdge = SidebarEdge.Right;
        var sidebar = expandFirst ? left : right;
        var other = expandFirst ? right : left;

        var snapshot = WorkspaceLayout.Measure(document, 100, 610, expandedSidebarId: sidebar.Id);

        Assert.Equal(new LayoutRect(expandedX, 0, 62, 610), snapshot.Areas[sidebar.Id]);
        Assert.Equal(new LayoutRect(railX, 0, 28, 610), snapshot.Areas[other.Id]);
        Assert.Empty(snapshot.Splits);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(40)]
    [InlineData(170)]
    [InlineData(200)]
    public void ExpandedLayoutDegradesWithoutOverlapOrLeavingTheViewport(double width)
    {
        var document = WorkspaceDocument.CreateEmpty();
        var sidebar = document.AddArea();
        var other = document.AddArea();
        sidebar.SidebarEdge = SidebarEdge.Left;

        var snapshot = WorkspaceLayout.Measure(document, width, 200, expandedSidebarId: sidebar.Id);

        Assert.Equal(2, snapshot.Areas.Count);
        foreach (var rect in snapshot.Areas.Values)
        {
            Assert.InRange(rect.X, 0, width);
            Assert.InRange(rect.Width, 0, width);
            Assert.InRange(rect.X + rect.Width, 0, width);
            Assert.Equal(200, rect.Height);
        }
        var first = snapshot.Areas[sidebar.Id];
        var second = snapshot.Areas[other.Id];
        Assert.True(first.X + first.Width <= second.X);
    }

    [Fact]
    public void RepeatedExpansionAndViewportChangesDoNotDriftPreferredSizeOrRatio()
    {
        var document = TestWorkspaceFactory.CreateThreeAreas();
        var sidebar = document.Areas.First();
        sidebar.SidebarEdge = SidebarEdge.Left;
        sidebar.SidebarExtent = 375;
        document.Root!.Ratio = .22;
        var item = sidebar.Components.Single();
        item.X = -45;
        item.Y = 20;

        for (var index = 0; index < 3; index++)
        {
            Assert.Equal(28, WorkspaceLayout.Measure(document, 810, 610).Areas[sidebar.Id].Width);
            WorkspaceLayout.Measure(document, 200, 160, expandedSidebarId: sidebar.Id);
            Assert.Equal(320, WorkspaceLayout.Measure(document, 810, 610, expandedSidebarId: sidebar.Id).Areas[sidebar.Id].Width);
        }

        Assert.Equal(375, sidebar.SidebarExtent);
        Assert.Equal(SidebarEdge.Left, sidebar.SidebarEdge);
        Assert.Equal(.22, document.Root.Ratio);
        Assert.Equal(-45, item.X);
        Assert.Equal(20, item.Y);
    }

    [Fact]
    public void ExpansionRequestForNormalAreaDoesNotOverrideItsSplitRatio()
    {
        var document = WorkspaceDocument.CreateEmpty();
        var first = document.AddArea();
        document.AddArea();
        document.Root!.Ratio = .25;

        var snapshot = WorkspaceLayout.Measure(document, 810, 610, expandedSidebarId: first.Id, expandedSidebarExtent: 500);

        Assert.Equal(new LayoutRect(0, 0, 200, 610), snapshot.Areas[first.Id]);
        Assert.Single(snapshot.Splits);
    }
}
