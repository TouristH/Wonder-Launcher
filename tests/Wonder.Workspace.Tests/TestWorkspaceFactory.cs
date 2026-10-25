// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Wonder.Workspace;

namespace Wonder.Workspace.Tests;

internal static class TestWorkspaceFactory
{
    public static WorkspaceDocument CreateThreeAreas()
    {
        var document = WorkspaceDocument.CreateEmpty();
        var left = document.AddArea();
        var upperRight = document.AddArea(left.Id);
        document.AddArea(upperRight.Id, SplitAxis.Vertical);
        var settings = document.Library.Single();
        foreach (var area in document.Areas)
            area.Components.Add(document.CreateShortcut(settings));
        return document;
    }
}
