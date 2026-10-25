// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Wonder.Desktop;

internal static class HoverTipBehavior
{
    private static readonly HashSet<Control> OpenTips = new();
    private static bool _mousePressed;
    private static int _nativeDrags;
    private static bool _initialized;
    private static bool Suppressed => _mousePressed || _nativeDrags != 0;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        ToolTip.ToolTipOpeningEvent.AddClassHandler<Control>((_, args) =>
        {
            if (Suppressed) { args.Cancel = true; args.Handled = true; }
        }, handledEventsToo: true);
        ToolTip.IsOpenProperty.Changed.AddClassHandler<Control>((control, _) =>
        {
            if (ToolTip.GetIsOpen(control)) OpenTips.Add(control);
            else OpenTips.Remove(control);
        });
        // TopLevel includes windows and popups. Tunnel handlers also see handled/captured input.
        InputElement.PointerPressedEvent.AddClassHandler<TopLevel>((_, args) =>
        {
            if (args.Pointer.Type != PointerType.Mouse) return;
            _mousePressed = true;
            CloseOpenTips();
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.PointerMovedEvent.AddClassHandler<TopLevel>(UpdatePointer, RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.PointerReleasedEvent.AddClassHandler<TopLevel>(UpdatePointer, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private static void UpdatePointer(TopLevel root, PointerEventArgs args)
    {
        if (args.Pointer.Type == PointerType.Mouse)
        {
            var state = args.GetCurrentPoint(root).Properties;
            // One mouse button state across windows, even if the platform changes pointer IDs.
            // Also recovers when release occurred outside the application.
            _mousePressed = state.IsLeftButtonPressed || state.IsRightButtonPressed || state.IsMiddleButtonPressed || state.IsXButton1Pressed || state.IsXButton2Pressed;
        }
        if (Suppressed) CloseOpenTips();
    }

    private static void CloseOpenTips()
    {
        foreach (var control in OpenTips.ToArray()) ToolTip.SetIsOpen(control, false);
    }

    public static IDisposable SuppressForNativeDrag()
    {
        _nativeDrags++;
        CloseOpenTips();
        return new DragSuppression();
    }

    private sealed class DragSuppression : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _nativeDrags--;
            // Esc can end a native drag while the button is still held. Keep pointer
            // state until release, or the next move if the native loop consumed release.
        }
    }
}
