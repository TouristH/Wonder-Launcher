// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Wonder.Workspace;

namespace Wonder.Desktop;

public sealed partial class WorkspaceView
{
    private readonly TextBlock _areaPlacementLabel = new()
    {
        FontSize = 16, Foreground = Palette.Accent,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
    };
    private readonly Border _areaPlacementPreview = new()
    {
        Name = "AreaPlacementPreview", Background = Palette.Active, BorderBrush = Palette.Accent,
        BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(12),
        IsHitTestVisible = false, IsVisible = false, ZIndex = 350
    };

    // The central half is reserved for swapping. Outside it, choose the closest
    // normalized edge, giving corners one deterministic direction on any aspect ratio.
    internal static AreaPlacement? PlacementAt(Rect bounds, Point point)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0 || !bounds.Contains(point)) return null;
        var x = (point.X - bounds.X) / bounds.Width;
        var y = (point.Y - bounds.Y) / bounds.Height;
        var horizontalDistance = Math.Min(x, 1 - x);
        var verticalDistance = Math.Min(y, 1 - y);
        if (horizontalDistance >= .25 && verticalDistance >= .25) return null;
        if (verticalDistance <= horizontalDistance)
            return y < .5 ? AreaPlacement.Top : AreaPlacement.Bottom;
        return x < .5 ? AreaPlacement.Left : AreaPlacement.Right;
    }

    private static string PlacementText(AreaPlacement? placement) => placement switch
    {
        AreaPlacement.Left => "放在左侧",
        AreaPlacement.Right => "放在右侧",
        AreaPlacement.Top => "放在上方",
        AreaPlacement.Bottom => "放在下方",
        _ => "交换功能区"
    };

    private void UpdateAreaDrop(Gesture gesture)
    {
        var targetId = AreaAt(gesture.Last);
        if (targetId == gesture.AreaId || !new Rect(Bounds.Size).Contains(gesture.Last)) targetId = null;
        gesture.TargetAreaId = targetId;
        gesture.Placement = targetId is null ? null : PlacementAt(ToRect(GetAreaBounds(targetId)), gesture.Last);
        _dragText.Text = targetId is null ? "拖到目标边缘摆放，中央交换 · Esc 取消"
            : "松开" + PlacementText(gesture.Placement) + " · Esc 取消";
    }

    private void UpdateAreaPlacementPreview()
    {
        var gesture = _gesture;
        _areaPlacementPreview.IsVisible = gesture is
            { Active: true, Component: null, Split: null, CenterArea: null, TargetAreaId: not null };
        if (!_areaPlacementPreview.IsVisible) return;
        _areaPlacementLabel.Text = PlacementText(gesture!.Placement);
        _areaPlacementPreview.Child ??= _areaPlacementLabel;
    }

    private void ArrangeAreaPlacementPreview(LayoutSnapshot snapshot, Size size)
    {
        if (!_areaPlacementPreview.IsVisible || _gesture?.TargetAreaId is not { } id ||
            !snapshot.Areas.TryGetValue(id, out var target)) return;
        var bounds = ToRect(target);
        var preview = new Rect(bounds.X + bounds.Width / 4, bounds.Y + bounds.Height / 4, bounds.Width / 2, bounds.Height / 2);
        if (_gesture.Placement is { } placement && _gesture.AreaId is { } sourceId)
        {
            // Preview the resulting geometry after closing the source's former gap.
            // Copy only layout metadata; components/application state are neither read nor mutated.
            var layout = new WorkspaceDocument { Root = CopyPlacementLayout(Document.Root) };
            if (!layout.MoveArea(sourceId, id, placement))
            {
                _areaPlacementPreview.IsVisible = false;
                _dragText.Text = "当前布局无法继续拆分 · Esc 取消";
                return;
            }
            preview = ToRect(WorkspaceLayout.Measure(layout, size.Width, size.Height,
                expandedSidebarId: _expandedSidebarId, expandedSidebarExtent: _settings.SidebarExtent).Areas[sourceId]);
        }
        _areaPlacementPreview.Arrange(preview.Deflate(4));
    }

    private static LayoutNode? CopyPlacementLayout(LayoutNode? node) => node is null ? null : new LayoutNode
    {
        Id = node.Id, Axis = node.Axis, Ratio = node.Ratio,
        First = CopyPlacementLayout(node.First), Second = CopyPlacementLayout(node.Second),
        Area = node.Area is not { } area ? null : new Area
        {
            Id = area.Id, SidebarEdge = area.SidebarEdge, SidebarExtent = area.SidebarExtent,
            CenterX = area.CenterX, CenterY = area.CenterY
        }
    };
}
