// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Wonder.Extensibility.Contracts;

namespace Wonder.Extensibility;

public sealed partial class PluginManager
{
    private static PluginComponentRegistration ValidateComponent(PluginComponentRegistration registration, string pluginId)
    {
        ArgumentNullException.ThrowIfNull(registration);
        var definition = registration.Definition;
        if (definition is null || definition.Id is null || !definition.Id.StartsWith(pluginId + ".", StringComparison.Ordinal) ||
            definition.Id.Length <= pluginId.Length + 1 || definition.Id.Length > 160 ||
            !definition.Id.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-') ||
            !HasText(definition.Title, 256) || definition.Description is null || definition.Description.Length > 4096 ||
            !InRange(definition.Width, 1, 4096) || !InRange(definition.Height, 1, 4096) ||
            definition.Outline is null || definition.Outline.Count is 1 or 2 ||
            definition.Outline.Any(point => !InRange(point.X, 0, 1) || !InRange(point.Y, 0, 1)) ||
            definition.Desktop is not { } desktop || !InRange(desktop.DragDelayMilliseconds, 0, 1000) ||
            !InRange(desktop.MaximumDragTiltDegrees, 0, 45) || !InRange(desktop.HorizontalMargin, 0, 4096) ||
            !InRange(desktop.TopInset, 0, 4096) || desktop.FirstActivationOrder is < 0 or > 1024 ||
            !InRange(desktop.RevealDelayMilliseconds, 0, 60_000) || !InRange(desktop.RevealDurationMilliseconds, 0, 10_000) ||
            !InRange(desktop.RevealOffsetY, -1024, 1024) || registration.CreateRuntime is null)
            throw new InvalidDataException("插件组件的标识、尺寸、轮廓或桌面行为无效。");

        var card = registration.Card;
        if (card is null || card.Title is null || card.Title.Length > 256 ||
            !HasText(card.TextKey, 80) || !HasText(card.FooterKey, 80) || card.Style is not { } style ||
            !Color(style.Background) || !Color(style.Border) || !Color(style.TitleColor) ||
            !Color(style.TextColor) || !Color(style.FooterColor) ||
            !InRange(style.BackgroundOpacity, 0, 1) || !InRange(style.CornerRadius, 0, 2048) ||
            !InRange(style.Padding, 0, 1024) || !InRange(style.TitleLetterSpacing, -32, 128) ||
            !InRange(style.TitleFontSize, 1, 256) || !InRange(style.TextFontSize, 1, 256) ||
            !InRange(style.FooterFontSize, 1, 256) || !InRange(card.RowLineHeight, 1, 512))
            throw new InvalidDataException("插件卡片的文字绑定或样式无效。");

        if (card.Narration is { } narration && (card.Scrollable ||
            !HasText(narration.TextOpacityKey, 80) || !HasText(narration.IndicatorOpacityKey, 80) ||
            !HasText(narration.IndicatorActiveKey, 80) || !Color(narration.ActiveColor) || !Color(narration.IdleColor)))
            throw new InvalidDataException("插件状态条的绑定或样式无效。");

        if (card.Rows is null || card.Rows.Count > 128 || card.Rows.Any(row => row is null ||
            row.Label is null || row.Label.Length > 256 || !HasText(row.ValueKey, 80) ||
            row.HighlightKey is { } highlight && !HasText(highlight, 80) ||
            row.SecondaryValueKey is { } secondary && !HasText(secondary, 80) ||
            row.ActionId is { } action && !HasText(action, 80) || row.AccentColor is { } accent && !Color(accent)))
            throw new InvalidDataException("插件卡片的读数行无效。");

        return registration with
        {
            Definition = definition with { Outline = Array.AsReadOnly(definition.Outline.ToArray()) },
            Card = card with { Rows = Array.AsReadOnly(card.Rows.ToArray()) }
        };
    }

    private static bool InRange(double value, double minimum, double maximum) =>
        double.IsFinite(value) && value >= minimum && value <= maximum;

    private static bool Color(string value) => value is { Length: 7 } && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit);
}
