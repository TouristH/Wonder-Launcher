// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia.Media;
using Wonder.Workspace;

namespace Wonder.Desktop;

internal static class Palette
{
    // Keep brush identities stable so open windows and drag previews update in place.
    // The first group predates the 0.8 refresh and is still referenced across the desktop;
    // the second group adds the tonal steps the refresh needs for real hierarchy.
    public static readonly SolidColorBrush Background = new(Color.Parse("#101416"));
    public static readonly SolidColorBrush Surface = new(Color.Parse("#1A2128"));
    public static readonly SolidColorBrush Header = new(Color.Parse("#151B20"));
    public static readonly SolidColorBrush Line = new(Color.Parse("#2A333C"));
    public static readonly SolidColorBrush Text = new(Color.Parse("#E9EEF4"));
    public static readonly SolidColorBrush Muted = new(Color.Parse("#93A1B1"));
    public static readonly SolidColorBrush Accent = new(Color.Parse("#8CE0BC"));
    public static readonly SolidColorBrush Active = new(Color.Parse("#1A2B27"));
    public static readonly SolidColorBrush Hover = new(Color.Parse("#223731"));
    public static readonly SolidColorBrush Pressed = new(Color.Parse("#2A453C"));
    public static readonly SolidColorBrush Emphasis = new(Color.Parse("#8CE0BC"));
    public static readonly SolidColorBrush Error = new(Color.Parse("#FFB48A"));
    public static readonly SolidColorBrush PluginVerified = new(Color.Parse("#7ED9A4"));
    public static readonly SolidColorBrush PluginUnverified = new(Color.Parse("#FF9B9B"));

    /// <summary>A card sitting above an area surface.</summary>
    public static readonly SolidColorBrush Raised = new(Color.Parse("#242C36"));
    /// <summary>An input well, chip or toolbar recessed into a surface.</summary>
    public static readonly SolidColorBrush Sunken = new(Color.Parse("#0E1316"));
    /// <summary>A hairline that separates structure from structure.</summary>
    public static readonly SolidColorBrush LineStrong = new(Color.Parse("#7C8CA0"));
    /// <summary>The quietest tint of the accent, for fills behind accent text.</summary>
    public static readonly SolidColorBrush AccentSoft = new(Color.Parse("#18231F"));
    /// <summary>A tinted border that hints at the accent without claiming to be interactive.</summary>
    public static readonly SolidColorBrush AccentLine = new(Color.Parse("#33604F"));
    /// <summary>Text and glyphs drawn on top of a solid accent fill.</summary>
    public static readonly SolidColorBrush OnAccent = new(Color.Parse("#08120F"));
    /// <summary>The three tones of the Wonder mark, derived from the active accent.</summary>
    public static readonly SolidColorBrush MarkTop = new(Color.Parse("#8CE0BC"));
    public static readonly SolidColorBrush MarkLeft = new(Color.Parse("#5FA98C"));
    public static readonly SolidColorBrush MarkRight = new(Color.Parse("#3E7763"));
    /// <summary>The bright core inside the mark's head.</summary>
    public static readonly SolidColorBrush MarkCore = new(Color.Parse("#DFFBEF"));
    /// <summary>The comet trail: a paler wash than the head, so the two read as separate parts.</summary>
    public static readonly SolidColorBrush MarkTrail = new(Color.Parse("#BFEEDC"));

    /// <summary>Shadow colour for BoxShadow declarations; brushes cannot travel through BoxShadow.</summary>
    public static Color ShadowColor { get; private set; } = Color.Parse("#66050A0E");

    /// <summary>
    /// The single elevation layer, rebuilt on every read so a theme change cannot leave a stale shadow behind.
    /// </summary>
    public static BoxShadows Elevation => new(new BoxShadow { OffsetX = 0, OffsetY = 1, Blur = 6, Spread = 0, Color = ShadowColor });

    public static Color AccentFor(AccentColor accent, bool light) => Color.Parse((accent, light) switch
    {
        (AccentColor.Blue, false) => "#A5CEFF", (AccentColor.Blue, true) => "#2667BC",
        (AccentColor.Amber, false) => "#F5CF8C", (AccentColor.Amber, true) => "#8C590A",
        (AccentColor.Rose, false) => "#F3B8CB", (AccentColor.Rose, true) => "#B03D6C",
        (_, true) => "#1A7758", _ => "#8CE0BC"
    });

    public static void Apply(bool light, AccentColor accent)
    {
        Background.Color = Color.Parse(light ? "#E4EAEE" : "#101416");
        Surface.Color = Color.Parse(light ? "#FFFFFF" : "#1A2128");
        Header.Color = Color.Parse(light ? "#F7FAFB" : "#151B20");
        Line.Color = Color.Parse(light ? "#C9D5DE" : "#2A333C");
        LineStrong.Color = Color.Parse(light ? "#4A5A69" : "#7C8CA0");
        Raised.Color = Color.Parse(light ? "#FFFFFF" : "#242C36");
        Sunken.Color = Color.Parse(light ? "#DCE4EA" : "#0E1316");
        Text.Color = Color.Parse(light ? "#131C24" : "#E9EEF4");
        Muted.Color = Color.Parse(light ? "#4E606F" : "#93A1B1");
        Error.Color = Color.Parse(light ? "#A84210" : "#FFB48A");
        PluginVerified.Color = Color.Parse(light ? "#176B3A" : "#7ED9A4");
        PluginUnverified.Color = Color.Parse(light ? "#B4232C" : "#FF9B9B");
        Accent.Color = AccentFor(accent, light);
        Active.Color = Blend(Surface.Color, Accent.Color, light ? .08 : .13);
        Hover.Color = Blend(Surface.Color, Accent.Color, light ? .15 : .21);
        Pressed.Color = Blend(Surface.Color, Accent.Color, light ? .23 : .30);
        AccentSoft.Color = Blend(Surface.Color, Accent.Color, light ? .12 : .11);
        AccentLine.Color = Blend(Surface.Color, Accent.Color, light ? .34 : .40);
        Emphasis.Color = Accent.Color;
        OnAccent.Color = light ? Color.Parse("#FFFFFF") : Darken(Accent.Color, .82);
        ShadowColor = light ? Color.Parse("#1F17202A") : Color.Parse("#66050A0E");
        // The in-app mark follows the accent the way every other accent-coloured surface does.
        // Only the packaged .ico is fixed (sky blue): an icon file cannot read a runtime theme.
        MarkTop.Color = Accent.Color;
        MarkLeft.Color = Blend(Background.Color, Accent.Color, light ? .38 : .72);
        MarkRight.Color = Blend(Background.Color, Accent.Color, light ? .22 : .46);
        MarkCore.Color = Blend(Accent.Color, Color.Parse("#FFFFFF"), light ? .34 : .72);
        MarkTrail.Color = Blend(Accent.Color, Color.Parse("#FFFFFF"), light ? .12 : .42);
    }

    private static Color Blend(Color background, Color foreground, double amount) => Color.FromRgb(
        (byte)(background.R * (1 - amount) + foreground.R * amount),
        (byte)(background.G * (1 - amount) + foreground.G * amount),
        (byte)(background.B * (1 - amount) + foreground.B * amount));

    private static Color Darken(Color color, double amount) => Color.FromRgb(
        (byte)(color.R * (1 - amount)), (byte)(color.G * (1 - amount)), (byte)(color.B * (1 - amount)));
}
