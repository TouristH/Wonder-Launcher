// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering;

namespace Wonder.Desktop;

internal sealed class PolygonButton : Button, ICustomHitTest
{
    private readonly StreamGeometry _geometry;
    private readonly Polygon _polygon;
    private readonly ClickRipple _ripple;
    private Point? _clickPosition;
    private IBrush _restingFill = Palette.Active;
    private bool _selected;

    public PolygonButton(Points points, Size size, Control content)
    {
        Width = size.Width;
        Height = size.Height;
        Padding = new Thickness(0);
        FocusAdorner = null;
        _geometry = new StreamGeometry();
        using (var context = _geometry.Open())
        {
            context.BeginFigure(points[0], true);
            foreach (var point in points.Skip(1)) context.LineTo(point);
            context.EndFigure(true);
        }

        _polygon = new Polygon { Points = points, IsHitTestVisible = false };
        _ripple = new ClickRipple(_geometry) { IsHitTestVisible = false };
        Template = new FuncControlTemplate<PolygonButton>((_, _) =>
        {
            var surface = new Grid { IsHitTestVisible = false };
            surface.Children.Add(_polygon);
            surface.Children.Add(_ripple);
            surface.Children.Add(content);
            return surface;
        });
        UpdateHighlight();
    }

    public void SetRestingFill(IBrush fill)
    {
        _restingFill = fill;
        UpdateHighlight();
    }

    public void SetSelected(bool selected)
    {
        _selected = selected;
        UpdateHighlight();
    }

    public bool ContainsLocal(Point point) => _geometry.FillContains(point);

    // Avalonia 12's compositor transforms the point into this visual's coordinates before this call.
    bool ICustomHitTest.HitTest(Point point) => ContainsLocal(point);

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        _clickPosition = e.GetPosition(this);
        try { base.OnPointerReleased(e); }
        finally { _clickPosition = null; }
    }

    protected override void OnClick()
    {
        // Only a completed click reaches here; captured drags and cancelled presses do not.
        _ripple.Start(_clickPosition ?? new Point(Bounds.Width / 2, Bounds.Height / 2));
        base.OnClick();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsPointerOverProperty || change.Property == IsPressedProperty || change.Property == IsFocusedProperty)
            UpdateHighlight();
    }

    private void UpdateHighlight()
    {
        if (_polygon is null) return;
        var emphasized = _selected || IsPressed || IsFocused;
        _polygon.Fill = IsPressed || _selected ? Palette.Pressed : IsPointerOver || IsFocused ? Palette.Hover : _restingFill;
        _polygon.Stroke = emphasized ? Palette.Emphasis : Palette.Accent;
        _polygon.StrokeThickness = emphasized ? 2.6 : IsPointerOver ? 1.8 : 1.2;
    }
}
