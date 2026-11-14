// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using System.Linq;
using Avalonia;
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
            .OrderBy(area => snapshot.Areas[area.Id].Y).ThenByDescending(area => snapshot.Areas[area.Id].X)
            .ToArray();
        var total = Document.Areas.Sum(area => area.Components.Count);
        var placed = new List<(string AreaId, string ItemId)>();
        var occupied = areas.ToDictionary(area => area.Id, area => area.Components
            .Select(item => GetComponentBounds(item.Id)).Where(rect => rect.Width > 0).ToList());
        var ordered = _pendingPluginPlacement.Select(id => Components.Registry.Find(id)?.Definition)
            .Where(definition => definition is not null).Select(definition => definition!)
            .OrderBy(definition => definition.Desktop.FirstActivationOrder).ThenBy(definition => definition.Id);
        foreach (var definition in ordered)
        {
            // Match ordinary UI placement capacity; a full/collapsed desktop keeps the library entry.
            var area = areas.FirstOrDefault(area => area.Components.Count < 24);
            if (area is null || total >= 1024) break;
            var bounds = snapshot.Areas[area.Id];
            var application = Document.Library.First(item => item.DefinitionId == definition.Id);
            var shortcut = Document.CreateShortcut(application);
            var size = ComponentFootprint(shortcut, area, bounds.Width);
            var leftInset = definition.Desktop.StretchToAreaWidth ? definition.Desktop.HorizontalMargin : AreaView.Inset;
            var left = bounds.X + leftInset;
            var right = bounds.X + bounds.Width - leftInset;
            var top = bounds.Y + AreaView.HeaderHeight + definition.Desktop.TopInset * ComponentScale;
            var candidate = new Rect(right - size.Width, top, size.Width, size.Height);
            // Pack first-activation cards right to left, wrapping below occupied cards.
            // Existing shortcuts participate in collision checks but are never moved.
            for (var attempt = 0; attempt < 128; attempt++)
            {
                var collision = occupied[area.Id].FirstOrDefault(rect => rect.Inflate(6).Intersects(candidate));
                if (collision.Width <= 0) break;
                var nextX = collision.X - 12 - size.Width;
                candidate = nextX >= left && !definition.Desktop.StretchToAreaWidth
                    ? new Rect(nextX, candidate.Y, size.Width, size.Height)
                    : new Rect(right - size.Width, Math.Max(candidate.Y, collision.Bottom + 12), size.Width, size.Height);
            }
            var position = PlacementOffset(shortcut, area, bounds, size,
                candidate.Center.X - bounds.X - bounds.Width / 2,
                candidate.Center.Y - bounds.Y - bounds.Height / 2);
            shortcut.X = position.X - area.CenterX;
            shortcut.Y = position.Y - area.CenterY;
            area.Components.Add(shortcut);
            occupied[area.Id].Add(new Rect(bounds.X + bounds.Width / 2 + position.X - size.Width / 2,
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
