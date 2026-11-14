// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Wonder.Extensibility.Contracts;

namespace Wonder.Desktop.Components;

internal static class PluginNarrationPresenter
{
    internal static ComponentContent Present(ComponentCard card, ComponentPresentationContext context)
    {
        var options = card.Narration!;
        var style = card.Style;
        var dot = new Border { Width = 6, Height = 6, CornerRadius = new(3), VerticalAlignment = VerticalAlignment.Center };
        var text = new TextBlock
        {
            FontSize = style.TextFontSize, Foreground = Brush.Parse(style.TextColor), LineHeight = 22,
            MaxLines = 1, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        var content = new Grid { ColumnDefinitions = new("6,*"), ColumnSpacing = 16 };
        content.Children.Add(dot);
        Grid.SetColumn(text, 1);
        content.Children.Add(text);
        var border = new Border
        {
            Background = new SolidColorBrush(Color.Parse(style.Background), style.BackgroundOpacity),
            BorderBrush = Brush.Parse(style.Border), BorderThickness = new(1), CornerRadius = new(style.CornerRadius),
            Padding = new(12, 0), ClipToBounds = true, Child = content
        };
        AutomationProperties.SetName(border, context.Definition.Title);
        var active = Brush.Parse(options.ActiveColor);
        var idle = Brush.Parse(options.IdleColor);
        void Refresh()
        {
            var value = context.Get(card.TextKey);
            if (text.Text != value) text.Text = value;
            text.Opacity = Opacity(context.Get(options.TextOpacityKey));
            dot.Opacity = Opacity(context.Get(options.IndicatorOpacityKey, "1"));
            dot.Background = context.Get(options.IndicatorActiveKey) == "true" ? active : idle;
        }
        Refresh();
        return new ComponentContent(border, Refresh);
    }

    private static double Opacity(string value) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var opacity)
        && double.IsFinite(opacity) ? Math.Clamp(opacity, 0, 1) : 0;
}
