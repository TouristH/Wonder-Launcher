// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;

namespace Wonder.Desktop;

/// <summary>Selection surface and peer actions, so settings/deletion never select a launch target.</summary>
internal sealed class InstanceBrowserCard : Border
{
    public Button SelectButton { get; }
    public Button SettingsButton { get; }
    public Button DeleteButton { get; }
    private bool _selected;
    public bool Selected
    {
        get => _selected;
        set { _selected = value; Classes.Set("selected", value); SelectButton.Classes.Set("selected", value); Paint(); }
    }

    public InstanceBrowserCard(string name, string title, string version, string path, Action select, Action settings, Action delete)
    {
        CornerRadius = new CornerRadius(12); BorderThickness = new Thickness(1);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var content = new StackPanel { Spacing = 5 };
        content.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Palette.Text, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new TextBlock { Text = version, FontSize = 11, LineHeight = 16, Foreground = Palette.Accent, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new TextBlock { Text = path, FontSize = 10, LineHeight = 15, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap });
        SelectButton = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
            Template = new FuncControlTemplate<Button>((_, _) => new Border { Background = Brushes.Transparent, Padding = new Thickness(12, 10, 52, 10), Child = content })
        };
        AutomationProperties.SetName(SelectButton, name);
        SelectButton.Click += (_, e) => { e.Handled = true; select(); };
        Grid.SetColumnSpan(SelectButton, 2); grid.Children.Add(SelectButton);
        var actions = new StackPanel { Spacing = 5, Margin = new Thickness(0, 8, 8, 8), VerticalAlignment = VerticalAlignment.Top };
        SettingsButton = ActionButton("设置", "设置" + name, settings);
        DeleteButton = ActionButton("删除", "删除" + name, delete);
        actions.Children.Add(SettingsButton); actions.Children.Add(DeleteButton);
        Grid.SetColumn(actions, 1); grid.Children.Add(actions); Child = grid;
        ToolTip.SetTip(SelectButton, title + "\n" + version + "\n" + path);
        PropertyChanged += (_, e) => { if (e.Property == IsPointerOverProperty || e.Property == IsKeyboardFocusWithinProperty || e.Property == IsEnabledProperty) Paint(); };
        SelectButton.PropertyChanged += (_, e) => { if (e.Property == Button.IsPressedProperty) Paint(); };
        Paint();
    }

    private static Button ActionButton(string label, string name, Action action)
    {
        var button = new Button { Content = label, FontSize = 11, Padding = new Thickness(6, 4), MinWidth = 36, MinHeight = 28 };
        AutomationProperties.SetName(button, name); ToolTip.SetTip(button, name);
        button.Click += (_, e) => { e.Handled = true; action(); };
        return button;
    }

    private void Paint()
    {
        Background = SelectButton.IsPressed ? Palette.Pressed : IsPointerOver ? Palette.Hover : _selected ? Palette.Active : Palette.Surface;
        BorderBrush = _selected || IsKeyboardFocusWithin ? Palette.Emphasis : Palette.Line;
        BorderThickness = new Thickness(_selected || IsKeyboardFocusWithin ? 2 : 1);
        Opacity = IsEnabled ? 1 : .5;
    }
}
