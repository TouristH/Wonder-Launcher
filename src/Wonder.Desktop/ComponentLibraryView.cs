// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Wonder.Workspace;

namespace Wonder.Desktop;

internal sealed class ComponentLibraryView : Border
{
    private readonly WorkspaceView _workspace;
    private ComponentItem[] _items;
    private readonly TextBox _search;
    private readonly WrapPanel _cards;
    private readonly TextBlock _status;
    private readonly List<ComponentView> _views = [];
    private readonly Canvas _dragLayer = new() { IsHitTestVisible = false, ClipToBounds = true };
    private ComponentView? _dragPreview;
    private CapturedDrag? _capturedDrag;
    private bool _runtimeVisible = true;

    private sealed record CapturedDrag(Control Source, IPointer Pointer, ComponentItem Application, Vector GrabOffset,
        ComponentView? Component, FloatingComponentPreview? Preview, IDisposable Suppression);

    internal void RefreshItems(ComponentItem[] items)
    {
        if (_capturedDrag is not null) FinishCapturedDrag(false);
        _items = items;
        RefreshCards();
    }

    public ComponentLibraryView(WorkspaceView workspace, ComponentItem[] items, Action close)
    {
        _workspace = workspace;
        _items = items;
        Background = Palette.Background;
        Padding = new Thickness(24, 20);
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto") };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 24) };
        var headings = new StackPanel { Spacing = 8 };
        headings.Children.Add(new TextBlock { Text = "组件库", FontSize = 28, FontWeight = FontWeight.SemiBold });
        headings.Children.Add(new TextBlock { Text = "在这里直接使用，也可拖到功能区。", Foreground = Palette.Muted, FontSize = 13, TextWrapping = TextWrapping.Wrap });
        header.Children.Add(headings);
        var back = new Button { Content = "收起组件库", VerticalAlignment = VerticalAlignment.Top, Padding = new Thickness(12, 10) };
        AutomationProperties.SetName(back, "返回工作区");
        back.Click += (_, _) => close();
        Grid.SetColumn(back, 1);
        header.Children.Add(back);
        layout.Children.Add(header);

        _search = new TextBox { PlaceholderText = "搜索组件", HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 22) };
        AutomationProperties.SetName(_search, "搜索组件");
        Grid.SetRow(_search, 1);
        layout.Children.Add(_search);

        _cards = new WrapPanel { Orientation = Orientation.Horizontal };
        var scroll = new ScrollViewer { Content = _cards, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 2);
        layout.Children.Add(scroll);
        _status = new TextBlock { Text = UsageHint, Foreground = Palette.Muted, FontSize = 12, Margin = new Thickness(0, 16, 0, 0), TextWrapping = TextWrapping.Wrap };
        Grid.SetRow(_status, 3);
        layout.Children.Add(_status);
        Child = new Grid { Children = { layout, _dragLayer } };
        _search.TextChanged += (_, _) => RefreshCards();
        RefreshCards();
        Focusable = OperatingSystem.IsLinux();
        IsTabStop = false;
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape || _capturedDrag is null) return;
            e.Handled = true;
            FinishCapturedDrag(false);
        }, RoutingStrategies.Tunnel, true);
        AttachedToVisualTree += (_, _) => _workspace.ComponentLibraryDropTarget = this;
        DetachedFromVisualTree += (_, _) => Detach();
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragEnterEvent, ShortcutDragOver, RoutingStrategies.Bubble, true);
        AddHandler(DragDrop.DragOverEvent, ShortcutDragOver, RoutingStrategies.Bubble, true);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) =>
        {
            EndPreview();
            _status.Text = UsageHint;
        }, RoutingStrategies.Bubble, true);
        AddHandler(DragDrop.DropEvent, ShortcutDrop, RoutingStrategies.Bubble, true);
    }

    private const string UsageHint = "拖出创建快捷方式；把功能区组件拖回此页可移除该快捷方式，应用与数据保留。";

    public void Detach()
    {
        FinishCapturedDrag(false);
        if (_workspace.ComponentLibraryDropTarget == this) _workspace.ComponentLibraryDropTarget = null;
        EndPreview();
        foreach (var view in _views) view.Dispose();
        _views.Clear();
    }

    internal void SetRuntimeVisible(bool visible)
    {
        _runtimeVisible = visible;
        if (!visible) FinishCapturedDrag(false);
        foreach (var view in _views) view.SetRuntimeVisible(visible);
    }

    private bool Accepts(WorkspaceComponentTransfer? data) => data != null &&
        data.Source == _workspace && data.Document == _workspace.Document && _workspace.ContainsShortcut(data.Shortcut);

    private void ShortcutDragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetValue(LibraryComponentTransfer.Format) is { } copy && _workspace.Document.Library.Contains(copy.Application))
        {
            if (!copy.UsesFloatingPreview) ShowPreview(copy.Application, e.GetPosition(_dragLayer), copy.GrabOffset);
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        var move = e.DataTransfer.TryGetValue(WorkspaceComponentTransfer.Format);
        var accepted = e.DragEffects.HasFlag(DragDropEffects.Move) && Accepts(move);
        if (accepted && !move!.UsesFloatingPreview) ShowPreview(move.Shortcut, e.GetPosition(_dragLayer), move.GrabOffset);
        else EndPreview();
        e.DragEffects = accepted ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
        _status.Text = accepted ? "松开移除此快捷方式 · 库内应用和共享数据保留 · Esc 取消" : UsageHint;
    }

    private void ShortcutDrop(object? sender, DragEventArgs e)
    {
        var data = e.DataTransfer.TryGetValue(WorkspaceComponentTransfer.Format);
        var removed = e.DragEffects.HasFlag(DragDropEffects.Move) && Accepts(data) && _workspace.RemoveShortcut(data!.Shortcut);
        EndPreview();
        e.DragEffects = removed ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
        _status.Text = removed ? "已移除这处快捷方式，库内应用和其他快捷方式仍可继续使用。" : UsageHint;
    }

    private void RefreshCards()
    {
        FinishCapturedDrag(false);
        foreach (var view in _views) view.Dispose();
        _views.Clear();
        _cards.Children.Clear();
        var query = _search.Text?.Trim() ?? "";
        foreach (var item in _items)
        {
            var entry = _workspace.Components.GetDefinition(item);
            if (!entry.Title.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !entry.Description.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !entry.Id.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
            var card = new StackPanel { Spacing = 12, Margin = new Thickness(20) };
            var scale = Math.Min(1, 252 / entry.Width);
            var preview = new ComponentView(item, _workspace.Components)
            {
                Width = entry.Width * scale,
                Height = entry.Height * scale, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            preview.SetRuntimeVisible(_runtimeVisible);
            _views.Add(preview);
            ToolTip.SetTip(preview, "点击使用 · 拖动到功能区创建快捷方式");
            WireDrag(preview, item, entry.Title, preview);
            var handle = new Border { Background = Brushes.Transparent, Padding = new Thickness(0, 4, 0, 8), Cursor = new Cursor(StandardCursorType.Hand), Child = new TextBlock { Text = "⠿  " + entry.Title + "  ↗", FontSize = 17, FontWeight = FontWeight.SemiBold } };
            AutomationProperties.SetName(handle, "拖动" + entry.Title);
            ToolTip.SetTip(handle, "拖到功能区创建快捷方式");
            WireDrag(handle, item, entry.Title);
            card.Children.Add(handle);
            var previewHeight = entry.Id is ComponentIds.Instances or ComponentIds.Accounts or ComponentIds.Downloads ? preview.Height : Math.Max(184, preview.Height);
            card.Children.Add(new Border { Height = previewHeight, Child = preview });
            card.Children.Add(new TextBlock { Text = entry.Description, Foreground = Palette.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, MinHeight = 38 });
            _cards.Children.Add(new Border { Width = 292, Background = Palette.Surface, BorderBrush = Palette.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18), Margin = new Thickness(0, 0, 16, 16), Child = card });
        }
        if (_cards.Children.Count == 0)
            _cards.Children.Add(new TextBlock { Text = "没有找到匹配组件。", Foreground = Palette.Muted, Margin = new Thickness(6, 24) });
    }

    private void WireDrag(Control source, ComponentItem item, string title, ComponentView? component = null)
    {
        PointerPressedEventArgs? pending = null;
        Point start = default;
        Vector grabOffset = default;
        var dragging = false;
        source.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (dragging || _capturedDrag is not null || !e.GetCurrentPoint(source).Properties.IsLeftButtonPressed) return;
            start = e.GetPosition(source);
            if (component != null && (!component.IsHitAt(start) || !component.CanStartDrag(start))) return;
            pending = e;
            // Normalize the grabbed content point before the desktop applies its own scale.
            // A card-title drag instead places its component center at the pointer.
            grabOffset = component == null ? default : new Vector(
                (component.Bounds.Width / 2 - start.X) * component.Footprint.Width / component.Bounds.Width,
                (component.Bounds.Height / 2 - start.Y) * component.Footprint.Height / component.Bounds.Height);
        }, RoutingStrategies.Tunnel, true);
        source.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (pending != null && e.Pointer.Captured == null) e.Pointer.Capture(source);
        }, RoutingStrategies.Bubble, true);
        source.AddHandler(PointerMovedEvent, (_, e) => UiOperation.Start(async () =>
        {
            if (_capturedDrag is { } active && active.Source == source && active.Pointer == e.Pointer)
            {
                e.Handled = true;
                UpdateCapturedDrag(active, source.PointToScreen(e.GetPosition(source)));
                return;
            }
            if (pending == null || ((Vector)(e.GetPosition(source) - start)).Length < 6) return;
            var trigger = pending;
            pending = null;
            e.Handled = true;
            if (OperatingSystem.IsLinux() && trigger.Pointer.Type == PointerType.Mouse &&
                TopLevel.GetTopLevel(this)?.TryGetPlatformHandle()?.HandleDescriptor == "XID")
            {
                var screenPoint = source.PointToScreen(e.GetPosition(source));
                Focus();
                trigger.Pointer.Capture(source);
                if (trigger.Pointer.Captured != source) return;
                component?.SetSelected(true);
                var preview = FloatingComponentPreview.TryShow(this, _workspace, item.Application, grabOffset);
                var drag = _capturedDrag = new CapturedDrag(source, trigger.Pointer, item.Application, grabOffset,
                    component, preview, HoverTipBehavior.SuppressForNativeDrag());
                _status.Text = $"正在拖动「{title}」；移到功能区后松开，Esc 取消。";
                UpdateCapturedDrag(drag, screenPoint);
                return;
            }
            dragging = true;
            try
            {
                component?.SetSelected(true);
                using var floating = trigger.Pointer.Type == PointerType.Mouse
                    ? FloatingComponentPreview.TryShow(this, _workspace, item.Application, grabOffset)
                    : null;
                var data = new DataTransfer();
                data.Add(DataTransferItem.Create(LibraryComponentTransfer.Format, new LibraryComponentTransfer(item.Application, grabOffset, floating != null)));
                _status.Text = $"正在拖动「{title}」；移到功能区后松开，Esc 取消。";
                using var hoverSuppression = HoverTipBehavior.SuppressForNativeDrag();
                if (floating == null) ShowPreview(item.Application, e.GetPosition(_dragLayer), grabOffset);
                var effect = await DragDrop.DoDragDropAsync(trigger, data, DragDropEffects.Copy);
                if (TopLevel.GetTopLevel(source) is not null)
                    _status.Text = effect == DragDropEffects.Copy ? $"已创建「{title}」的快捷方式，数据实时同步。" : "已取消放置，库中应用保留。";
            }
            finally
            {
                dragging = false;
                EndPreview();
                component?.SetSelected(false);
                _workspace.ClearLibraryDrop();
            }
        }, () => TopLevel.GetTopLevel(source) is null), RoutingStrategies.Tunnel, true);
        source.AddHandler(PointerReleasedEvent, (_, e) =>
        {
            pending = null;
            if (_capturedDrag is { } drag && drag.Source == source && drag.Pointer == e.Pointer && e.InitialPressMouseButton == MouseButton.Left)
            {
                e.Handled = true;
                var point = source.PointToScreen(e.GetPosition(source));
                var placed = false;
                try { placed = _workspace.DropCapturedLibraryDrag(drag.Application, drag.GrabOffset, point); }
                finally { FinishCapturedDrag(placed); }
                return;
            }
            if (e.Pointer.Captured == source) e.Pointer.Capture(null);
        }, RoutingStrategies.Tunnel, true);
        source.PointerCaptureLost += (_, _) =>
        {
            pending = null;
            if (_capturedDrag is { } drag && drag.Source == source && drag.Pointer.Captured != source) FinishCapturedDrag(false);
        };
        source.DetachedFromVisualTree += (_, _) =>
        {
            pending = null;
            if (_capturedDrag?.Source == source) FinishCapturedDrag(false);
        };
    }

    private void UpdateCapturedDrag(CapturedDrag drag, PixelPoint screenPoint)
    {
        var overLibrary = new Rect(Bounds.Size).Contains(this.PointToClient(screenPoint)) &&
            TopLevel.GetTopLevel(this) is Window window && (drag.Preview?.IsX11WindowAt(window, screenPoint) ?? true);
        if (overLibrary)
        {
            _workspace.ClearLibraryDrop();
            if (drag.Preview is null) ShowPreview(drag.Application, _dragLayer.PointToClient(screenPoint), drag.GrabOffset);
            return;
        }
        EndPreview();
        _workspace.UpdateCapturedLibraryDrag(drag.Application, drag.GrabOffset, screenPoint, drag.Preview is not null);
    }

    internal void ShowCapturedWorkspaceDrag(ComponentItem item, Vector grabOffset, PixelPoint screenPoint, bool floating)
    {
        if (floating) EndPreview();
        else ShowPreview(item, _dragLayer.PointToClient(screenPoint), grabOffset);
        _status.Text = "松开移除此快捷方式 · 库内应用和共享数据保留 · Esc 取消";
    }

    internal void ClearCapturedWorkspaceDrag()
    {
        EndPreview();
        _status.Text = UsageHint;
    }

    private void FinishCapturedDrag(bool placed)
    {
        if (_capturedDrag is not { } drag) return;
        _capturedDrag = null;
        if (drag.Pointer.Captured == drag.Source) drag.Pointer.Capture(null);
        drag.Preview?.Dispose();
        drag.Suppression.Dispose();
        EndPreview();
        drag.Component?.SetSelected(false);
        _workspace.ClearLibraryDrop();
        _status.Text = placed ? "已创建快捷方式，数据实时同步。" : "已取消放置，库中应用保留。";
    }

    private void ShowPreview(ComponentItem item, Point position, Vector grabOffset)
    {
        if (_dragPreview?.Item != item)
        {
            EndPreview();
            _dragPreview = _workspace.CreateLibraryPreview(item);
            _dragLayer.Children.Add(_dragPreview);
        }
        var preview = _dragPreview;
        var definition = _workspace.Components.GetDefinition(preview.Item);
        var center = position + grabOffset * (preview.Footprint.Width / definition.Width);
        Canvas.SetLeft(preview, center.X - preview.Footprint.Width / 2);
        Canvas.SetTop(preview, center.Y - preview.Footprint.Height / 2);
        preview.IsVisible = true;
    }

    private void EndPreview()
    {
        if (_dragPreview is not { } preview) return;
        _dragPreview = null;
        _dragLayer.Children.Remove(preview);
        preview.Dispose();
    }

}
