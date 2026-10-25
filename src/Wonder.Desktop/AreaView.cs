// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Wonder.Workspace;
using Wonder.Desktop.Components;

namespace Wonder.Desktop;

internal sealed class AreaView : Border, IDisposable
{
    public const double HeaderHeight = 46;
    public const double Inset = 14;
    public Area Area { get; }
    public Dictionary<string, ComponentView> Components { get; } = new();
    private readonly Canvas _canvas;
    private readonly TextBlock _hint;
    private readonly TextBlock _number;

    public AreaView(Area area, int index, ComponentHost host, bool showNumber = false)
    {
        Area = area;
        Background = Palette.Surface;
        BorderBrush = Palette.Line;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(16);
        ClipToBounds = true;
        var grid = new Grid { RowDefinitions = new RowDefinitions("46,*") };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(17, 0, 17, 0) };
        _number = new TextBlock { Text = (index + 1).ToString("00"), Foreground = Palette.Muted, FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), IsVisible = showNumber };
        var title = new TextBlock { Text = area.Title, FontSize = 12, FontWeight = FontWeight.Medium, Foreground = Palette.Text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 10, 0) };
        ToolTip.SetTip(title, area.Title + "\n右键可重命名功能区");
        _hint = new TextBlock { Text = "⠿", FontSize = 18, Foreground = Palette.Muted, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(title, 1);
        Grid.SetColumn(_hint, 2);
        header.Children.Add(_number);
        header.Children.Add(title);
        header.Children.Add(_hint);
        ToolTip.SetTip(header, "长按顶边栏拖动，放到另一个功能区以交换位置");
        _canvas = new Canvas { ClipToBounds = true, Margin = new Thickness(Inset, 0, Inset, Inset) };
        Grid.SetRow(_canvas, 1);
        grid.Children.Add(header);
        grid.Children.Add(_canvas);
        Child = grid;
        foreach (var component in area.Components)
        {
            var view = new ComponentView(component, host);
            Components.Add(component.Id, view);
            _canvas.Children.Add(view);
        }
        if (area.Components.Count == 0)
        {
            var placeholder = new TextBlock { Text = "右键打开组件库，或拖入组件", FontSize = 12, Foreground = Palette.Muted, IsHitTestVisible = false };
            Canvas.SetLeft(placeholder, 10);
            Canvas.SetTop(placeholder, 12);
            _canvas.Children.Add(placeholder);
        }
    }

    public void ShowNumber(bool visible) => _number.IsVisible = visible;

    public void SetRuntimeVisible(bool visible)
    {
        foreach (var view in Components.Values) view.SetRuntimeVisible(visible);
    }

    public void Dispose()
    {
        foreach (var view in Components.Values) view.Dispose();
    }

    public void Highlight(bool source, bool target, bool pending = false)
    {
        BorderBrush = source || target || pending ? Palette.Accent : Palette.Line;
        Background = target ? Palette.Active : Palette.Surface;
        Opacity = source ? .65 : 1;
        _hint.Text = target ? "松开交换" : source ? "拖动中" : pending ? "按住…" : "⠿";
        _hint.FontSize = source || target || pending ? 11 : 18;
    }

    public Rect ComponentRect(ComponentView view, Size areaSize)
    {
        var position = WorkspaceLayout.FromCenterOffset(areaSize.Width, areaSize.Height,
            view.Footprint.Width, view.Footprint.Height, view.Item.X, view.Item.Y);
        return new Rect(position.X, position.Y, view.Footprint.Width, view.Footprint.Height);
    }

    public void PositionComponents(Size size)
    {
        foreach (var view in Components.Values)
        {
            var rect = ComponentRect(view, size);
            view.Width = rect.Width;
            view.Height = rect.Height;
            Canvas.SetLeft(view, rect.X - Inset - 1);
            Canvas.SetTop(view, rect.Y - HeaderHeight - 1);
        }
    }
}
