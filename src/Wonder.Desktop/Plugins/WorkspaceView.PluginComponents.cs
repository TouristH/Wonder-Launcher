// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using System.Linq;
using Wonder.Extensibility;
using Wonder.Extensibility.Contracts;
using Wonder.Workspace;

namespace Wonder.Desktop;

public sealed partial class WorkspaceView
{
    private readonly HashSet<string> _pendingPluginPlacement = new(StringComparer.Ordinal);

    internal void RefreshPluginComponents(IReadOnlySet<string> changed)
    {
        CancelGesture();
        var known = Document.Library.Select(item => item.DefinitionId).ToHashSet(StringComparer.Ordinal);
        var definitions = Components.Registry.Definitions;
        Document.EnsureLibrary(definitions.Select(item => item.Id));
        foreach (var definition in definitions)
            if (!known.Contains(definition.Id) && definition.Desktop.PlaceOnFirstActivation)
                _pendingPluginPlacement.Add(definition.Id);
        Components.RefreshDefinitions(changed);
        Rebuild();
        PlaceNewPluginComponents(this, EventArgs.Empty);
        if (_pendingPluginPlacement.Count > 0)
        {
            LayoutUpdated -= PlaceNewPluginComponents;
            LayoutUpdated += PlaceNewPluginComponents;
        }
        NotifyChanged();
    }

    private void PlaceNewPluginComponents(object? sender, EventArgs args)
    {
        if (_disposed || _pendingPluginPlacement.Count == 0)
        {
            LayoutUpdated -= PlaceNewPluginComponents;
            return;
        }
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        if (!Document.Areas.Any()) Document.AddArea().Title = "工作区";
        var snapshot = Snapshot;
        var areas = Document.Areas.Where(area => area.SidebarEdge == SidebarEdge.None)
            .ToDictionary(area => area.Id);
        var total = Document.Areas.Sum(area => area.Components.Count);
        var placed = new List<(string AreaId, string ItemId)>();
        var occupied = areas.Values.ToDictionary(area => area.Id, area => area.Components
            .Select(item => GetComponentBounds(item.Id)).Where(rect => rect.Width > 0)
            .Select(rect => new PluginComponentPlacement.Bounds(rect.X, rect.Y, rect.Width, rect.Height)).ToList());
        var areaPositions = areas.Values.Select(area =>
        {
            var bounds = snapshot.Areas[area.Id];
            return (area.Id, bounds.X, bounds.Y, area.Components.Count);
        });
        var ordered = PluginComponentPlacement.OrderDefinitions(_pendingPluginPlacement
            .Select(id => Components.Registry.Find(id)?.Definition).OfType<ComponentDefinition>());
        foreach (var definition in ordered)
        {
            var areaId = PluginComponentPlacement.SelectArea(areaPositions, total);
            if (areaId is null) break;
            var area = areas[areaId];
            var bounds = snapshot.Areas[areaId];
            var application = Document.Library.First(item => item.DefinitionId == definition.Id);
            var shortcut = Document.CreateShortcut(application);
            var size = ComponentFootprint(shortcut, area, bounds.Width);
            var leftInset = definition.Desktop.StretchToAreaWidth ? definition.Desktop.HorizontalMargin : AreaView.Inset;
            var left = bounds.X + leftInset;
            var right = bounds.X + bounds.Width - leftInset;
            var top = bounds.Y + AreaView.HeaderHeight + definition.Desktop.TopInset * ComponentScale;
            var center = PluginComponentPlacement.FindCenter(left, right, top, size.Width, size.Height,
                definition.Desktop.StretchToAreaWidth, occupied[areaId]);
            var position = PlacementOffset(shortcut, area, bounds, size,
                center.X - bounds.X - bounds.Width / 2,
                center.Y - bounds.Y - bounds.Height / 2);
            shortcut.X = position.X - area.CenterX;
            shortcut.Y = position.Y - area.CenterY;
            area.Components.Add(shortcut);
            occupied[areaId].Add(new(bounds.X + bounds.Width / 2 + position.X - size.Width / 2,
                bounds.Y + bounds.Height / 2 + position.Y - size.Height / 2, size.Width, size.Height));
            placed.Add((area.Id, shortcut.Id));
            total++;
        }
        _pendingPluginPlacement.Clear();
        LayoutUpdated -= PlaceNewPluginComponents;
        Rebuild();
        foreach (var (areaId, itemId) in placed)
            _areas[areaId].Components[itemId].RevealOnFirstActivation();
        NotifyChanged();
    }
}
