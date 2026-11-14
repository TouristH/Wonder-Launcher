// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using Avalonia.Threading;
using Wonder.Extensibility.Contracts;

namespace Wonder.Desktop.Components;

/// <summary>Adapts activation-owned, framework-independent contributions to the desktop.</summary>
internal sealed class PluginComponentBridge(WorkspaceView workspace)
{
    private readonly Dictionary<string, PluginComponentRegistration> _current = new(StringComparer.Ordinal);

    internal bool Synchronize(IReadOnlyList<PluginComponentRegistration> contributions)
    {
        Dispatcher.UIThread.VerifyAccess();
        var next = contributions.ToDictionary(item => item.Definition.Id, StringComparer.Ordinal);
        var changed = _current.Keys.Union(next.Keys).Where(id =>
            !_current.TryGetValue(id, out var previous) || !next.TryGetValue(id, out var replacement) ||
            !ReferenceEquals(previous, replacement)).ToHashSet(StringComparer.Ordinal);
        if (changed.Count == 0) return false;
        foreach (var id in changed)
        {
            if (_current.Remove(id)) workspace.Components.Registry.Unregister(id);
            if (!next.TryGetValue(id, out var registration)) continue;
            workspace.Components.Registry.Register(registration.Definition, registration.CreateRuntime,
                context => PluginCardPresenter.Present(registration.Card, context));
            _current.Add(id, registration);
        }
        workspace.RefreshPluginComponents(changed);
        return true;
    }
}
