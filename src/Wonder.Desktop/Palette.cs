// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia.Media;
using Wonder.Workspace;

namespace Wonder.Desktop;

internal static class Palette
{
    // Keep brush identities stable so open windows and drag previews update in place.
    public static readonly SolidColorBrush Background = new(Color.Parse("#101317"));
    public static readonly SolidColorBrush Surface = new(Color.Parse("#181D23"));
    public static readonly SolidColorBrush Header = new(Color.Parse("#15191E"));
    public static readonly SolidColorBrush Line = new(Color.Parse("#303943"));
    public static readonly SolidColorBrush Text = new(Color.Parse("#E8EDEF"));
    public static readonly SolidColorBrush Muted = new(Color.Parse("#9BA8B4"));
    public static readonly SolidColorBrush Accent = new(Color.Parse("#ADF2CF"));
    public static readonly SolidColorBrush Active = new(Color.Parse("#203B34"));
    public static readonly SolidColorBrush Hover = new(Color.Parse("#2B4C3F"));
    public static readonly SolidColorBrush Pressed = new(Color.Parse("#355D4C"));
    public static readonly SolidColorBrush Emphasis = new(Color.Parse("#DBFFE9"));
    public static readonly SolidColorBrush Error = new(Color.Parse("#FFBE91"));

    public static Color AccentFor(AccentColor accent, bool light) => Color.Parse((accent, light) switch
    {
        (AccentColor.Blue, false) => "#A5CEFF", (AccentColor.Blue, true) => "#245AA6",
        (AccentColor.Amber, false) => "#F5CF8C", (AccentColor.Amber, true) => "#8C590A",
        (AccentColor.Rose, false) => "#F3B8CB", (AccentColor.Rose, true) => "#A13662",
        (_, true) => "#206B50", _ => "#ADF2CF"
    });

    public static void Apply(bool light, AccentColor accent)
    {
        Background.Color = Color.Parse(light ? "#F2F4F3" : "#101317");
        Surface.Color = Color.Parse(light ? "#FFFFFF" : "#181D23");
        Header.Color = Color.Parse(light ? "#F9FAF9" : "#15191E");
        Line.Color = Color.Parse(light ? "#D1D9D5" : "#303943");
        Text.Color = Color.Parse(light ? "#202C29" : "#E8EDEF");
        Muted.Color = Color.Parse(light ? "#596A63" : "#9BA8B4");
        Error.Color = Color.Parse(light ? "#A84210" : "#FFBE91");
        Accent.Color = AccentFor(accent, light);
        Active.Color = Blend(Surface.Color, Accent.Color, light ? .08 : .14);
        Hover.Color = Blend(Surface.Color, Accent.Color, light ? .15 : .23);
        Pressed.Color = Blend(Surface.Color, Accent.Color, light ? .23 : .32);
        Emphasis.Color = Accent.Color;
    }

    private static Color Blend(Color background, Color foreground, double amount) => Color.FromRgb(
        (byte)(background.R * (1 - amount) + foreground.R * amount),
        (byte)(background.G * (1 - amount) + foreground.G * amount),
        (byte)(background.B * (1 - amount) + foreground.B * amount));
}
