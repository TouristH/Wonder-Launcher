// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Wonder.Extensibility;

namespace Wonder.Desktop;

/// <summary>Observes input in this launcher, including owned windows, popups and native drag routes.</summary>
internal sealed class PluginDesktopObservation : IDisposable
{
    private readonly Window _main;
    private readonly PluginObservationHub _hub;
    private readonly IDisposable[] _subscriptions;
    private readonly HashSet<Window> _windows = [];
    private TopLevel? _lastSurface;
    private bool _disposed;

    public PluginDesktopObservation(Window main, PluginObservationHub hub)
    {
        _main = main;
        _hub = hub;
        _subscriptions =
        [
            InputElement.PointerMovedEvent.AddClassHandler<TopLevel>(Move, RoutingStrategies.Tunnel, handledEventsToo: true),
            InputElement.PointerPressedEvent.AddClassHandler<TopLevel>(Press, RoutingStrategies.Tunnel, handledEventsToo: true),
            InputElement.PointerReleasedEvent.AddClassHandler<TopLevel>(Release, RoutingStrategies.Tunnel, handledEventsToo: true),
            InputElement.PointerWheelChangedEvent.AddClassHandler<TopLevel>(Wheel, RoutingStrategies.Tunnel, handledEventsToo: true),
            InputElement.KeyDownEvent.AddClassHandler<TopLevel>(KeyDown, RoutingStrategies.Tunnel, handledEventsToo: true),
            InputElement.TextInputEvent.AddClassHandler<TopLevel>((surface, args) =>
            {
                if (EventWindow(surface, args) is { } window)
                    _hub.RecordInteraction("text.input", Target(args.Source as Visual, window.Title ?? "启动器"));
            }, RoutingStrategies.Tunnel, handledEventsToo: true),
            DragDrop.DragEnterEvent.AddClassHandler<TopLevel>(Drag, handledEventsToo: true),
            DragDrop.DragOverEvent.AddClassHandler<TopLevel>(Drag, handledEventsToo: true),
            DragDrop.DropEvent.AddClassHandler<TopLevel>(Drag, handledEventsToo: true),
            DragDrop.DragLeaveEvent.AddClassHandler<TopLevel>((surface, _) => Clear(surface), handledEventsToo: true),
            InputElement.PointerExitedEvent.AddClassHandler<TopLevel>((surface, args) =>
            {
                if (ReferenceEquals(args.Source, surface)) Clear(surface);
            }, handledEventsToo: true),
            Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            {
                WatchWindow(window);
                // Show(owner) may raise Opened before it assigns Owner. Resolve that ownership
                // after Show returns; do not silently lose the child's lifecycle events.
                if (!_windows.Contains(window)) Dispatcher.UIThread.Post(() =>
                {
                    if (window.IsVisible) WatchWindow(window);
                });
            }),
            Window.WindowClosedEvent.AddClassHandler<Window>((window, _) =>
            {
                Clear(window);
                if (_windows.Remove(window))
                {
                    UnwatchWindow(window);
                    _hub.RecordInteraction("window.close", window.Title ?? "启动器");
                }
            }),
            Window.WindowStateProperty.Changed.AddClassHandler<Window>((window, _) =>
            {
                if (!_disposed && _windows.Contains(window))
                    _hub.RecordInteraction("window.state", window.Title ?? "启动器", window.WindowState.ToString());
            }),
            Window.IsActiveProperty.Changed.AddClassHandler<Window>((window, _) =>
            {
                if (!window.IsActive) Clear(window);
            })
        ];
        if (main.IsVisible) WatchWindow(main);
    }

    private Window? Owner(TopLevel surface)
    {
        if (surface is PopupRoot popup) return Owner(popup.ParentTopLevel);
        Window? window = null;
        for (var ancestor = surface as WindowBase; ancestor is not null; ancestor = ancestor.Owner)
        {
            window ??= ancestor as Window;
            if (ReferenceEquals(ancestor, _main)) return window;
        }
        return null;
    }

    // A popup's routed event also visits its owner. Observe the originating surface once.
    private Window? EventWindow(TopLevel surface, RoutedEventArgs args) =>
        _disposed || args.Source is Visual visual && TopLevel.GetTopLevel(visual) is { } origin && !ReferenceEquals(origin, surface)
            ? null : Owner(surface);

    private bool ObservePosition(TopLevel surface, Point point, Window window)
    {
        if (!new Rect(surface.Bounds.Size).Contains(point)) { Clear(surface); return false; }
        _lastSurface = surface;
        _hub.MovePointer(point.X, point.Y,
            ReferenceEquals(surface, window) ? window.Title ?? "启动器" : (window.Title ?? "启动器") + " · 菜单",
            surface.Bounds.Width, surface.Bounds.Height);
        return true;
    }

    private void Move(TopLevel surface, PointerEventArgs args)
    {
        if (args.Pointer.Type == PointerType.Mouse && EventWindow(surface, args) is { } window)
            ObservePosition(surface, args.GetPosition(surface), window);
    }

    private void Press(TopLevel surface, PointerPressedEventArgs args)
    {
        if (args.Pointer.Type != PointerType.Mouse || EventWindow(surface, args) is not { } window ||
            !ObservePosition(surface, args.GetPosition(surface), window)) return;
        var button = args.GetCurrentPoint(surface).Properties.PointerUpdateKind switch
        {
            PointerUpdateKind.RightButtonPressed => "right",
            PointerUpdateKind.MiddleButtonPressed => "middle",
            PointerUpdateKind.XButton1Pressed => "x1",
            PointerUpdateKind.XButton2Pressed => "x2",
            _ => "left"
        };
        _hub.RecordClick(Target(args.Source as Visual, window.Title ?? "启动器"), button);
    }

    private void Release(TopLevel surface, PointerReleasedEventArgs args)
    {
        if (args.Pointer.Type != PointerType.Mouse || EventWindow(surface, args) is not { } window ||
            !ObservePosition(surface, args.GetPosition(surface), window)) return;
        _hub.RecordInteraction("pointer.release", Target(args.Source as Visual, window.Title ?? "启动器"), args.InitialPressMouseButton.ToString().ToLowerInvariant());
    }

    private void Wheel(TopLevel surface, PointerWheelEventArgs args)
    {
        if (EventWindow(surface, args) is not { } window || !ObservePosition(surface, args.GetPosition(surface), window)) return;
        _hub.RecordInteraction("pointer.wheel", Target(args.Source as Visual, window.Title ?? "启动器"),
            string.Create(CultureInfo.InvariantCulture, $"{args.Delta.X:0.###},{args.Delta.Y:0.###}"));
    }

    private void Drag(TopLevel surface, DragEventArgs args)
    {
        // Native drag loops deliver DragOver instead of PointerMoved. Keep the same position
        // stream through this public route, without relying on Avalonia's private raw-input API.
        if (EventWindow(surface, args) is not { } window || !ObservePosition(surface, args.GetPosition(surface), window)) return;
        if (args.RoutedEvent == DragDrop.DragEnterEvent || args.RoutedEvent == DragDrop.DropEvent)
            _hub.RecordInteraction(args.RoutedEvent == DragDrop.DropEvent ? "drag.drop" : "drag.enter",
                Target(args.Source as Visual, window.Title ?? "启动器"));
    }

    private void KeyDown(TopLevel surface, KeyEventArgs args)
    {
        if (EventWindow(surface, args) is not { } window) return;
        if (args.Key is Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) return;
        var target = Target(args.Source as Visual, window.Title ?? "启动器");
        var shortcut = (args.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) != 0;
        // AltGr can produce text. Never turn it into a stream of printable input keys.
        if ((args.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt)) == (KeyModifiers.Control | KeyModifiers.Alt)) shortcut = false;
        if (shortcut)
            _hub.RecordInteraction("keyboard.shortcut", target, args.KeyModifiers.ToString().Replace(", ", "+") + "+" + args.Key);
        else if (args.Key is Key.Enter or Key.Escape or Key.Tab or Key.Space or Key.Back or Key.Delete or
            Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown || args.Key is >= Key.F1 and <= Key.F24)
            _hub.RecordInteraction("keyboard.key", target, args.Key.ToString());
        // Printable characters are represented solely by text.input, with no text/key payload.
    }

    private static string Target(Visual? source, string fallback)
    {
        if (source is null) return fallback;
        var ancestors = source.GetVisualAncestors().Prepend(source).OfType<Control>().ToArray();
        if (ancestors.OfType<TextBox>().FirstOrDefault() is { } input)
            return AutomationProperties.GetName(input) is { Length: > 0 } label ? label : "文本框";
        foreach (var control in ancestors)
        {
            if (AutomationProperties.GetName(control) is { Length: > 0 } name) return name;
            if (control is Button { Content: string content } && !string.IsNullOrWhiteSpace(content)) return content;
            if (control is MenuItem { Header: string header } && !string.IsNullOrWhiteSpace(header)) return header;
            if (control is ComboBox) return "选项列表";
        }
        return fallback;
    }

    private void WatchWindow(Window window)
    {
        if (_disposed || Owner(window) is null || !_windows.Add(window)) return;
        window.PositionChanged += WindowMoved;
        window.Resized += WindowResized;
        _hub.RecordInteraction("window.open", window.Title ?? "启动器");
    }
    private void UnwatchWindow(Window window)
    {
        window.PositionChanged -= WindowMoved;
        window.Resized -= WindowResized;
    }
    private void WindowMoved(object? sender, PixelPointEventArgs args)
    {
        if (!_disposed && sender is Window window)
            _hub.RecordInteraction("window.move", window.Title ?? "启动器", $"{args.Point.X},{args.Point.Y}");
    }
    private void WindowResized(object? sender, WindowResizedEventArgs args)
    {
        if (!_disposed && sender is Window window)
            _hub.RecordInteraction("window.resize", window.Title ?? "启动器", string.Create(CultureInfo.InvariantCulture, $"{args.ClientSize.Width:0.###},{args.ClientSize.Height:0.###}"));
    }

    private void Clear(TopLevel surface)
    {
        if (!ReferenceEquals(_lastSurface, surface) && !ReferenceEquals(_lastSurface is null ? null : Owner(_lastSurface), surface)) return;
        _lastSurface = null;
        _hub.ClearPointer();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var subscription in _subscriptions) subscription.Dispose();
        foreach (var window in _windows) UnwatchWindow(window);
        _windows.Clear();
        _lastSurface = null;
        _hub.ClearPointer();
    }
}
