// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Wonder.Desktop;

/// <summary>
/// The caption-button glyphs, drawn rather than typed. A text character sits on the font baseline
/// and cannot be centred optically, and a stretched <see cref="Avalonia.Controls.Shapes.Path"/>
/// collapses when its geometry has no height (the minimise bar). Drawing against the control's own
/// bounds keeps every glyph centred, equal-weight, and proportional when the button size changes.
/// </summary>
internal sealed class WindowGlyph : Control
{
    public enum Glyph { Minimize, Maximize, Restore, Close }

    private Glyph _kind;

    public WindowGlyph(Glyph kind) => _kind = kind;

    public Glyph Kind
    {
        get => _kind;
        set { if (_kind == value) return; _kind = value; InvalidateVisual(); }
    }

    /// <summary>Swapped on hover so the glyph reads as active along with its background.</summary>
    public IBrush Stroke { get; set; } = Palette.Muted;

    /// <summary>Ratio of the button's shorter side the glyph occupies; keeps weight consistent.</summary>
    private const double Proportion = .26;

    public override void Render(DrawingContext context)
    {
        var side = Math.Min(Bounds.Width, Bounds.Height);
        if (side <= 0) return;
        var size = Math.Clamp(side * Proportion, 7, 18);
        var half = size / 2;
        var centre = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var pen = new Pen(Stroke, Math.Max(1, size / 11), lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

        switch (Kind)
        {
            case Glyph.Minimize:
                context.DrawLine(pen, new Point(centre.X - half, centre.Y), new Point(centre.X + half, centre.Y));
                break;
            case Glyph.Maximize:
                context.DrawRectangle(null, pen, new Rect(centre.X - half, centre.Y - half, size, size));
                break;
            case Glyph.Restore:
                // The back pane, then the front one on top of it.
                var offset = size * .22;
                context.DrawRectangle(null, pen, new Rect(centre.X - half + offset, centre.Y - half - offset * .2, size * .8, size * .8));
                context.DrawRectangle(null, pen, new Rect(centre.X - half - offset * .2, centre.Y - half + offset, size * .8, size * .8));
                break;
            case Glyph.Close:
                context.DrawLine(pen, new Point(centre.X - half, centre.Y - half), new Point(centre.X + half, centre.Y + half));
                context.DrawLine(pen, new Point(centre.X + half, centre.Y - half), new Point(centre.X - half, centre.Y + half));
                break;
        }
    }
}
