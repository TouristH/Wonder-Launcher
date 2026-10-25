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
    private readonly ComponentItem[] _items;
    private readonly TextBox _search;
    private readonly WrapPanel _cards;
    private readonly TextBlock _status;
    private readonly List<ComponentView> _views = [];
    private bool _runtimeVisible = true;

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
        Child = layout;
        _search.TextChanged += (_, _) => RefreshCards();
        RefreshCards();
        AttachedToVisualTree += (_, _) => _workspace.ComponentLibraryDropTarget = this;
        DetachedFromVisualTree += (_, _) => Detach();
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragEnterEvent, ShortcutDragOver, RoutingStrategies.Bubble, true);
        AddHandler(DragDrop.DragOverEvent, ShortcutDragOver, RoutingStrategies.Bubble, true);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => { _status.Text = UsageHint; }, RoutingStrategies.Bubble, true);
        AddHandler(DragDrop.DropEvent, ShortcutDrop, RoutingStrategies.Bubble, true);
    }

    private const string UsageHint = "拖出创建快捷方式；把功能区组件拖回此页可移除该快捷方式，应用与数据保留。";

    public void Detach()
    {
        if (_workspace.ComponentLibraryDropTarget == this) _workspace.ComponentLibraryDropTarget = null;
        foreach (var view in _views) view.Dispose();
        _views.Clear();
    }

    internal void SetRuntimeVisible(bool visible)
    {
        _runtimeVisible = visible;
        foreach (var view in _views) view.SetRuntimeVisible(visible);
    }

    private bool Accepts(WorkspaceComponentTransfer? data) => data != null &&
        data.Source == _workspace && data.Document == _workspace.Document && _workspace.ContainsShortcut(data.Shortcut);

    private void ShortcutDragOver(object? sender, DragEventArgs e)
    {
        var accepted = e.DragEffects.HasFlag(DragDropEffects.Move) && Accepts(e.DataTransfer.TryGetValue(WorkspaceComponentTransfer.Format));
        e.DragEffects = accepted ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
        _status.Text = accepted ? "松开移除此快捷方式 · 库内应用和共享数据保留 · Esc 取消" : UsageHint;
    }

    private void ShortcutDrop(object? sender, DragEventArgs e)
    {
        var data = e.DataTransfer.TryGetValue(WorkspaceComponentTransfer.Format);
        var removed = e.DragEffects.HasFlag(DragDropEffects.Move) && Accepts(data) && _workspace.RemoveShortcut(data!.Shortcut);
        e.DragEffects = removed ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
        _status.Text = removed ? "已移除这处快捷方式，库内应用和其他快捷方式仍可继续使用。" : UsageHint;
    }

    private void RefreshCards()
    {
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
            card.Children.Add(new Border { Height = Math.Max(184, preview.Height), Child = preview });
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
            if (dragging || !e.GetCurrentPoint(source).Properties.IsLeftButtonPressed) return;
            start = e.GetPosition(source);
            if (component != null && (!component.IsHitAt(start) || !component.CanStartDrag(start))) return;
            pending = e;
            // Keep the point grabbed within the component under the pointer.
            // A card-title drag instead places its component center at the pointer.
            grabOffset = component == null ? default : new Point(component.Bounds.Width / 2, component.Bounds.Height / 2) - start;
        }, RoutingStrategies.Tunnel, true);
        source.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (pending != null && e.Pointer.Captured == null) e.Pointer.Capture(source);
        }, RoutingStrategies.Bubble, true);
        source.AddHandler(PointerMovedEvent, async (_, e) =>
        {
            if (pending == null || ((Vector)(e.GetPosition(source) - start)).Length < 6) return;
            var trigger = pending;
            pending = null;
            dragging = true;
            e.Handled = true;
            component?.SetSelected(true);
            var data = new DataTransfer();
            data.Add(DataTransferItem.Create(LibraryComponentTransfer.Format, new LibraryComponentTransfer(item.Application, grabOffset)));
            _status.Text = $"正在拖动「{title}」；移到功能区后松开，Esc 取消。";
            try
            {
                using var hoverSuppression = HoverTipBehavior.SuppressForNativeDrag();
                var effect = await DragDrop.DoDragDropAsync(trigger, data, DragDropEffects.Copy);
                _status.Text = effect == DragDropEffects.Copy ? $"已创建「{title}」的快捷方式，数据实时同步。" : "已取消放置，库中应用保留。";
            }
            finally
            {
                dragging = false;
                component?.SetSelected(false);
                _workspace.ClearLibraryDrop();
            }
        }, RoutingStrategies.Tunnel, true);
        source.AddHandler(PointerReleasedEvent, (_, e) =>
        {
            pending = null;
            if (e.Pointer.Captured == source) e.Pointer.Capture(null);
        }, RoutingStrategies.Tunnel, true);
        source.PointerCaptureLost += (_, _) => pending = null;
        source.DetachedFromVisualTree += (_, _) => pending = null;
    }

}
