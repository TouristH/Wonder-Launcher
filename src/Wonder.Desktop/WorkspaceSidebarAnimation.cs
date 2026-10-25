// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Diagnostics;
using Avalonia;
using Avalonia.Threading;
using Wonder.Workspace;

namespace Wonder.Desktop;

public sealed partial class WorkspaceView
{
    internal TimeSpan SidebarTransitionDuration { get; set; } = TimeSpan.FromMilliseconds(190);
    private readonly DispatcherTimer _sidebarAnimationTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _sidebarAnimationClock = new();
    private LayoutSnapshot? _sidebarAnimationFrom, _sidebarAnimationTo, _sidebarPresented;
    private Size _sidebarAnimationSize;
    private Dictionary<string, double> _sidebarStartOpacity = new();
    private double _sidebarAnimationProgress;

    private void InitializeSidebarAnimation()
    {
        _sidebarAnimationTimer.Tick += (_, _) =>
        {
            if (_sidebarAnimationFrom == null || _sidebarAnimationTo == null) { FinishSidebarAnimation(); return; }
            var elapsed = _sidebarAnimationClock.Elapsed.TotalMilliseconds / Math.Max(1, SidebarTransitionDuration.TotalMilliseconds);
            if (elapsed >= 1) FinishSidebarAnimation();
            else
            {
                _sidebarAnimationProgress = 1 - Math.Pow(1 - elapsed, 3);
                _sidebarPresented = InterpolateLayout(_sidebarAnimationFrom, _sidebarAnimationTo, _sidebarAnimationProgress);
            }
            InvalidateArrange();
        };
        DetachedFromVisualTree += (_, _) => FinishSidebarAnimation();
    }

    private void StartSidebarAnimation(LayoutSnapshot from, Dictionary<string, double>? sourceOpacity = null)
    {
        var target = MeasureWorkspaceTarget(Bounds.Size);
        var opacity = sourceOpacity ?? CaptureSidebarOpacity();
        FinishSidebarAnimation();
        if (SidebarTransitionDuration <= TimeSpan.Zero || VisualRoot == null || Bounds.Width <= 0 || Bounds.Height <= 0 ||
            from.Areas.Count != target.Areas.Count || target.Areas.Any(pair => !from.Areas.ContainsKey(pair.Key))) return;
        _sidebarAnimationFrom = from;
        _sidebarAnimationTo = target;
        _sidebarPresented = from;
        _sidebarAnimationSize = Bounds.Size;
        _sidebarStartOpacity = opacity;
        _sidebarAnimationProgress = 0;
        _sidebarAnimationClock.Restart();
        _sidebarAnimationTimer.Start();
        InvalidateArrange();
    }

    private Dictionary<string, double> CaptureSidebarOpacity() =>
        _areas.ToDictionary(pair => pair.Key, pair => pair.Value.IsVisible ? pair.Value.Opacity : 0);

    private void FinishSidebarAnimation()
    {
        _sidebarAnimationTimer.Stop();
        _sidebarAnimationClock.Reset();
        _sidebarAnimationFrom = _sidebarAnimationTo = _sidebarPresented = null;
        _sidebarStartOpacity.Clear();
        _sidebarAnimationProgress = 1;
    }

    private LayoutSnapshot MeasureWorkspace(Size size)
    {
        if (_sidebarPresented != null && size == _sidebarAnimationSize) return _sidebarPresented;
        if (_sidebarPresented != null) FinishSidebarAnimation();
        return MeasureWorkspaceTarget(size);
    }

    private bool IsSidebarContentVisible(string id) => id == _expandedSidebarId ||
        _sidebarPresented != null && _sidebarStartOpacity.GetValueOrDefault(id) > 0;

    private double SidebarContentOpacity(string id)
    {
        var target = id == _expandedSidebarId ? 1.0 : 0.0;
        if (_sidebarPresented == null) return target;
        var start = _sidebarStartOpacity.GetValueOrDefault(id);
        return start + (target - start) * _sidebarAnimationProgress;
    }

    private static LayoutSnapshot InterpolateLayout(LayoutSnapshot from, LayoutSnapshot to, double progress)
    {
        LayoutRect Blend(LayoutRect a, LayoutRect b) => new(
            a.X + (b.X - a.X) * progress, a.Y + (b.Y - a.Y) * progress,
            a.Width + (b.Width - a.Width) * progress, a.Height + (b.Height - a.Height) * progress);
        return new LayoutSnapshot
        {
            Areas = to.Areas.ToDictionary(pair => pair.Key, pair => Blend(from.Areas[pair.Key], pair.Value)),
            Splits = to.Splits.ToDictionary(pair => pair.Key, pair => from.Splits.TryGetValue(pair.Key, out var old) ? Blend(old, pair.Value) : pair.Value)
        };
    }
}
