// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Wonder.Extensibility.Contracts;

namespace Wonder.Desktop;

internal sealed class WorkspaceGridOverlay : Control
{
    public Point Origin { get; set; }
    public double Unit { get; set; } = ComponentGrid.Unit;

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        using var clip = context.PushClip(bounds);
        var pen = new Pen(Palette.Line, 1);
        var firstX = Origin.X - Math.Floor(Origin.X / Unit) * Unit;
        var firstY = Origin.Y - Math.Floor(Origin.Y / Unit) * Unit;
        for (var x = firstX; x < bounds.Width; x += Unit)
            context.DrawLine(pen, new Point(x, 0), new Point(x, bounds.Height));
        for (var y = firstY; y < bounds.Height; y += Unit)
            context.DrawLine(pen, new Point(0, y), new Point(bounds.Width, y));
    }
}
