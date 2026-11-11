// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Wonder.Workspace;

namespace Wonder.Desktop;

internal sealed class WorkspaceCenterOverlay(Area area) : Control
{
    public Point Origin { get; set; }
    internal bool OriginOutside => !new Rect(Bounds.Size).Contains(Origin);

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width < 24 || height < 24) return;
        using var clip = context.PushClip(new Rect(Bounds.Size));
        var pen = new Pen(Palette.Accent, 1, DashStyle.Dash);
        if (Origin.Y >= 0 && Origin.Y <= height)
            context.DrawLine(pen, new Point(0, Origin.Y), new Point(width, Origin.Y));
        if (Origin.X >= 0 && Origin.X <= width)
            context.DrawLine(pen, new Point(Origin.X, 0), new Point(Origin.X, height));
        if (!OriginOutside)
        {
            context.DrawEllipse(Palette.Background, new Pen(Palette.Accent, 2), Origin, 7, 7);
            context.DrawLine(new Pen(Palette.Accent, 2), Origin - new Vector(12, 0), Origin + new Vector(12, 0));
            context.DrawLine(new Pen(Palette.Accent, 2), Origin - new Vector(0, 12), Origin + new Vector(0, 12));
        }
        else
        {
            var middle = new Point(width / 2, height / 2);
            var vector = new Vector(Origin.X - middle.X, Origin.Y - middle.Y);
            var inset = Math.Min(22, Math.Min(width, height) / 3);
            var factor = Math.Min((width / 2 - inset) / Math.Max(.001, Math.Abs(vector.X)),
                (height / 2 - inset) / Math.Max(.001, Math.Abs(vector.Y)));
            var tip = middle + vector * factor;
            var unit = vector / vector.Length;
            var normal = new Vector(-unit.Y, unit.X);
            var geometry = new StreamGeometry();
            using (var drawing = geometry.Open())
            {
                drawing.BeginFigure(tip, true);
                drawing.LineTo(tip - unit * 17 + normal * 8);
                drawing.LineTo(tip - unit * 17 - normal * 8);
                drawing.EndFigure(true);
            }
            context.DrawGeometry(Palette.Accent, null, geometry);
        }
        var text = new FormattedText($"中心 ({area.CenterX:0}, {area.CenterY:0}) · 拖动画布平移 · Esc 退出",
            CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 12, Palette.Accent);
        context.DrawRectangle(Palette.Background, null, new Rect(8, 8, Math.Min(width - 16, text.Width + 16), 26));
        context.DrawText(text, new Point(16, 12));
    }
}
