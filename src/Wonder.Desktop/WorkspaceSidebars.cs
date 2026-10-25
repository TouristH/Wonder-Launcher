// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Wonder.Workspace;

namespace Wonder.Desktop;

public sealed partial class WorkspaceView
{
    private readonly Dictionary<string, Border> _sidebarTabs = new();
    private readonly DispatcherTimer _sidebarCloseTimer = new() { Interval = TimeSpan.FromMilliseconds(280) };
    private readonly Border _sidebarResize = new()
    {
        Background = Brushes.Transparent, ZIndex = 300, UseLayoutRounding = false,
        Child = new Border { Background = Palette.Line, IsHitTestVisible = false, UseLayoutRounding = false }
    };
    private string? _expandedSidebarId;
    private Point _sidebarPointer = new(-1, -1);

    private void InitializeSidebars()
    {
        InitializeSidebarAnimation();
        AutomationProperties.SetName(_sidebarResize, "调整侧边栏大小");
        ToolTip.SetTip(_sidebarResize, "拖大后恢复为普通功能区");
        _sidebarResize.PointerEntered += (_, _) => ((Border)_sidebarResize.Child!).Background = Palette.Accent;
        _sidebarResize.PointerExited += (_, _) => ((Border)_sidebarResize.Child!).Background = Palette.Line;
        _sidebarCloseTimer.Tick += (_, _) =>
        {
            if (_gesture != null || _renameDialog != null || ContextMenu?.IsOpen == true || IsOverSidebarInteraction(_sidebarPointer)) return;
            _sidebarCloseTimer.Stop();
            var from = Snapshot;
            _expandedSidebarId = null;
            StartSidebarAnimation(from);
            InvalidateArrange();
        };
        PointerExited += (_, e) => { _sidebarPointer = e.GetPosition(this); ScheduleSidebarClose(); };
        DetachedFromVisualTree += (_, _) => _sidebarCloseTimer.Stop();
    }

    private void RebuildSidebars()
    {
        _sidebarTabs.Clear();
        if (Document.FindArea(_expandedSidebarId ?? "")?.SidebarEdge is null or SidebarEdge.None)
            _expandedSidebarId = null;
        foreach (var area in Document.Areas.Where(area => area.SidebarEdge != SidebarEdge.None))
        {
            var vertical = area.SidebarEdge is SidebarEdge.Left or SidebarEdge.Right;
            var label = new TextBlock
            {
                Text = vertical ? VerticalSidebarTitle(area.Title) : area.Title,
                FontSize = 11, Foreground = Palette.Muted,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var tab = new Border { Background = Palette.Surface, BorderBrush = Palette.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Child = label, ClipToBounds = true, ZIndex = 210 };
            AutomationProperties.SetName(tab, "侧边栏 " + area.Title);
            ToolTip.SetTip(tab, area.Title + "\n悬停展开 · 右键可重命名 · 拖大展开区域可恢复功能区");
            tab.PointerEntered += (_, e) => { _sidebarPointer = e.GetPosition(this); OpenSidebar(area.Id); };
            _sidebarTabs.Add(area.Id, tab);
            Children.Add(tab);
        }
        Children.Add(_sidebarResize);
    }

    private static string VerticalSidebarTitle(string title)
    {
        var elements = StringInfo.GetTextElementEnumerator(title);
        var lines = new List<string>();
        while (lines.Count < 10 && elements.MoveNext()) lines.Add(elements.GetTextElement());
        return string.Join("\n", lines);
    }

    private LayoutSnapshot MeasureWorkspaceTarget(Size size) => WorkspaceLayout.Measure(Document, size.Width, size.Height,
        expandedSidebarId: _expandedSidebarId,
        expandedSidebarExtent: _gesture?.SidebarArea != null ? _gesture.SidebarPreviewExtent : _settings.SidebarExtent);

    internal LayoutRect GetAreaBounds(string id) => Snapshot.Areas[id];

    private bool IsOverExpandedSidebar(Point point) => _expandedSidebarId != null &&
        Document.FindArea(_expandedSidebarId)?.SidebarEdge is not (null or SidebarEdge.None) &&
        ToRect(GetAreaBounds(_expandedSidebarId)).Contains(point);

    private bool IsOverSidebarInteraction(Point point) => IsOverExpandedSidebar(point) ||
        IsOverSidebarDivider(point);

    private bool IsOverSidebarDivider(Point point) => _expandedSidebarId != null &&
        _sidebarResize.IsVisible && _sidebarResize.Bounds.Contains(point);

    private void OpenSidebar(string id)
    {
        _sidebarCloseTimer.Stop();
        if (_expandedSidebarId == id) return;
        if (_gesture is { Split: not null }) return;
        var from = Snapshot;
        _expandedSidebarId = id;
        StartSidebarAnimation(from);
        InvalidateArrange();
    }

    private void UpdateSidebarHover(Point point)
    {
        _sidebarPointer = point;
        foreach (var id in _sidebarTabs.Keys)
            if (Snapshot.Areas.TryGetValue(id, out var rail) && ToRect(rail).Contains(point))
            {
                OpenSidebar(id);
                return;
            }
        if (IsOverSidebarInteraction(point) || _gesture != null) _sidebarCloseTimer.Stop();
        else ScheduleSidebarClose();
    }

    private void ScheduleSidebarClose()
    {
        if (_expandedSidebarId != null && !_sidebarCloseTimer.IsEnabled) _sidebarCloseTimer.Start();
    }

    private Rect SidebarResizeBounds()
    {
        if (_expandedSidebarId == null) return default;
        var area = Document.FindArea(_expandedSidebarId)!;
        var bounds = _areas[area.Id].Bounds;
        var vertical = area.SidebarEdge is SidebarEdge.Left or SidebarEdge.Right;
        var gap = double.PositiveInfinity;
        var hasNeighbor = false;
        foreach (var (id, view) in _areas)
        {
            if (id == area.Id) continue;
            var other = view.IsVisible ? view.Bounds : _sidebarTabs[id].Bounds;
            var overlaps = vertical ? other.Bottom > bounds.Top && other.Top < bounds.Bottom
                : other.Right > bounds.Left && other.Left < bounds.Right;
            if (!overlaps) continue;
            var distance = area.SidebarEdge switch
            {
                SidebarEdge.Left => other.Left - bounds.Right,
                SidebarEdge.Right => bounds.Left - other.Right,
                SidebarEdge.Top => other.Top - bounds.Bottom,
                _ => bounds.Top - other.Bottom
            };
            if (distance < -.001) continue;
            hasNeighbor = true;
            gap = Math.Min(gap, Math.Max(0, distance));
        }
        // Use arranged edges, including their DPI rounding, without rounding the
        // handle and centered line separately. Opposite closed rails may leave
        // a large empty center: keep the handle next to the expanded sidebar.
        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        if (gap > WorkspaceLayout.DefaultGap + 2 / scale) gap = WorkspaceLayout.DefaultGap;
        var halfGap = hasNeighbor ? gap / 2 : 0;
        const double halfHitWidth = 4;
        return area.SidebarEdge switch
        {
            SidebarEdge.Left => new Rect(bounds.Right + halfGap - halfHitWidth, bounds.Y, 8, bounds.Height),
            SidebarEdge.Right => new Rect(bounds.Left - halfGap - halfHitWidth, bounds.Y, 8, bounds.Height),
            SidebarEdge.Top => new Rect(bounds.X, bounds.Bottom + halfGap - halfHitWidth, bounds.Width, 8),
            SidebarEdge.Bottom => new Rect(bounds.X, bounds.Top - halfGap - halfHitWidth, bounds.Width, 8),
            _ => default
        };
    }

    private void ArrangeSidebars(LayoutSnapshot snapshot)
    {
        foreach (var (id, tab) in _sidebarTabs)
        {
            tab.IsVisible = !IsSidebarContentVisible(id);
            tab.Arrange(ToRect(snapshot.Areas[id]));
        }
        _sidebarResize.IsVisible = _expandedSidebarId != null;
        if (_expandedSidebarId == null) return;
        var edge = Document.FindArea(_expandedSidebarId)!.SidebarEdge;
        var vertical = edge is SidebarEdge.Left or SidebarEdge.Right;
        _sidebarResize.Cursor = new Cursor(vertical ? StandardCursorType.SizeWestEast : StandardCursorType.SizeNorthSouth);
        var line = (Border)_sidebarResize.Child!;
        line.Width = vertical ? 2 : double.NaN;
        line.Height = vertical ? double.NaN : 2;
        line.HorizontalAlignment = vertical ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        line.VerticalAlignment = vertical ? VerticalAlignment.Stretch : VerticalAlignment.Center;
        _sidebarResize.Arrange(SidebarResizeBounds());
    }

    private bool PrepareSidebarResize(Gesture gesture, Point position)
    {
        if (_expandedSidebarId == null || !IsOverSidebarDivider(position)) return false;
        var area = Document.FindArea(_expandedSidebarId)!;
        var parent = ParentOf(Document.Root, area.Id);
        if (parent == null) return false;
        gesture.SidebarArea = area;
        gesture.Split = parent;
        gesture.OriginalRatio = parent.Ratio;
        gesture.SplitBounds = NodeBounds(parent, Snapshot);
        var bounds = GetAreaBounds(area.Id);
        gesture.InitialSidebarExtent = gesture.SidebarPreviewExtent = area.SidebarEdge is SidebarEdge.Left or SidebarEdge.Right ? bounds.Width : bounds.Height;
        return true;
    }

    private void ResizeExpandedSidebar(Gesture gesture)
    {
        var edge = gesture.SidebarArea!.SidebarEdge;
        var horizontal = edge is SidebarEdge.Left or SidebarEdge.Right;
        var delta = horizontal ? gesture.Last.X - gesture.Start.X : gesture.Last.Y - gesture.Start.Y;
        if (edge is SidebarEdge.Right or SidebarEdge.Bottom) delta = -delta;
        // Shrinking a temporary sidebar is a no-op; only enlarging can pin it open.
        gesture.SidebarPreviewExtent = Math.Clamp(gesture.InitialSidebarExtent + delta, gesture.InitialSidebarExtent,
            Math.Max(gesture.InitialSidebarExtent, horizontal ? gesture.SplitBounds.Width : gesture.SplitBounds.Height));
        var bounds = MeasureWorkspaceTarget(Bounds.Size).Areas[gesture.SidebarArea.Id];
        var canRestore = (horizontal ? bounds.Width : bounds.Height) > gesture.InitialSidebarExtent + 8;
        _dragText.Text = canRestore ? "松开恢复为普通功能区 · Esc 取消" : "松开保持侧边栏 · Esc 取消";
    }

    private void FindSidebarCollapse(Gesture gesture, double firstLength, double available)
    {
        gesture.CollapseArea = null;
        gesture.CollapseEdge = SidebarEdge.None;
        var split = gesture.Split!;
        foreach (var first in new[] { true, false })
        {
            var candidate = (first ? split.First : split.Second)?.Area;
            if (candidate == null || (first ? firstLength : available - firstLength) > WorkspaceLayout.SidebarCollapseThreshold) continue;
            var bounds = gesture.InitialSnapshot!.Areas[candidate.Id];
            var edge = WorkspaceLayout.GetSidebarEdge(split, first, bounds, Bounds.Width, Bounds.Height);
            if (edge == SidebarEdge.None) continue;
            gesture.CollapseArea = candidate;
            gesture.CollapseEdge = edge;
            break;
        }
        _dragText.Text = gesture.CollapseArea != null ? "松开收起为侧边栏 · Esc 取消" : "调整大小 · Esc 取消";
    }

    private bool CommitSidebarGesture(Gesture gesture)
    {
        var from = Snapshot;
        var sourceOpacity = CaptureSidebarOpacity();
        if (gesture.SidebarArea is { } expanded)
        {
            var horizontal = expanded.SidebarEdge is SidebarEdge.Left or SidebarEdge.Right;
            var delta = horizontal ? gesture.Last.X - gesture.Start.X : gesture.Last.Y - gesture.Start.Y;
            if (Math.Abs(delta) < .001)
            {
                EndGesture();
                return true;
            }
            ResizeExpandedSidebar(gesture);
            var bounds = MeasureWorkspaceTarget(Bounds.Size).Areas[expanded.Id];
            var extent = horizontal ? bounds.Width : bounds.Height;
            if (extent <= gesture.InitialSidebarExtent + 8)
            {
                EndGesture();
                return true;
            }
            if (extent > gesture.InitialSidebarExtent + 8)
            {
                var available = Math.Max(1, (horizontal ? gesture.SplitBounds.Width : gesture.SplitBounds.Height) - 10);
                var fraction = Math.Clamp(extent / available, .01, .99);
                gesture.Split!.Ratio = gesture.Split.First!.Area == expanded ? fraction : 1 - fraction;
                expanded.SidebarEdge = SidebarEdge.None;
                _expandedSidebarId = null;
            }
            expanded.SidebarExtent = Math.Max(80, extent);
        }
        else if (gesture.CollapseArea is { } area)
        {
            // Keep the preferred thickness independent of reflowed region bounds.
            area.SidebarExtent = Math.Clamp(area.SidebarExtent, 80, WorkspaceLayout.DefaultSidebarExtent);
            area.SidebarEdge = gesture.CollapseEdge;
            gesture.Split!.Ratio = gesture.OriginalRatio;
            _expandedSidebarId = null;
        }
        else return false;
        EndGesture();
        Rebuild();
        StartSidebarAnimation(from, sourceOpacity);
        NotifyChanged();
        return true;
    }

    private static LayoutNode? ParentOf(LayoutNode? node, string areaId)
    {
        if (node == null || node.Area != null) return null;
        if (node.First?.Area?.Id == areaId || node.Second?.Area?.Id == areaId) return node;
        return ParentOf(node.First, areaId) ?? ParentOf(node.Second, areaId);
    }
}
