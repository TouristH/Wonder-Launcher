// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

namespace Wonder.Extensibility.Contracts;

/// <summary>A framework-independent card and its application runtime factory.</summary>
public sealed record PluginComponentRegistration(ComponentDefinition Definition, ComponentCard Card,
    Func<IComponentState, IComponentRuntime> CreateRuntime);

/// <summary>The host displays a title, bound text, optional rows, and a right-aligned footer.</summary>
public sealed record ComponentCard
{
    public string Title { get; init; } = "";
    public string TextKey { get; init; } = "text";
    public string FooterKey { get; init; } = "footer";
    /// <summary>Optional compact readouts between the main text and footer.</summary>
    public IReadOnlyList<ComponentCardRow> Rows { get; init; } = [];
    /// <summary>Keep the header visible while the text and rows scroll within the card.</summary>
    public bool Scrollable { get; init; }
    /// <summary>Null uses ordinary card presentation. A value selects a single-line status strip.</summary>
    public ComponentNarrationPresentation? Narration { get; init; }
    public double RowLineHeight { get; init; } = 17;
    public ComponentCardStyle Style { get; init; } = new();
}

/// <summary>A labeled value. An optional highlight binding is bright only when its value is "true".</summary>
public sealed record ComponentCardRow(string Label, string ValueKey)
{
    public string? HighlightKey { get; init; }
    public bool Monospace { get; init; }
    public string? SecondaryValueKey { get; init; }
    public string? ActionId { get; init; }
    public string? AccentColor { get; init; }
}

/// <summary>Bindings for a single-line status strip. Opacities are invariant numbers from zero to one.</summary>
public sealed record ComponentNarrationPresentation
{
    public string TextOpacityKey { get; init; } = "text-opacity";
    public string IndicatorOpacityKey { get; init; } = "indicator-opacity";
    public string IndicatorActiveKey { get; init; } = "indicator-active";
    public string ActiveColor { get; init; } = "#E0A458";
    public string IdleColor { get; init; } = "#4A5058";
}

/// <summary>Colors use #RRGGBB. Sizes and spacing use device-independent pixels.</summary>
public sealed record ComponentCardStyle
{
    public string Background { get; init; } = "#1B1E24";
    public double BackgroundOpacity { get; init; } = .92;
    public string Border { get; init; } = "#262A32";
    public string TitleColor { get; init; } = "#8A9099";
    public string TextColor { get; init; } = "#E8EAED";
    public string FooterColor { get; init; } = "#4A5058";
    public double CornerRadius { get; init; } = 8;
    public double Padding { get; init; } = 12;
    public double TitleFontSize { get; init; } = 11;
    public double TitleLetterSpacing { get; init; } = 2;
    public double TextFontSize { get; init; } = 12;
    public double FooterFontSize { get; init; } = 11;
}
