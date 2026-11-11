// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace Wonder.Desktop;

internal sealed class ClickRipple : Control
{
    private readonly Geometry _clip;
    private readonly Func<bool>? _isVisible;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _elapsed = new();
    private Point _origin;
    private double _progress = 1;
    public event Action? Finished;

    public ClickRipple(Geometry clip, Func<bool>? isVisible = null)
    {
        _clip = clip; _isVisible = isVisible;
        _timer.Tick += (_, _) =>
        {
            _progress = Math.Min(1, _elapsed.Elapsed.TotalMilliseconds / 350);
            if (_progress >= 1 || _isVisible?.Invoke() == false) Stop();
            InvalidateVisual();
        };
        DetachedFromVisualTree += (_, _) => Stop();
    }

    public void Start(Point origin)
    {
        if (VisualRoot == null) return;
        _origin = origin;
        _progress = 0;
        _elapsed.Restart();
        _timer.Start();
        InvalidateVisual();
    }

    private void Stop()
    {
        if (_progress == 1 && !_timer.IsEnabled) return;
        _timer.Stop();
        _elapsed.Reset();
        _progress = 1;
        InvalidateVisual();
        Finished?.Invoke();
    }

    public override void Render(DrawingContext context)
    {
        if (_progress >= 1 || _isVisible?.Invoke() == false) return;
        var x = Math.Max(_origin.X, Bounds.Width - _origin.X);
        var y = Math.Max(_origin.Y, Bounds.Height - _origin.Y);
        var radius = 4 + (Math.Sqrt(x * x + y * y) - 4) * (1 - Math.Pow(1 - _progress, 3));
        using (context.PushGeometryClip(_clip))
        using (context.PushOpacity(.30 * Math.Pow(1 - _progress, 1.5)))
            context.DrawEllipse(Palette.Accent, new Pen(Palette.Accent, 1.5), _origin, radius, radius);
    }
}

internal static class ButtonClickRipples
{
    private const uint GetClientAreaAnimation = 0x1042;
    private static readonly ConditionalWeakTable<Button, State> States = new();
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        InputElement.PointerPressedEvent.AddClassHandler<Button>((button, e) =>
        {
            if (button.ClickMode == ClickMode.Press && e.GetCurrentPoint(button).Properties.IsLeftButtonPressed)
                RememberOrigin(button, e);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.PointerReleasedEvent.AddClassHandler<Button>((button, e) =>
        {
            if (button.ClickMode == ClickMode.Release && e.InitialPressMouseButton == MouseButton.Left)
                RememberOrigin(button, e);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        Button.ClickEvent.AddClassHandler<Button>((button, e) =>
        {
            if (e.Source == button && button is not PolygonButton) Show(button);
        }, handledEventsToo: true);
    }

    private static void RememberOrigin(Button button, PointerEventArgs e)
    {
        if (button is PolygonButton) return;
        var state = States.GetOrCreateValue(button);
        state.Origin = e.GetPosition(button);
        var generation = ++state.Generation;
        Dispatcher.UIThread.Post(() => { if (state.Generation == generation) state.Origin = null; });
    }

    private static void Show(Button button)
    {
        var size = button.Bounds.Size;
        if (!button.IsEffectivelyVisible || size.Width <= 0 || size.Height <= 0 || !AnimationsEnabled() || AdornerLayer.GetAdornerLayer(button) is not { } layer) return;
        var state = States.GetOrCreateValue(button);
        var origin = state.Origin ?? new Point(size.Width / 2, size.Height / 2);
        state.Origin = null;
        state.RemoveActive?.Invoke();
        var clip = new RectangleGeometry(new Rect(size), button.CornerRadius.TopLeft, button.CornerRadius.TopLeft);
        var ripple = new ClickRipple(clip, () => button.IsEffectivelyVisible) { IsHitTestVisible = false };
        state.RemoveActive = Remove;
        AdornerLayer.SetAdornedElement(ripple, button);
        AdornerLayer.SetIsClipEnabled(ripple, true);
        layer.Children.Add(ripple);
        button.DetachedFromVisualTree += Detach;
        ripple.Finished += Remove;
        ripple.Start(origin);

        void Detach(object? sender, VisualTreeAttachmentEventArgs args) => Remove();
        void Remove()
        {
            ripple.Finished -= Remove;
            button.DetachedFromVisualTree -= Detach;
            state.RemoveActive = null;
            layer.Children.Remove(ripple);
        }
    }

    internal static bool AnimationsEnabled() => !OperatingSystem.IsWindows() ||
        !SystemParametersInfo(GetClientAreaAnimation, 0, out var enabled, 0) || enabled;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, [MarshalAs(UnmanagedType.Bool)] out bool value, uint flags);

    private sealed class State
    {
        public Point? Origin;
        public int Generation;
        public Action? RemoveActive;
    }
}
