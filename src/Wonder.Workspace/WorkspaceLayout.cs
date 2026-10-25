// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Collections.ObjectModel;

namespace Wonder.Workspace;

public readonly record struct LayoutRect(double X, double Y, double Width, double Height);

public sealed class LayoutSnapshot
{
    public IReadOnlyDictionary<string, LayoutRect> Areas { get; init; } = new Dictionary<string, LayoutRect>();
    public IReadOnlyDictionary<string, LayoutRect> Splits { get; init; } = new Dictionary<string, LayoutRect>();
}

public static class WorkspaceLayout
{
    public const double DefaultGap = 10;
    public const double MinimumAreaWidth = 160;
    public const double MinimumAreaHeight = 120;
    public const double SidebarRailSize = 28;
    public const double SidebarCollapseThreshold = 64;
    public const double DefaultSidebarExtent = 320;

    /// <summary>Returns the outer edge eligible for explicitly collapsing a direct leaf during a splitter drag.</summary>
    public static SidebarEdge GetSidebarEdge(LayoutNode split, bool first, LayoutRect bounds, double width, double height)
    {
        var child = first ? split.First : split.Second;
        if (split.Area is not null || child?.Area is not { SidebarEdge: SidebarEdge.None } ||
            !double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
            return SidebarEdge.None;
        const double epsilon = .001;
        if (split.Axis == SplitAxis.Horizontal)
            return first
                ? Math.Abs(bounds.X) <= epsilon ? SidebarEdge.Left : SidebarEdge.None
                : Math.Abs(bounds.X + bounds.Width - width) <= epsilon ? SidebarEdge.Right : SidebarEdge.None;
        return first
            ? Math.Abs(bounds.Y) <= epsilon ? SidebarEdge.Top : SidebarEdge.None
            : Math.Abs(bounds.Y + bounds.Height - height) <= epsilon ? SidebarEdge.Bottom : SidebarEdge.None;
    }

    /// <summary>Converts a fixed center offset to top-left coordinates in the entire region, without clamping.</summary>
    public static (double X, double Y) FromCenterOffset(
        double areaWidth, double areaHeight, double componentWidth, double componentHeight, double offsetX, double offsetY) =>
        (NormalizeLength(areaWidth) / 2 + FiniteOrZero(offsetX) - NormalizeLength(componentWidth) / 2,
         NormalizeLength(areaHeight) / 2 + FiniteOrZero(offsetY) - NormalizeLength(componentHeight) / 2);

    /// <summary>Converts top-left coordinates to signed pixel offsets from the entire region center.</summary>
    public static (double X, double Y) ToCenterOffset(
        double areaWidth, double areaHeight, double componentWidth, double componentHeight, double topLeftX, double topLeftY) =>
        (FiniteOrZero(topLeftX) + NormalizeLength(componentWidth) / 2 - NormalizeLength(areaWidth) / 2,
         FiniteOrZero(topLeftY) + NormalizeLength(componentHeight) / 2 - NormalizeLength(areaHeight) / 2);

    /// <summary>Measures normal and temporarily expanded sidebar areas without changing saved sizes or split ratios.</summary>
    public static LayoutSnapshot Measure(WorkspaceDocument document, double width, double height, double gap = DefaultGap,
        string? expandedSidebarId = null, double? expandedSidebarExtent = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var areas = new Dictionary<string, LayoutRect>();
        var splits = new Dictionary<string, LayoutRect>();
        var minimums = new Dictionary<LayoutNode, Sizing>();
        gap = NormalizeLength(gap);
        if (document.Root is not null)
        {
            Minimum(document.Root);
            Arrange(document.Root, new LayoutRect(0, 0, NormalizeLength(width), NormalizeLength(height)));
        }

        return new LayoutSnapshot
        {
            Areas = new ReadOnlyDictionary<string, LayoutRect>(areas),
            Splits = new ReadOnlyDictionary<string, LayoutRect>(splits)
        };

        Sizing Minimum(LayoutNode node)
        {
            if (node.Area is not null)
            {
                var expanded = IsExpanded(node.Area);
                var extent = SidebarSize(node.Area);
                var minimum = expanded ? Math.Min(80, extent) : SidebarRailSize;
                return minimums[node] = node.Area.SidebarEdge switch
                {
                    SidebarEdge.Left or SidebarEdge.Right => new Sizing(minimum, MinimumAreaHeight, extent, null, expanded),
                    SidebarEdge.Top or SidebarEdge.Bottom => new Sizing(MinimumAreaWidth, minimum, null, extent, expanded),
                    _ => new Sizing(MinimumAreaWidth, MinimumAreaHeight, null, null, false)
                };
            }
            var first = Minimum(node.First!);
            var second = Minimum(node.Second!);
            var horizontal = node.Axis == SplitAxis.Horizontal;
            var firstFixed = horizontal ? first.FixedWidth : first.FixedHeight;
            var secondFixed = horizontal ? second.FixedWidth : second.FixedHeight;
            var expandedFixed = firstFixed.HasValue && first.Expanded || secondFixed.HasValue && second.Expanded;
            var divider = firstFixed.HasValue || secondFixed.HasValue ? expandedFixed ? gap : 0 : gap;
            return minimums[node] = node.Axis == SplitAxis.Horizontal
                ? new Sizing(first.Width + second.Width + divider,
                    Math.Max(first.Height, second.Height),
                    first.FixedWidth.HasValue && second.FixedWidth.HasValue ? first.FixedWidth.Value + second.FixedWidth.Value + divider : null,
                    first.FixedHeight.HasValue && second.FixedHeight.HasValue ? Math.Max(first.FixedHeight.Value, second.FixedHeight.Value) : null,
                    first.Expanded || second.Expanded)
                : new Sizing(Math.Max(first.Width, second.Width),
                    first.Height + second.Height + divider,
                    first.FixedWidth.HasValue && second.FixedWidth.HasValue ? Math.Max(first.FixedWidth.Value, second.FixedWidth.Value) : null,
                    first.FixedHeight.HasValue && second.FixedHeight.HasValue ? first.FixedHeight.Value + second.FixedHeight.Value + divider : null,
                    first.Expanded || second.Expanded);
        }

        bool IsExpanded(Area area) => area.SidebarEdge != SidebarEdge.None && area.Id == expandedSidebarId;

        double SidebarSize(Area area)
        {
            if (!IsExpanded(area))
                return SidebarRailSize;
            if (expandedSidebarExtent is { } preview && double.IsFinite(preview))
                return Math.Max(0, preview);
            return Math.Clamp(double.IsFinite(area.SidebarExtent) ? area.SidebarExtent : DefaultSidebarExtent, 80, DefaultSidebarExtent);
        }

        void Arrange(LayoutNode node, LayoutRect bounds)
        {
            if (node.Area is not null)
            {
                var extent = SidebarSize(node.Area);
                var railWidth = Math.Min(bounds.Width, extent);
                var railHeight = Math.Min(bounds.Height, extent);
                areas.Add(node.Area.Id, node.Area.SidebarEdge switch
                {
                    SidebarEdge.Left => bounds with { Width = railWidth },
                    SidebarEdge.Right => bounds with { X = bounds.X + bounds.Width - railWidth, Width = railWidth },
                    SidebarEdge.Top => bounds with { Height = railHeight },
                    SidebarEdge.Bottom => bounds with { Y = bounds.Y + bounds.Height - railHeight, Height = railHeight },
                    _ => bounds
                });
                return;
            }

            var horizontal = node.Axis == SplitAxis.Horizontal;
            var length = horizontal ? bounds.Width : bounds.Height;
            var firstSizing = minimums[node.First!];
            var secondSizing = minimums[node.Second!];
            var firstFixed = horizontal ? firstSizing.FixedWidth : firstSizing.FixedHeight;
            var secondFixed = horizontal ? secondSizing.FixedWidth : secondSizing.FixedHeight;
            if (firstFixed.HasValue || secondFixed.HasValue)
            {
                var expandFirst = firstFixed.HasValue && firstSizing.Expanded;
                var expandSecond = secondFixed.HasValue && secondSizing.Expanded;
                var availableForChildren = Math.Max(0, length - (expandFirst || expandSecond ? Math.Min(gap, length) : 0));
                double firstSize, secondSize;
                if (expandFirst)
                {
                    // Prefer the sibling's minimum (including a closed 28px rail)
                    // over the expanded panel's requested size when its parent is small.
                    var siblingMinimum = horizontal ? secondSizing.Width : secondSizing.Height;
                    firstSize = Math.Min(firstFixed!.Value, Math.Max(0, availableForChildren - siblingMinimum));
                    secondSize = secondFixed.HasValue ? Math.Min(secondFixed.Value, availableForChildren - firstSize) : availableForChildren - firstSize;
                }
                else if (expandSecond)
                {
                    var siblingMinimum = horizontal ? firstSizing.Width : firstSizing.Height;
                    secondSize = Math.Min(secondFixed!.Value, Math.Max(0, availableForChildren - siblingMinimum));
                    firstSize = firstFixed.HasValue ? Math.Min(firstFixed.Value, availableForChildren - secondSize) : availableForChildren - secondSize;
                }
                else
                {
                    // Closed rails retain their former no-gap layout. Two fixed
                    // children stay at opposite edges with an unoccupied center.
                    firstSize = firstFixed.HasValue ? Math.Min(length, firstFixed.Value) : Math.Max(0, length - secondFixed!.Value);
                    secondSize = secondFixed.HasValue ? Math.Min(length - firstSize, secondFixed.Value) : length - firstSize;
                }
                if (horizontal)
                {
                    Arrange(node.First!, new LayoutRect(bounds.X, bounds.Y, firstSize, bounds.Height));
                    Arrange(node.Second!, new LayoutRect(bounds.X + length - secondSize, bounds.Y, secondSize, bounds.Height));
                }
                else
                {
                    Arrange(node.First!, new LayoutRect(bounds.X, bounds.Y, bounds.Width, firstSize));
                    Arrange(node.Second!, new LayoutRect(bounds.X, bounds.Y + length - secondSize, bounds.Width, secondSize));
                }
                return;
            }
            var divider = Math.Min(gap, length);
            var available = Math.Max(0, length - divider);
            var firstMinimum = horizontal ? minimums[node.First!].Width : minimums[node.First!].Height;
            var secondMinimum = horizontal ? minimums[node.Second!].Width : minimums[node.Second!].Height;
            var requested = double.IsFinite(node.Ratio) ? Math.Clamp(node.Ratio, 0, 1) : .5;
            // When there is no room for both minima, share the available space in proportion.
            var firstLength = available >= firstMinimum + secondMinimum
                ? Math.Clamp(available * requested, firstMinimum, available - secondMinimum)
                : available * firstMinimum / (firstMinimum + secondMinimum);
            var secondLength = Math.Max(0, available - firstLength);

            if (horizontal)
            {
                splits.Add(node.Id, new LayoutRect(bounds.X + firstLength, bounds.Y, divider, bounds.Height));
                Arrange(node.First!, new LayoutRect(bounds.X, bounds.Y, firstLength, bounds.Height));
                Arrange(node.Second!, new LayoutRect(bounds.X + firstLength + divider, bounds.Y, secondLength, bounds.Height));
            }
            else
            {
                splits.Add(node.Id, new LayoutRect(bounds.X, bounds.Y + firstLength, bounds.Width, divider));
                Arrange(node.First!, new LayoutRect(bounds.X, bounds.Y, bounds.Width, firstLength));
                Arrange(node.Second!, new LayoutRect(bounds.X, bounds.Y + firstLength + divider, bounds.Width, secondLength));
            }
        }
    }

    /// <summary>Clamps a top-left position to the component's available travel inside its region.</summary>
    public static (double X, double Y) ClampComponent(double x, double y, double width, double height, double areaWidth, double areaHeight) =>
        (ClampPosition(x, areaWidth, width), ClampPosition(y, areaHeight, height));

    private static double ClampPosition(double requested, double available, double component) =>
        Math.Clamp(double.IsFinite(requested) ? requested : 0, 0, Math.Max(0, NormalizeLength(available) - NormalizeLength(component)));

    private static double NormalizeLength(double value) => double.IsFinite(value) && value > 0 ? value : 0;
    private static double FiniteOrZero(double value) => double.IsFinite(value) ? value : 0;
    private readonly record struct Sizing(double Width, double Height, double? FixedWidth, double? FixedHeight, bool Expanded);
}
