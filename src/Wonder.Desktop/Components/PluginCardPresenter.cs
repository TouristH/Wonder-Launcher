// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Wonder.Extensibility.Contracts;

namespace Wonder.Desktop.Components;

internal static class PluginCardPresenter
{
    internal static ComponentContent Present(ComponentCard card, ComponentPresentationContext context)
    {
        if (card.Narration is not null) return PluginNarrationPresenter.Present(card, context);
        var style = card.Style;
        var title = new TextBlock
        {
            Text = string.IsNullOrEmpty(card.Title) ? context.Definition.Title : card.Title,
            FontSize = style.TitleFontSize, LetterSpacing = style.TitleLetterSpacing,
            Foreground = Brush.Parse(style.TitleColor), LineHeight = 15,
            MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new(0, 0, 0, 3)
        };
        var text = new TextBlock
        {
            FontSize = style.TextFontSize, Foreground = Brush.Parse(style.TextColor),
            LineHeight = 17, TextWrapping = TextWrapping.Wrap, MaxLines = card.Scrollable ? 0 : 2,
            TextTrimming = card.Scrollable ? TextTrimming.None : TextTrimming.CharacterEllipsis
        };
        var footer = new TextBlock
        {
            FontSize = style.FooterFontSize, Foreground = Brush.Parse(style.FooterColor),
            LineHeight = 15, TextAlignment = TextAlignment.Right, MaxLines = card.Scrollable ? 0 : 1,
            TextWrapping = card.Scrollable ? TextWrapping.Wrap : TextWrapping.NoWrap,
            TextTrimming = card.Scrollable ? TextTrimming.None : TextTrimming.CharacterEllipsis
        };
        var rows = new StackPanel { Spacing = 0 };
        var refreshers = new List<Action>();
        foreach (var row in card.Rows)
        {
            var line = new Grid
            {
                ColumnDefinitions = new(row.SecondaryValueKey is null ? "Auto,*" : "Auto,*,Auto"),
                ColumnSpacing = 6, MinHeight = card.RowLineHeight
            };
            line.Children.Add(new TextBlock
            {
                Text = row.Label, FontSize = style.TextFontSize, LineHeight = card.RowLineHeight,
                Foreground = Brush.Parse(style.TitleColor)
            });
            var value = new TextBlock
            {
                FontSize = style.TextFontSize, LineHeight = card.RowLineHeight, MaxLines = card.Scrollable ? 0 : 1,
                TextWrapping = card.Scrollable ? TextWrapping.Wrap : TextWrapping.NoWrap,
                TextTrimming = card.Scrollable ? TextTrimming.None : TextTrimming.CharacterEllipsis
            };
            if (row.Monospace) value.FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace");
            Grid.SetColumn(value, 1);
            line.Children.Add(value);
            TextBlock? secondary = null;
            if (row.SecondaryValueKey is not null)
            {
                secondary = new TextBlock { FontSize = 11, LineHeight = card.RowLineHeight, Foreground = Brush.Parse(style.TitleColor) };
                Grid.SetColumn(secondary, 2);
                line.Children.Add(secondary);
            }
            Control rowControl = line;
            if (row.AccentColor is { } accent)
                rowControl = new Border { BorderBrush = Brush.Parse(accent), BorderThickness = new(2, 0, 0, 0), Padding = new(8, 0, 0, 0), Child = line };
            if (row.ActionId is { } actionId)
            {
                var surface = new Border { Child = rowControl, Padding = new(0, 5), Background = Brushes.Transparent };
                var button = new Button
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch, Cursor = new Cursor(StandardCursorType.Hand),
                    Template = new FuncControlTemplate<Button>((_, _) => surface), IsEnabled = !context.IsPreview
                };
                AutomationProperties.SetName(button, row.Label.Length > 0 ? row.Label : context.Definition.Title);
                button.Click += (_, _) => context.Invoke(actionId);
                button.PropertyChanged += (_, args) =>
                {
                    if (args.Property == Button.IsPointerOverProperty)
                        surface.Background = button.IsPointerOver ? Brush.Parse("#22262E") : Brushes.Transparent;
                };
                rowControl = button;
            }
            rows.Children.Add(rowControl);
            var target = rowControl;
            var normal = Brush.Parse(style.TextColor);
            var muted = Brush.Parse(style.TitleColor);
            refreshers.Add(() =>
            {
                var current = context.Get(row.ValueKey);
                if (value.Text != current) value.Text = current;
                value.Foreground = row.HighlightKey is null || context.Get(row.HighlightKey) == "true" ? normal : muted;
                if (secondary is not null) secondary.Text = context.Get(row.SecondaryValueKey!);
                target.IsVisible = current.Length > 0 || secondary?.Text?.Length > 0;
            });
        }

        var body = new Grid { RowDefinitions = new("Auto,*,Auto") };
        body.Children.Add(title);
        var contents = new StackPanel { Spacing = 3 };
        contents.Children.Add(text);
        contents.Children.Add(rows);
        if (card.Scrollable)
        {
            contents.Children.Add(footer);
            var scroll = new ScrollViewer
            {
                Content = contents, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            AutomationProperties.SetName(scroll, context.Definition.Title + "内容");
            Grid.SetRow(scroll, 1);
            body.Children.Add(scroll);
        }
        else
        {
            Grid.SetRow(contents, 1);
            Grid.SetRow(footer, 2);
            body.Children.Add(contents);
            body.Children.Add(footer);
        }
        var border = new Border
        {
            Background = new SolidColorBrush(Color.Parse(style.Background), style.BackgroundOpacity),
            BorderBrush = Brush.Parse(style.Border), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(style.CornerRadius), Padding = new Thickness(style.Padding),
            ClipToBounds = true, Child = body
        };
        AutomationProperties.SetName(border, context.Definition.Title);
        void Refresh()
        {
            var currentText = context.Get(card.TextKey);
            if (text.Text != currentText) text.Text = currentText;
            text.IsVisible = currentText.Length > 0;
            var currentFooter = context.Get(card.FooterKey);
            if (footer.Text != currentFooter) footer.Text = currentFooter;
            footer.IsVisible = currentFooter.Length > 0;
            foreach (var refresh in refreshers) refresh();
        }
        Refresh();
        var headerOnlyDrag = card.Scrollable || card.Rows.Any(row => row.ActionId is not null);
        return new ComponentContent(border, Refresh,
            canStartDrag: point => !headerOnlyDrag || point.Y < style.Padding + 19);
    }
}
