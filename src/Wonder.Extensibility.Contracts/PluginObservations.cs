// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

namespace Wonder.Extensibility.Contracts;

/// <summary>Observed launcher state. Elapsed uses a monotonic clock and is independent of TimestampUtc.</summary>
public sealed record PluginObservationSnapshot
{
    public DateTimeOffset TimestampUtc { get; init; }
    public TimeSpan Elapsed { get; init; }
    public PluginPointerObservation? Pointer { get; init; }
    /// <summary>At most five clicks, newest first. Sequence increases for each click.</summary>
    public IReadOnlyList<PluginClickObservation> RecentClicks { get; init; } = [];
    /// <summary>At most 64 launcher interactions, newest first. Text input never includes its contents.</summary>
    public IReadOnlyList<PluginInteractionObservation> RecentActions { get; init; } = [];
    public PluginInstanceObservation? SelectedInstance { get; init; }
}

/// <summary>Pointer coordinates relative to the client area of Surface; absent when not observed.</summary>
public sealed record PluginPointerObservation(double X, double Y, TimeSpan StationaryDuration, string Surface)
{
    public double SurfaceWidth { get; init; }
    public double SurfaceHeight { get; init; }
}
public sealed record PluginClickObservation(long Sequence, string Target, TimeSpan AtElapsed)
{
    /// <summary>left, right, middle, x1 or x2.</summary>
    public string Button { get; init; } = "left";
}
public sealed record PluginInteractionObservation(long Sequence, string Kind, string Target, string? Detail, TimeSpan AtElapsed);
public sealed record PluginInstanceObservation(string Id, string Name);
