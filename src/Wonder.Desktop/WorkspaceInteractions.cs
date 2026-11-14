// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using System.Diagnostics;

namespace Wonder.Desktop;

public sealed partial class WorkspaceView
{
    /// <summary>Semantic actions within this workspace. Details never contain editor text.</summary>
    public event Action<string, string, string?>? InteractionObserved;

    private void ObserveInteraction(string kind, string target, string? detail = null) =>
        InteractionObserved?.Invoke(kind, target, detail);

    private void ObserveGesture(Gesture gesture, string phase, string? detail = null)
    {
        if (!gesture.Active) return;
        if (phase == "move")
        {
            var now = Stopwatch.GetTimestamp();
            if (gesture.LastObservation != 0 && Stopwatch.GetElapsedTime(gesture.LastObservation, now).TotalMilliseconds < 50) return;
            gesture.LastObservation = now;
        }
        var kind = gesture.CenterArea != null ? "center.drag" : gesture.SidebarArea != null ? "sidebar.resize" :
            gesture.Split != null ? "area.resize" : gesture.Component != null ? "component.drag" : "area.drag";
        var target = gesture.Component != null ? Components.GetDefinition(gesture.Component).Title :
            gesture.CenterArea?.Title ?? gesture.SidebarArea?.Title ?? Document.FindArea(gesture.AreaId ?? "")?.Title ?? "功能区分隔线";
        ObserveInteraction(kind + "." + phase, target, detail);
    }
}
