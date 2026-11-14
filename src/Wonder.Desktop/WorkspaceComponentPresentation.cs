// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using Avalonia;
using Wonder.Workspace;

namespace Wonder.Desktop;

public sealed partial class WorkspaceView
{
    private Size ComponentFootprint(ComponentItem item, Area? area) => ComponentView.GetFootprint(
        Components.GetDefinition(item), ComponentScale,
        area?.SidebarEdge is not null and not SidebarEdge.None && Components.Registry.Find(item.DefinitionId)?.SidebarEntry is not null);

    internal void ConfigureComponentPreview(ComponentView preview, PixelPoint screenPoint)
    {
        var point = this.PointToClient(screenPoint);
        var area = new Rect(Bounds.Size).Contains(point) && AreaAt(point) is { } id ? Document.FindArea(id) : null;
        ConfigureComponentPreview(preview, area);
    }

    private void ConfigureComponentPreview(ComponentView preview, Area? area) =>
        preview.SetAreaPresentation(area?.SidebarEdge is not null and not SidebarEdge.None);
}
