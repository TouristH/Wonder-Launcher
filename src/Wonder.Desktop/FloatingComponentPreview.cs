// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Media;
using Avalonia.Threading;
using Wonder.Workspace;

namespace Wonder.Desktop;

internal sealed class FloatingComponentPreview : IDisposable
{
    private const uint WindowDisabled = 0x08000000;
    private const uint ExtendedTransparent = 0x00000020;
    private const uint ExtendedToolWindow = 0x00000080;
    private const uint ExtendedLayered = 0x00080000;
    private const uint ExtendedNoActivate = 0x08000000;
    private const uint NonClientHitTest = 0x0084;
    private const uint MouseActivate = 0x0021;

    private readonly Window _owner;
    private readonly WorkspaceView _workspace;
    private readonly Window? _window;
    private readonly Popup? _popup;
    private TopLevel? _popupRoot;
    private readonly ComponentView _preview;
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Vector _grabOffset;
    private readonly X11FloatingPreview? _x11;
    private bool _disposed;
    private bool _closed;

    private FloatingComponentPreview(Window owner, WorkspaceView workspace, ComponentItem item, Vector grabOffset, X11FloatingPreview? x11)
    {
        _owner = owner;
        _workspace = workspace;
        _x11 = x11;
        _preview = workspace.CreateLibraryPreview(item);
        var size = _preview.Footprint;
        var definition = workspace.Components.GetDefinition(item);
        _grabOffset = grabOffset * (size.Width / definition.Width);
        if (x11 is null)
        {
            _window = new Window
            {
                Width = size.Width,
                Height = size.Height,
                WindowStartupLocation = WindowStartupLocation.Manual,
                WindowDecorations = WindowDecorations.None,
                CanResize = false,
                ShowActivated = false,
                ShowInTaskbar = false,
                Topmost = true,
                TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
                Background = Brushes.Transparent,
                FontFamily = owner.FontFamily,
                Content = _preview
            };
            // The underlying workspace and library remain the OLE drop targets.
            Win32Properties.AddWindowStylesCallback(_window, (style, extended) =>
                (style | WindowDisabled, extended | ExtendedTransparent | ExtendedToolWindow | ExtendedNoActivate | ExtendedLayered));
            Win32Properties.AddWndProcHookCallback(_window, IgnoreHitTesting);
            _window.Closed += (_, _) => { _closed = true; Dispose(); };
        }
        else
        {
            // XWayland can cache override-redirect at creation. An X11 popup requests
            // it in XCreateWindow so the compositor follows every position change.
            _popup = new Popup
            {
                Child = _preview,
                Width = size.Width,
                Height = size.Height,
                PlacementTarget = owner,
                Placement = PlacementMode.AnchorAndGravity,
                PlacementRect = new Rect(0, 0, 0, 0),
                PlacementAnchor = PopupAnchor.TopLeft,
                PlacementGravity = PopupGravity.BottomRight,
                PlacementConstraintAdjustment = 0,
                IsLightDismissEnabled = false,
                ShouldUseOverlayLayer = false,
                WindowManagerAddShadowHint = false,
                IsHitTestVisible = false,
                Topmost = true
            };
            _popup.Closed += (_, _) => Dispose();
        }
        _timer.Tick += (_, _) => MoveToCursor();
        _owner.Closed += OwnerClosed;
    }

    internal static FloatingComponentPreview? TryShow(Control source, WorkspaceView workspace, ComponentItem item, Vector grabOffset)
    {
        if (TopLevel.GetTopLevel(source) is not Window owner) return null;
        var handleType = owner.TryGetPlatformHandle()?.HandleDescriptor;
        var x11 = OperatingSystem.IsLinux() && handleType == "XID" ? X11FloatingPreview.TryOpen() : null;
        if (x11 is null && !(OperatingSystem.IsWindows() && handleType == "HWND")) return null;
        if (!TryGetCursor(x11, out var cursor)) { x11?.Dispose(); return null; }

        FloatingComponentPreview? preview = null;
        try
        {
            preview = new FloatingComponentPreview(owner, workspace, item, grabOffset, x11);
            var scale = owner.Screens.ScreenFromPoint(cursor)?.Scaling ?? owner.RenderScaling;
            preview.PlaceAt(cursor, scale);
            if (preview._popup is { } popup && x11 is not null)
            {
                popup.IsOpen = true;
                preview._popupRoot = TopLevel.GetTopLevel(preview._preview) as PopupRoot;
                if (preview._popupRoot?.TryGetPlatformHandle() is not { HandleDescriptor: "XID" } xid)
                {
                    preview.Dispose();
                    return null;
                }
                preview._popupRoot.Background = Brushes.Transparent;
                preview._popupRoot.FontFamily = owner.FontFamily;
                preview._popupRoot.TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
                x11.Configure(xid.Handle);
            }
            else if (preview._window is { } window)
            {
                window.Show(owner);
                if (window.TryGetPlatformHandle() is not { } handle ||
                    window.ActualTransparencyLevel != WindowTransparencyLevel.Transparent)
                {
                    preview.Dispose();
                    return null;
                }
                EnableWindow(handle.Handle, false);
            }
            preview._timer.Start();
            return preview;
        }
        catch (InvalidOperationException)
        {
            if (preview is not null) preview.Dispose();
            else x11?.Dispose();
            return null;
        }
    }

    internal static FloatingComponentPreview? TryShow(WorkspaceView workspace, ComponentItem item, Vector grabOffset) =>
        TryShow(workspace, workspace, item, grabOffset);

    private static IntPtr IgnoreHitTesting(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == NonClientHitTest) { handled = true; return new IntPtr(-1); } // HTTRANSPARENT
        if (message == MouseActivate) { handled = true; return new IntPtr(3); } // MA_NOACTIVATE
        return IntPtr.Zero;
    }

    private void MoveToCursor()
    {
        if (!_disposed && TryGetCursor(_x11, out var cursor))
        {
            _workspace.ConfigureComponentPreview(_preview, cursor);
            var size = _preview.Footprint;
            if (_window != null) { _window.Width = size.Width; _window.Height = size.Height; }
            if (_popup != null) { _popup.Width = size.Width; _popup.Height = size.Height; }
            PlaceAt(cursor, _window?.RenderScaling ?? _popupRoot?.RenderScaling ?? _owner.RenderScaling);
        }
    }

    internal bool IsX11WindowAt(Window window, PixelPoint point) => !_disposed && _x11?.Contains(window, point) == true;

    private static bool TryGetCursor(X11FloatingPreview? x11, out PixelPoint point)
    {
        if (x11 is not null) return x11.TryGetCursor(out point);
        var found = GetCursorPos(out var cursor);
        point = new PixelPoint(cursor.X, cursor.Y);
        return found;
    }

    private void PlaceAt(PixelPoint cursor, double scale)
    {
        // Position is in physical screen pixels, while the preview and grab offset are in DIPs.
        // Read the floating window's current scaling on every move, including after a DPI change.
        var origin = new PixelPoint(
            cursor.X + (int)Math.Round((_grabOffset.X - _preview.Footprint.Width / 2) * scale),
            cursor.Y + (int)Math.Round((_grabOffset.Y - _preview.Footprint.Height / 2) * scale));
        if (_window is { } window && origin != window.Position) window.Position = origin;
        if (_popup is { } popup)
        {
            var offset = _owner.PointToClient(origin);
            popup.HorizontalOffset = offset.X;
            popup.VerticalOffset = offset.Y;
        }
    }

    private void OwnerClosed(object? sender, EventArgs args) => Dispose();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _owner.Closed -= OwnerClosed;
        if (!_closed) _window?.Close();
        if (_popup is { } popup) popup.IsOpen = false;
        _preview.Dispose();
        _x11?.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPosition { public int X, Y; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out CursorPosition position);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnableWindow(IntPtr window, [MarshalAs(UnmanagedType.Bool)] bool enable);
}
