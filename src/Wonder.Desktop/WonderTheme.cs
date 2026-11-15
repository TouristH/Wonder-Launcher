// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;

namespace Wonder.Desktop;

/// <summary>
/// Recolours the Fluent controls by overriding the resource keys their ControlThemes read.
/// Replacing a ControlTheme wholesale is not an option: a theme without its own template
/// renders nothing. The values are the shared <see cref="Palette"/> brushes themselves, so a
/// later <see cref="Palette.Apply"/> repaints every open window without rebuilding this table.
/// </summary>
internal static class WonderTheme
{
    public static void Install()
    {
        var resources = Avalonia.Application.Current?.Resources;
        if (resources is null) return;

        Set(resources, "ButtonBackground", Palette.Surface);
        Set(resources, "ButtonBackgroundPointerOver", Palette.Hover);
        Set(resources, "ButtonBackgroundPressed", Palette.Pressed);
        Set(resources, "ButtonBackgroundDisabled", Palette.Sunken);
        Set(resources, "ButtonForeground", Palette.Text);
        Set(resources, "ButtonForegroundPointerOver", Palette.Text);
        Set(resources, "ButtonForegroundPressed", Palette.Text);
        Set(resources, "ButtonForegroundDisabled", Palette.Muted);
        Set(resources, "ButtonBorderBrush", Palette.LineStrong);
        Set(resources, "ButtonBorderBrushPointerOver", Palette.LineStrong);
        Set(resources, "ButtonBorderBrushPressed", Palette.AccentLine);
        Set(resources, "ButtonBorderBrushDisabled", Palette.Line);

        Set(resources, "AccentButtonBackground", Palette.Accent);
        Set(resources, "AccentButtonBackgroundPointerOver", Palette.Emphasis);
        Set(resources, "AccentButtonBackgroundPressed", Palette.Accent);
        Set(resources, "AccentButtonBackgroundDisabled", Palette.Sunken);
        Set(resources, "AccentButtonForeground", Palette.OnAccent);
        Set(resources, "AccentButtonForegroundPointerOver", Palette.OnAccent);
        Set(resources, "AccentButtonForegroundPressed", Palette.OnAccent);
        Set(resources, "AccentButtonForegroundDisabled", Palette.Muted);
        Set(resources, "AccentButtonBorderBrush", Palette.Accent);
        Set(resources, "AccentButtonBorderBrushPointerOver", Palette.Accent);
        Set(resources, "AccentButtonBorderBrushPressed", Palette.Accent);
        Set(resources, "AccentButtonBorderBrushDisabled", Palette.Line);

        Set(resources, "TextControlBackground", Palette.Sunken);
        Set(resources, "TextControlBackgroundPointerOver", Palette.Sunken);
        Set(resources, "TextControlBackgroundFocused", Palette.Sunken);
        Set(resources, "TextControlBackgroundDisabled", Palette.Sunken);
        Set(resources, "TextControlForeground", Palette.Text);
        Set(resources, "TextControlForegroundFocused", Palette.Text);
        Set(resources, "TextControlForegroundPointerOver", Palette.Text);
        Set(resources, "TextControlForegroundDisabled", Palette.Muted);
        Set(resources, "TextControlBorderBrush", Palette.LineStrong);
        Set(resources, "TextControlBorderBrushPointerOver", Palette.LineStrong);
        Set(resources, "TextControlBorderBrushFocused", Palette.Accent);
        Set(resources, "TextControlBorderBrushDisabled", Palette.Line);
        Set(resources, "TextControlPlaceholderForeground", Palette.Muted);
        Set(resources, "TextControlPlaceholderForegroundFocused", Palette.Muted);

        Set(resources, "ComboBoxBackground", Palette.Sunken);
        Set(resources, "ComboBoxBackgroundPointerOver", Palette.Sunken);
        Set(resources, "ComboBoxBackgroundPressed", Palette.Sunken);
        Set(resources, "ComboBoxBackgroundDisabled", Palette.Sunken);
        Set(resources, "ComboBoxBackgroundUnfocused", Palette.Sunken);
        Set(resources, "ComboBoxForeground", Palette.Text);
        Set(resources, "ComboBoxForegroundFocused", Palette.Text);
        Set(resources, "ComboBoxForegroundDisabled", Palette.Muted);
        Set(resources, "ComboBoxBorderBrush", Palette.LineStrong);
        Set(resources, "ComboBoxBorderBrushPointerOver", Palette.LineStrong);
        Set(resources, "ComboBoxBorderBrushPressed", Palette.AccentLine);
        Set(resources, "ComboBoxBorderBrushDisabled", Palette.Line);
        Set(resources, "ComboBoxBackgroundBorderBrushFocused", Palette.Accent);
        Set(resources, "ComboBoxPlaceHolderForeground", Palette.Muted);
        Set(resources, "ComboBoxDropDownBackground", Palette.Surface);
        Set(resources, "ComboBoxDropDownBorderBrush", Palette.LineStrong);
        Set(resources, "ComboBoxDropDownGlyphForeground", Palette.Muted);
        Set(resources, "ComboBoxDropDownGlyphForegroundDisabled", Palette.Line);
        Set(resources, "ComboBoxDropDownGlyphForegroundFocused", Palette.Accent);
        Set(resources, "ComboBoxDropDownGlyphForegroundFocusedPressed", Palette.Accent);
        Set(resources, "ComboBoxItemForeground", Palette.Text);
        Set(resources, "ComboBoxItemForegroundPointerOver", Palette.Text);
        Set(resources, "ComboBoxItemForegroundPressed", Palette.Text);
        Set(resources, "ComboBoxItemForegroundDisabled", Palette.Muted);
        Set(resources, "ComboBoxItemForegroundSelected", Palette.Text);
        Set(resources, "ComboBoxItemForegroundSelectedPointerOver", Palette.Text);
        Set(resources, "ComboBoxItemForegroundSelectedPressed", Palette.Text);
        Set(resources, "ComboBoxItemForegroundSelectedDisabled", Palette.Muted);
        Set(resources, "ComboBoxItemBackground", Palette.Surface);
        Set(resources, "ComboBoxItemBackgroundPointerOver", Palette.Hover);
        Set(resources, "ComboBoxItemBackgroundPressed", Palette.Pressed);
        Set(resources, "ComboBoxItemBackgroundDisabled", Palette.Surface);
        Set(resources, "ComboBoxItemBackgroundSelected", Palette.Active);
        Set(resources, "ComboBoxItemBackgroundSelectedPointerOver", Palette.Hover);
        Set(resources, "ComboBoxItemBackgroundSelectedPressed", Palette.Pressed);
        Set(resources, "ComboBoxItemBackgroundSelectedDisabled", Palette.Sunken);
        Set(resources, "ComboBoxItemBorderBrushPointerOver", Palette.LineStrong);
        Set(resources, "ComboBoxItemBorderBrushPressed", Palette.AccentLine);
        Set(resources, "ComboBoxItemBorderBrushDisabled", Palette.Line);
        Set(resources, "ComboBoxItemBorderBrushSelected", Palette.AccentLine);
        Set(resources, "ComboBoxItemBorderBrushSelectedPointerOver", Palette.LineStrong);
        Set(resources, "ComboBoxItemBorderBrushSelectedPressed", Palette.AccentLine);
        Set(resources, "ComboBoxItemBorderBrushSelectedDisabled", Palette.Line);

        Set(resources, "CheckBoxCheckBackgroundFillUnchecked", Palette.Sunken);
        Set(resources, "CheckBoxCheckBackgroundFillUncheckedPointerOver", Palette.Sunken);
        Set(resources, "CheckBoxCheckBackgroundFillUncheckedPressed", Palette.Surface);
        Set(resources, "CheckBoxCheckBackgroundFillUncheckedDisabled", Palette.Sunken);
        Set(resources, "CheckBoxCheckBackgroundFillChecked", Palette.Accent);
        Set(resources, "CheckBoxCheckBackgroundFillCheckedPointerOver", Palette.Emphasis);
        Set(resources, "CheckBoxCheckBackgroundFillCheckedPressed", Palette.Accent);
        Set(resources, "CheckBoxCheckBackgroundFillCheckedDisabled", Palette.Sunken);
        Set(resources, "CheckBoxCheckBackgroundFillIndeterminate", Palette.Accent);
        Set(resources, "CheckBoxCheckBackgroundFillIndeterminatePointerOver", Palette.Emphasis);
        Set(resources, "CheckBoxCheckBackgroundFillIndeterminatePressed", Palette.Accent);
        Set(resources, "CheckBoxCheckBackgroundFillIndeterminateDisabled", Palette.Sunken);
        Set(resources, "CheckBoxCheckBackgroundStrokeUnchecked", Palette.LineStrong);
        Set(resources, "CheckBoxCheckBackgroundStrokeUncheckedPointerOver", Palette.LineStrong);
        Set(resources, "CheckBoxCheckBackgroundStrokeUncheckedPressed", Palette.AccentLine);
        Set(resources, "CheckBoxCheckBackgroundStrokeUncheckedDisabled", Palette.Line);
        Set(resources, "CheckBoxCheckBackgroundStrokeCheckedPointerOver", Palette.Emphasis);
        Set(resources, "CheckBoxCheckBackgroundStrokeCheckedPressed", Palette.Accent);
        Set(resources, "CheckBoxCheckBackgroundStrokeCheckedDisabled", Palette.Line);
        Set(resources, "CheckBoxCheckBackgroundStrokeIndeterminate", Palette.Accent);
        Set(resources, "CheckBoxCheckBackgroundStrokeIndeterminatePointerOver", Palette.Emphasis);
        Set(resources, "CheckBoxCheckBackgroundStrokeIndeterminatePressed", Palette.Accent);
        Set(resources, "CheckBoxCheckBackgroundStrokeIndeterminateDisabled", Palette.Line);
        Set(resources, "CheckBoxCheckGlyphForegroundUnchecked", Palette.Sunken);
        Set(resources, "CheckBoxCheckGlyphForegroundUncheckedPointerOver", Palette.Sunken);
        Set(resources, "CheckBoxCheckGlyphForegroundUncheckedPressed", Palette.Sunken);
        Set(resources, "CheckBoxCheckGlyphForegroundUncheckedDisabled", Palette.Muted);
        Set(resources, "CheckBoxCheckGlyphForegroundChecked", Palette.OnAccent);
        Set(resources, "CheckBoxCheckGlyphForegroundCheckedPointerOver", Palette.OnAccent);
        Set(resources, "CheckBoxCheckGlyphForegroundCheckedPressed", Palette.OnAccent);
        Set(resources, "CheckBoxCheckGlyphForegroundCheckedDisabled", Palette.Muted);
        Set(resources, "CheckBoxCheckGlyphForegroundIndeterminate", Palette.OnAccent);
        Set(resources, "CheckBoxCheckGlyphForegroundIndeterminatePointerOver", Palette.OnAccent);
        Set(resources, "CheckBoxCheckGlyphForegroundIndeterminatePressed", Palette.OnAccent);
        Set(resources, "CheckBoxCheckGlyphForegroundIndeterminateDisabled", Palette.Muted);
        Set(resources, "CheckBoxForegroundChecked", Palette.Accent);
        Set(resources, "CheckBoxBorderBrushChecked", Palette.Accent);
        Set(resources, "CheckBoxBackgroundChecked", Palette.AccentSoft);

        Set(resources, "RadioButtonOuterEllipseFill", Palette.Sunken);
        Set(resources, "RadioButtonOuterEllipseFillPointerOver", Palette.Sunken);
        Set(resources, "RadioButtonOuterEllipseFillPressed", Palette.Surface);
        Set(resources, "RadioButtonOuterEllipseFillDisabled", Palette.Sunken);
        Set(resources, "RadioButtonOuterEllipseStroke", Palette.LineStrong);
        Set(resources, "RadioButtonOuterEllipseStrokePointerOver", Palette.LineStrong);
        Set(resources, "RadioButtonOuterEllipseStrokePressed", Palette.AccentLine);
        Set(resources, "RadioButtonOuterEllipseStrokeDisabled", Palette.Line);
        Set(resources, "RadioButtonOuterEllipseCheckedFill", Palette.Accent);
        Set(resources, "RadioButtonOuterEllipseCheckedFillPointerOver", Palette.Emphasis);
        Set(resources, "RadioButtonOuterEllipseCheckedFillPressed", Palette.Accent);
        Set(resources, "RadioButtonOuterEllipseCheckedFillDisabled", Palette.Sunken);
        Set(resources, "RadioButtonOuterEllipseCheckedStroke", Palette.Accent);
        Set(resources, "RadioButtonOuterEllipseCheckedStrokePointerOver", Palette.Emphasis);
        Set(resources, "RadioButtonOuterEllipseCheckedStrokePressed", Palette.Accent);
        Set(resources, "RadioButtonOuterEllipseCheckedStrokeDisabled", Palette.Line);
        Set(resources, "RadioButtonCheckGlyphFill", Palette.OnAccent);
        Set(resources, "RadioButtonCheckGlyphFillPointerOver", Palette.OnAccent);
        Set(resources, "RadioButtonCheckGlyphFillPressed", Palette.OnAccent);
        Set(resources, "RadioButtonCheckGlyphFillDisabled", Palette.Muted);

        Set(resources, "SliderThumbBackground", Palette.Accent);
        Set(resources, "SliderThumbBackgroundPointerOver", Palette.Emphasis);
        Set(resources, "SliderThumbBackgroundPressed", Palette.Accent);
        Set(resources, "SliderThumbBackgroundDisabled", Palette.Muted);
        Set(resources, "SliderTrackValueFill", Palette.Accent);
        Set(resources, "SliderTrackValueFillPointerOver", Palette.Emphasis);
        Set(resources, "SliderTrackValueFillPressed", Palette.Accent);
        Set(resources, "SliderTrackValueFillDisabled", Palette.Line);
        Set(resources, "SliderTrackFill", Palette.Sunken);
        Set(resources, "SliderTrackFillPointerOver", Palette.Sunken);
        Set(resources, "SliderTrackFillPressed", Palette.Surface);
        Set(resources, "SliderTrackFillDisabled", Palette.Sunken);

        Set(resources, "ToggleSwitchFillOn", Palette.Accent);
        Set(resources, "ToggleSwitchFillOnPointerOver", Palette.Emphasis);
        Set(resources, "ToggleSwitchFillOnPressed", Palette.Accent);
        Set(resources, "ToggleSwitchFillOnDisabled", Palette.Sunken);
        Set(resources, "ToggleSwitchFillOff", Palette.LineStrong);
        Set(resources, "ToggleSwitchFillOffPointerOver", Palette.LineStrong);
        Set(resources, "ToggleSwitchFillOffPressed", Palette.Line);
        Set(resources, "ToggleSwitchKnobFillOn", Palette.OnAccent);
        Set(resources, "ToggleSwitchKnobFillOnPointerOver", Palette.OnAccent);
        Set(resources, "ToggleSwitchKnobFillOnPressed", Palette.OnAccent);
        Set(resources, "ToggleSwitchKnobFillOnDisabled", Palette.Muted);
        Set(resources, "ToggleSwitchKnobFillOff", Palette.Surface);
        Set(resources, "ToggleSwitchKnobFillOffPointerOver", Palette.Surface);
        Set(resources, "ToggleSwitchKnobFillOffPressed", Palette.Surface);
        Set(resources, "ToggleSwitchKnobFillOffDisabled", Palette.Muted);
        Set(resources, "ToggleSwitchStrokeOn", Palette.Accent);
        Set(resources, "ToggleSwitchStrokeOff", Palette.LineStrong);
        Set(resources, "ToggleSwitchStrokeOnDisabled", Palette.Line);
        Set(resources, "ToggleSwitchStrokeOffDisabled", Palette.Line);

        Set(resources, "ScrollBarThumbFillPointerOver", Palette.Muted);
        Set(resources, "ScrollBarThumbFillPressed", Palette.AccentLine);
        Set(resources, "ScrollBarThumbFillDisabled", Palette.Line);
        Set(resources, "ScrollBarTrackFill", Palette.Background);

        Set(resources, "MenuFlyoutPresenterBackground", Palette.Surface);
        Set(resources, "MenuFlyoutPresenterBorderBrush", Palette.LineStrong);
        Set(resources, "MenuFlyoutItemBackgroundPointerOver", Palette.Hover);
        Set(resources, "MenuFlyoutItemBackgroundPressed", Palette.Pressed);
        Set(resources, "MenuFlyoutItemForeground", Palette.Text);
        Set(resources, "MenuFlyoutItemForegroundPointerOver", Palette.Text);
        Set(resources, "MenuFlyoutItemForegroundDisabled", Palette.Muted);

        Set(resources, "ToolTipBackground", Palette.Raised);
        Set(resources, "ToolTipForeground", Palette.Text);
        Set(resources, "ToolTipBorderBrush", Palette.LineStrong);

        Set(resources, "SystemControlHighlightAccentBrush", Palette.Accent);
        Set(resources, "SystemControlFocusVisualPrimaryBrush", Palette.Accent);
        Set(resources, "SystemControlFocusVisualSecondaryBrush", Palette.LineStrong);

        resources["ControlCornerRadius"] = new CornerRadius(8);
        resources["OverlayCornerRadius"] = new CornerRadius(12);
        resources["ButtonPadding"] = new Thickness(14, 7);
    }

    private static void Set(IResourceDictionary resources, string key, object value) => resources[key] = value;
}
