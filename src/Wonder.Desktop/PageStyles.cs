// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;

namespace Wonder.Desktop;

/// <summary>Shared surfaces and navigation buttons for component-owned pages.</summary>
internal static class PageStyles
{
    public static TextBlock Hint(string text) => new() { Text = text, FontSize = 12, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, LineHeight = 19 };

    public static Border Card(Control child, Thickness? padding = null) => new()
    {
        Background = Palette.Surface, BorderBrush = Palette.Line, BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(14), Padding = padding ?? new Thickness(20, 16), Child = child
    };

    public static Button CardButton(string automationName, string title, string subtitle, string? detail, Action onClick, bool selected = false)
    {
        var button = new NavigationCardButton(title, subtitle, detail) { Selected = selected };
        AutomationProperties.SetName(button, automationName);
        button.Click += (_, _) => onClick();
        return button;
    }

    public static void SetSelected(Button button, bool selected)
    {
        if (button is NavigationCardButton card) card.Selected = selected;
    }

    public static void SetCompact(Button button, bool compact)
    {
        if (button is NavigationCardButton card) card.SetCompact(compact);
    }

    private sealed class NavigationCardButton : Button
    {
        private readonly Border _surface;
        private readonly StackPanel _body;
        private readonly TextBlock _title, _caption;
        private readonly TextBlock? _detail;
        private bool _selected;
        public bool Selected { get => _selected; set { _selected = value; Classes.Set("selected", value); Paint(); } }

        public NavigationCardButton(string title, string subtitle, string? detail)
        {
            HorizontalAlignment = HorizontalAlignment.Stretch;
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
            MinHeight = 88;
            var body = _body = new StackPanel { Spacing = 9 };
            var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,20") };
            _title = new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = Palette.Text, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis };
            heading.Children.Add(_title);
            var arrow = new TextBlock { Text = "›", FontSize = 22, Foreground = Palette.Accent, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(arrow, 1); heading.Children.Add(arrow); body.Children.Add(heading);
            _caption = Hint(subtitle); _caption.Foreground = Palette.Accent; body.Children.Add(_caption);
            if (!string.IsNullOrWhiteSpace(detail))
            {
                _detail = Hint(detail); _detail.MaxLines = 2; _detail.TextTrimming = TextTrimming.CharacterEllipsis; body.Children.Add(_detail);
            }
            _surface = new Border { CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Padding = new Thickness(18, 15), Child = body };
            Template = new FuncControlTemplate<Button>((_, _) => _surface);
            ToolTip.SetTip(this, string.Join("\n", new[] { title, subtitle, detail }.Where(x => !string.IsNullOrWhiteSpace(x))));
            PropertyChanged += (_, e) =>
            {
                if (e.Property == IsPointerOverProperty || e.Property == IsPressedProperty || e.Property == IsFocusedProperty || e.Property == IsEnabledProperty) Paint();
            };
            Paint();
        }

        public void SetCompact(bool compact)
        {
            _body.VerticalAlignment = VerticalAlignment.Center;
            _body.Spacing = compact ? 6 : 9;
            _title.MaxLines = compact ? 1 : 2;
            _title.TextWrapping = compact ? TextWrapping.NoWrap : TextWrapping.Wrap;
            _caption.MaxLines = compact ? 2 : 3;
            _caption.TextTrimming = TextTrimming.CharacterEllipsis;
            if (_detail is not null)
            {
                _detail.MaxLines = compact ? 1 : 2;
                _detail.TextWrapping = compact ? TextWrapping.NoWrap : TextWrapping.Wrap;
            }
        }

        private void Paint()
        {
            _surface.Background = IsPressed ? Palette.Pressed : IsPointerOver ? Palette.Hover : _selected ? Palette.Active : Palette.Surface;
            _surface.BorderBrush = IsFocused || _selected ? Palette.Emphasis : Palette.Accent;
            _surface.BorderThickness = new Thickness(IsFocused || _selected ? 2 : 1);
            _surface.Opacity = IsEnabled ? 1 : .5;
        }
    }
}
