// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

namespace Wonder.Extensibility.Contracts;

/// <summary>Coordinates are normalized to the component's natural width and height.</summary>
public readonly record struct ComponentPoint(double X, double Y);

/// <summary>A stable definition, independent of the desktop renderer and application state.</summary>
public sealed record ComponentDefinition(string Id, string Title, string Description, double Width, double Height)
{
    /// <summary>An empty outline represents the complete rectangular footprint.</summary>
    public IReadOnlyList<ComponentPoint> Outline { get; init; } = [];
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
