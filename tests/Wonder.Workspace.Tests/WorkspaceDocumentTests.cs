// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Wonder.Workspace;

namespace Wonder.Workspace.Tests;

public sealed class WorkspaceDocumentTests
{
    [Fact]
    public void AddAreaSplitsTheSelectedAreaAndKeepsItsComponents()
    {
        var document = WorkspaceDocument.CreateEmpty();
        var first = document.AddArea();
        var component = document.CreateShortcut(document.Library.Single());
        first.Components.Add(component);
        var second = document.AddArea(first.Id, SplitAxis.Vertical);

        Assert.Null(document.Root!.Area);
        Assert.Equal(SplitAxis.Vertical, document.Root.Axis);
        Assert.Same(first, document.Root.First!.Area);
        Assert.Same(second, document.Root.Second!.Area);
        Assert.Same(component, document.FindArea(first.Id)!.Components.Single());
        Assert.Same(document.Root.First, document.FindNode(document.Root.First.Id));
    }

    [Fact]
    public void SwapAreasMovesTheWholeAreaIncludingShortcutIdentityAndPosition()
    {
        var document = WorkspaceDocument.CreateEmpty();
        var first = document.AddArea();
        var second = document.AddArea();
        var application = document.Library.Single();
        var component = document.CreateShortcut(application);
        component.X = 20;
        component.Y = 30;
        first.Components.Add(component);
        var firstLeaf = document.Root!.First!;
        var secondLeaf = document.Root.Second!;

        Assert.True(document.SwapAreas(first.Id, second.Id));
        Assert.Same(second, firstLeaf.Area);
        Assert.Same(first, secondLeaf.Area);
        Assert.Same(component, secondLeaf.Area!.Components.Single());
        Assert.Equal(20, component.X);
        Assert.Equal(30, component.Y);
        Assert.Same(application, component.Application);
        Assert.False(document.SwapAreas(first.Id, "missing"));
        Assert.Same(first, secondLeaf.Area);
    }

    [Fact]
    public void RemoveNestedAreaPromotesItsSiblingAndPreservesOtherBranch()
    {
        var document = WorkspaceDocument.CreateEmpty();
        var first = document.AddArea();
        var second = document.AddArea();
        var third = document.AddArea(second.Id, SplitAxis.Vertical);

        Assert.True(document.RemoveArea(second.Id));
        Assert.Same(first, document.Root!.First!.Area);
        Assert.Same(third, document.Root.Second!.Area);
        Assert.Equal(2, document.Areas.Count());
        Assert.False(document.RemoveArea("missing"));
        Assert.True(document.RemoveArea(first.Id));
        Assert.Same(third, document.Root.Area);
        Assert.True(document.RemoveArea(third.Id));
        Assert.Null(document.Root);
        Assert.Empty(document.Areas);
    }

    [Fact]
    public void DefaultWorkspaceContainsOneSettingsShortcutLinkedToItsApplication()
    {
        var document = WorkspaceDocument.CreateDefault();

        var area = Assert.Single(document.Areas);
        Assert.Equal("工作区", area.Title);
        var shortcut = Assert.Single(area.Components);
        Assert.Equal(ComponentIds.Settings, shortcut.DefinitionId);
        Assert.Same(Assert.Single(document.Library), shortcut.Application);
    }

    [Fact]
    public void DefaultSettingsShortcutIsCenteredInTheWholeArea()
    {
        var document = WorkspaceDocument.CreateDefault();
        var layout = WorkspaceLayout.Measure(document, 810, 610);

        Assert.Equal(3, document.Version);
        foreach (var area in document.Areas)
        {
            var bounds = layout.Areas[area.Id];
            var item = Assert.Single(area.Components);
            var topLeft = WorkspaceLayout.FromCenterOffset(bounds.Width, bounds.Height, 100, 80, item.X, item.Y);
            Assert.Equal(bounds.Width / 2, topLeft.X + 50);
            Assert.Equal(bounds.Height / 2, topLeft.Y + 40);
        }
    }
}
