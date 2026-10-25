// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Wonder.Workspace;

namespace Wonder.Desktop.Tests;

internal static class TestWorkspaceFactory
{
    public static WorkspaceDocument CreateThreeAreas()
    {
        var document = WorkspaceDocument.CreateEmpty();
        var first = document.AddArea();
        var second = document.AddArea(first.Id);
        var third = document.AddArea(second.Id, SplitAxis.Vertical);
        foreach (var area in new[] { first, second, third })
            area.Components.Add(document.CreateShortcut(document.Library.Single(item => item.DefinitionId == ComponentIds.Settings)));
        return document;
    }
}
