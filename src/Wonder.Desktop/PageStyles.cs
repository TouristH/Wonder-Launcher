// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Animation;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;

namespace Wonder.Desktop;

/// <summary>Shared surfaces and navigation buttons for component-owned pages.</summary>
internal static class PageStyles
{
    /// <summary>The two font stacks the spec allows: a text face and a numeric/hex face.</summary>
    public static readonly FontFamily MonoFamily = new("Cascadia Mono,Consolas,monospace");

    /// <summary>The one card radius; both the page card and the navigation card read from it.</summary>
    public static readonly CornerRadius CardRadius = new(12);

    /// <summary>
    /// The hover/press easing shared by every hand-painted surface. Without this, state changes
    /// snap between palette steps and the whole desktop reads as stiff; with it the same colours
    /// arrive over one short beat. Property values still change instantly, so snapshots and
    /// assertions see the target colour, not an interpolated one.
    /// </summary>
    public static Transitions HoverTransitions() => new()
    {
        new BrushTransition { Property = Border.BackgroundProperty, Duration = TimeSpan.FromMilliseconds(130) },
        new BrushTransition { Property = Border.BorderBrushProperty, Duration = TimeSpan.FromMilliseconds(130) },
    };

    /// <summary>The press-in easing: a card leans toward the pointer and springs back.</summary>
    public static Transitions PressTransitions() => new()
    {
        new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(110) },
    };

    /// <summary>Applies the shared press scale to a surface, keeping the origin centred.</summary>
    public static void EnablePressFeedback(Visual surface)
    {
        surface.RenderTransformOrigin = RelativePoint.Center;
        surface.RenderTransform = TransformOperations.Parse("scale(1)");
        surface.Transitions = PressTransitions();
    }

    /// <summary>Leans the surface in while pressed, back out when released.</summary>
    public static void PaintPressScale(Visual surface, bool pressed) =>
        surface.RenderTransform = TransformOperations.Parse(pressed ? "scale(0.982)" : "scale(1)");

    public static TextBlock Hint(string text) => new() { Text = text, FontSize = 12, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, LineHeight = 19 };

    public static Border Card(Control child, Thickness? padding = null) => new()
    {
        Background = Palette.Surface, BorderBrush = Palette.Line, BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12), Padding = padding ?? new Thickness(20, 16), Child = child
    };

    /// <summary>A page title inside a sub-window: 23 SemiBold, the one heading size above cards.</summary>
    public static TextBlock PageTitle(string text) => new()
    {
        Text = text, FontSize = 23, FontWeight = FontWeight.SemiBold, Foreground = Palette.Text, TextWrapping = TextWrapping.Wrap
    };

    /// <summary>A heading inside a card: 13 SemiBold. Never invent another size.</summary>
    public static TextBlock SectionTitle(string text) => new()
    {
        Text = text, FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Palette.Text, TextWrapping = TextWrapping.Wrap
    };

    /// <summary>A title inside a dialog: 21 SemiBold, one step below a page title.</summary>
    public static TextBlock DialogTitle(string text) => new()
    {
        Text = text, FontSize = 21, FontWeight = FontWeight.SemiBold, Foreground = Palette.Text, TextWrapping = TextWrapping.Wrap
    };

    /// <summary>The trailing chevron on a card that opens something.</summary>
    public static TextBlock Chevron() => new()
    {
        Text = "›", FontSize = 22, Foreground = Palette.Accent,
        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center
    };

    /// <summary>The single empty-state line a container shows when it holds nothing.</summary>
    public static TextBlock EmptyState(string text) => new()
    {
        Text = text, FontSize = 12, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Center, Margin = new Thickness(12)
    };

    /// <summary>A field label above an input: 13 regular.</summary>
    public static TextBlock Label(string text) => new()
    {
        Text = text, FontSize = 13, Foreground = Palette.Text, TextWrapping = TextWrapping.Wrap
    };

    /// <summary>A textual value the user may copy: IDs, paths, codes. Always the mono face.</summary>
    public static TextBlock Code(string text, bool selectable = false)
    {
        if (!selectable) return new TextBlock { Text = text, FontSize = 12, FontFamily = MonoFamily, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap };
        return new SelectableTextBlock { Text = text, FontSize = 12, FontFamily = MonoFamily, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap };
    }

    /// <summary>A vertical group of fields inside a card.</summary>
    public static StackPanel Field(Control child, double spacing = 6)
    {
        var panel = new StackPanel { Spacing = spacing };
        panel.Children.Add(child);
        return panel;
    }

    /// <summary>A labelled field: a 13 px label above its control.</summary>
    public static StackPanel Field(string label, Control control, double spacing = 6)
    {
        var panel = new StackPanel { Spacing = spacing };
        panel.Children.Add(Label(label));
        panel.Children.Add(control);
        return panel;
    }

    /// <summary>The one progress bar shape in the app.</summary>
    public static ProgressBar ProgressBar(string automationName, bool indeterminate = false)
    {
        var bar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 6, IsIndeterminate = indeterminate };
        AutomationProperties.SetName(bar, automationName);
        return bar;
    }

    /// <summary>The standard footer/secondary button used across every sub-window.</summary>
    public static Button Button(string text, string automationName, Action onClick)
    {
        var button = new Button { Content = text, MinHeight = 34, Margin = new Thickness(0, 0, 8, 4) };
        AutomationProperties.SetName(button, automationName);
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>A horizontal row of actions aligned to the trailing edge.</summary>
    public static StackPanel Actions(params Control[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var child in children) row.Children.Add(child);
        return row;
    }

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
            _surface = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(18, 15), Child = body, Transitions = HoverTransitions() };
            EnablePressFeedback(_surface);
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
            _surface.BorderBrush = IsFocused || _selected ? Palette.Emphasis : IsPointerOver ? Palette.LineStrong : Palette.Line;
            _surface.BorderThickness = new Thickness(IsFocused || _selected ? 2 : 1);
            _surface.Opacity = IsEnabled ? 1 : .5;
        }
    }
}
