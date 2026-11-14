// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

namespace Wonder.Extensibility.Contracts;

/// <summary>Coordinates are normalized to the component's natural width and height.</summary>
public readonly record struct ComponentPoint(double X, double Y);

/// <summary>Natural component dimensions use this DIP unit; the desktop scales it with components.</summary>
public static class ComponentGrid
{
    public const double Unit = 20;
}

/// <summary>A stable definition, independent of the desktop renderer and application state.</summary>
public sealed record ComponentDefinition(string Id, string Title, string Description, double Width, double Height)
{
    /// <summary>An empty outline represents the complete rectangular footprint.</summary>
    public IReadOnlyList<ComponentPoint> Outline { get; init; } = [];

    public ComponentDesktopOptions Desktop { get; init; } = new();
}

/// <summary>Optional desktop behavior; defaults preserve the host's ordinary component behavior.</summary>
public sealed record ComponentDesktopOptions
{
    /// <summary>Allow placement on the user's grid. Set false for components that require free movement.</summary>
    public bool SnapToGrid { get; init; } = true;
    /// <summary>Place a shortcut at the upper right only when this definition is first activated.</summary>
    public bool PlaceOnFirstActivation { get; init; }
    /// <summary>Use the containing area's width minus both horizontal margins; height remains natural.</summary>
    public bool StretchToAreaWidth { get; init; }
    public double HorizontalMargin { get; init; } = 24;
    /// <summary>Initial inset below the area's header, in natural component units.</summary>
    public double TopInset { get; init; } = 16;
    /// <summary>Orders components first placed by one activation. Existing shortcuts are never rearranged.</summary>
    public int FirstActivationOrder { get; init; }
    public double RevealDelayMilliseconds { get; init; }
    public double RevealDurationMilliseconds { get; init; }
    public double RevealOffsetY { get; init; }
    public double DragDelayMilliseconds { get; init; }
    public double MaximumDragTiltDegrees { get; init; }
}

/// <summary>Persistent data shared by every view of one application. Changes notify and save through the host.</summary>
public interface IComponentState
{
    IReadOnlyDictionary<string, string> Snapshot { get; }
    string Get(string key, string fallback = "");
    void Set(string key, string value);
}

/// <summary>
/// One runtime per application. All callbacks run on the host UI thread; operations must be short.
/// Resume and Suspend follow aggregate view visibility. Disposal releases all owned activity.
/// This synchronous contract does not authorize external background work or provide a plugin sandbox.
/// </summary>
public interface IComponentRuntime : IDisposable
{
    IReadOnlyDictionary<string, string> Values { get; }
    event EventHandler? Changed;
    void Resume();
    void Suspend();
    void Invoke(string actionId, string? value);
}
