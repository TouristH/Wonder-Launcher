// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Wonder.Extensibility.Contracts;

namespace Wonder.Desktop.Components;

internal sealed class ComponentPresentationContext(ComponentDefinition definition, bool preview,
    Func<IReadOnlyDictionary<string, string>> values, Action<string, string?> invoke)
{
    public ComponentDefinition Definition { get; } = definition;
    public bool IsPreview { get; } = preview;
    public IReadOnlyDictionary<string, string> Values => values();
    public string Get(string key, string fallback = "") => Values.GetValueOrDefault(key, fallback);
    public void Invoke(string action, string? value = null) { if (!IsPreview) invoke(action, value); }
}

/// <summary>Desktop-only presentation adapter. It owns controls and optional view-local subscriptions.</summary>
internal sealed class ComponentContent(Control control, Action? refresh = null, Action<bool>? select = null,
    Func<Point, bool>? canStartDrag = null, Action? dispose = null) : IDisposable
{
    public Control Control { get; } = control;
    public void Refresh() => refresh?.Invoke();
    public void SetSelected(bool value) => select?.Invoke(value);
    public bool CanStartDrag(Point point) => canStartDrag?.Invoke(point) ?? true;
    public void Dispose() => dispose?.Invoke();

    public static ComponentContent Text(ComponentPresentationContext context, string text)
    {
        var body = new StackPanel { Spacing = 12, Margin = new Thickness(16) };
        body.Children.Add(new TextBlock { Text = context.Definition.Title, FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Palette.Text });
        body.Children.Add(new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Palette.Text });
        return Card(body);
    }

    public static ComponentContent ActionCard(ComponentPresentationContext context, Control body, string label, string action,
        Action refresh, Func<bool>? enabled = null, Func<string>? description = null)
    {
        body.IsHitTestVisible = false;
        var surface = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 12), ClipToBounds = true, Child = body, Transitions = PageStyles.HoverTransitions() };
        PageStyles.EnablePressFeedback(surface);
        // Keep pointer hits stable while the painted surface shrinks during a press.
        var hitTarget = new Border { Background = Brushes.Transparent, CornerRadius = surface.CornerRadius, Child = surface };
        var button = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
            // The inner surface owns press feedback; Fluent must not scale the hit target too.
            RenderTransform = new MatrixTransform(Matrix.Identity),
            Template = new FuncControlTemplate<Button>((_, _) => hitTarget)
        };
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) => context.Invoke(action);
        var selected = false;
        void Paint()
        {
            surface.Background = button.IsPressed ? Palette.Pressed : button.IsPointerOver ? Palette.Hover : Palette.Active;
            surface.BorderBrush = selected || button.IsFocused ? Palette.Emphasis : button.IsPointerOver ? Palette.LineStrong : Palette.Line;
            surface.BorderThickness = new Thickness(selected || button.IsFocused ? 2 : 1);
            body.Opacity = button.IsEnabled || context.IsPreview ? 1 : .55;
            PageStyles.PaintPressScale(surface, button.IsPressed);
        }
        button.PropertyChanged += (_, args) =>
        {
            if (args.Property == Button.IsPressedProperty || args.Property == Button.IsPointerOverProperty ||
                args.Property == Button.IsFocusedProperty || args.Property == Button.IsEnabledProperty) Paint();
        };
        void Update()
        {
            refresh();
            button.IsEnabled = !context.IsPreview && (enabled?.Invoke() ?? true);
            var help = description?.Invoke() ?? context.Definition.Description;
            AutomationProperties.SetHelpText(button, help);
            ToolTip.SetTip(button, help);
            Paint();
        }
        Update();
        return new ComponentContent(button, Update, value => { selected = value; Paint(); });
    }

    internal static ComponentContent Card(Control body, Action? refresh = null, Func<Point, bool>? canStartDrag = null, Action? dispose = null)
    {
        var border = new Border
        {
            Background = Palette.Raised, BorderBrush = Palette.Line, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), ClipToBounds = true, Child = body
        };
        return new ComponentContent(border, refresh, selected =>
        {
            border.BorderBrush = selected ? Palette.Emphasis : Palette.Line;
            border.BorderThickness = new Thickness(selected ? 2 : 1);
        }, canStartDrag, dispose);
    }
}
