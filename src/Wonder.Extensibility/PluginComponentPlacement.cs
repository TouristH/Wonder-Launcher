// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using Wonder.Extensibility.Contracts;

namespace Wonder.Extensibility;

public static class PluginComponentPlacement
{
    private const int AreaCapacity = 24;
    private const int WorkspaceCapacity = 1024;
    private const double Gap = 12;

    public readonly record struct Bounds(double X, double Y, double Width, double Height);

    public static IEnumerable<ComponentDefinition> OrderDefinitions(IEnumerable<ComponentDefinition> definitions) =>
        definitions.OrderBy(definition => definition.Desktop.FirstActivationOrder).ThenBy(definition => definition.Id);

    public static string? SelectArea(IEnumerable<(string Id, double X, double Y, int ComponentCount)> areas,
        int totalComponents)
    {
        if (totalComponents >= WorkspaceCapacity) return null;
        return areas.Where(area => area.ComponentCount < AreaCapacity)
            .OrderBy(area => area.Y).ThenByDescending(area => area.X).FirstOrDefault().Id;
    }

    public static (double X, double Y) FindCenter(double left, double right, double top, double width, double height,
        bool stretchToAreaWidth, IReadOnlyList<Bounds> occupied)
    {
        var x = right - width;
        var y = top;
        var padding = Gap / 2;

        // Pack right to left and wrap below occupied cards without moving existing shortcuts.
        for (var attempt = 0; attempt < 128; attempt++)
        {
            var collision = occupied.FirstOrDefault(rect =>
                x < rect.X - padding + (rect.Width + Gap) && rect.X - padding < x + width &&
                y < rect.Y - padding + (rect.Height + Gap) && rect.Y - padding < y + height);
            if (collision.Width <= 0) break;
            var nextX = collision.X - Gap - width;
            if (nextX >= left && !stretchToAreaWidth)
            {
                x = nextX;
            }
            else
            {
                x = right - width;
                y = Math.Max(y, collision.Y + collision.Height + Gap);
            }
        }

        return (x + width / 2, y + height / 2);
    }
}
