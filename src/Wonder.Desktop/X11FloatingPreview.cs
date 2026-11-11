// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;

namespace Wonder.Desktop;

internal sealed class X11FloatingPreview : IDisposable
{
    private const int ShapeInput = 2;
    private const int ShapeSet = 0;
    private const int Unsorted = 0;
    private const string X11 = "libX11.so.6";
    private const string Xext = "libXext.so.6";

    private readonly nint _display;
    private readonly nint _root;

    private X11FloatingPreview(nint display)
    {
        _display = display;
        _root = XDefaultRootWindow(display);
    }

    internal static X11FloatingPreview? TryOpen()
    {
        nint display = 0;
        try
        {
            display = XOpenDisplay(0);
            if (display == 0) return null;
            var compositor = XInternAtom(display, $"_NET_WM_CM_S{XDefaultScreen(display)}", 1);
            if (compositor == 0 || XGetSelectionOwner(display, compositor) == 0)
            {
                XCloseDisplay(display);
                return null;
            }
            if (XShapeQueryVersion(display, out var major, out var minor) != 0 &&
                (major > 1 || major == 1 && minor >= 1))
                return new X11FloatingPreview(display);
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException) { }
        if (display != 0) XCloseDisplay(display);
        return null;
    }

    internal bool TryGetCursor(out PixelPoint point)
    {
        var sameScreen = XQueryPointer(_display, _root, out _, out _, out var x, out var y, out _, out _, out _) != 0;
        point = new PixelPoint(x, y);
        return sameScreen;
    }

    internal void Configure(nint window)
    {
        // Apply before mapping so the native floating window never covers a drop target for input.
        XShapeCombineRectangles(_display, window, ShapeInput, 0, 0, 0, 0, ShapeSet, Unsorted);
        XSync(_display, 0);
    }

    internal bool Contains(Window window, PixelPoint point) => Contains(_display, _root, window, point);

    internal static bool IsWindowAt(Window window, PixelPoint point)
    {
        var display = XOpenDisplay(0);
        if (display == 0) return false;
        try { return Contains(display, XDefaultRootWindow(display), window, point); }
        finally { XCloseDisplay(display); }
    }

    private static bool Contains(nint display, nint root, Window window, PixelPoint point)
    {
        if (window.TryGetPlatformHandle() is not { HandleDescriptor: "XID" } handle) return false;
        var current = root;
        while (XTranslateCoordinates(display, root, current, point.X, point.Y, out _, out _, out var child) != 0 && child != 0)
        {
            if (child == handle.Handle) return true;
            current = child;
        }
        return false;
    }

    public void Dispose() => XCloseDisplay(_display);

    [DllImport(X11)] private static extern nint XOpenDisplay(nint name);
    [DllImport(X11)] private static extern int XCloseDisplay(nint display);
    [DllImport(X11)] private static extern int XDefaultScreen(nint display);
    [DllImport(X11)] private static extern nint XDefaultRootWindow(nint display);
    [DllImport(X11)] private static extern nint XInternAtom(nint display, [MarshalAs(UnmanagedType.LPStr)] string name, int onlyIfExists);
    [DllImport(X11)] private static extern nint XGetSelectionOwner(nint display, nint selection);
    [DllImport(X11)] private static extern int XSync(nint display, int discard);
    [DllImport(X11)] private static extern int XQueryPointer(nint display, nint window, out nint root, out nint child,
        out int rootX, out int rootY, out int windowX, out int windowY, out uint modifiers);
    [DllImport(X11)] private static extern int XTranslateCoordinates(nint display, nint source, nint target,
        int x, int y, out int targetX, out int targetY, out nint child);
    [DllImport(Xext)] private static extern int XShapeQueryVersion(nint display, out int major, out int minor);
    [DllImport(Xext)] private static extern void XShapeCombineRectangles(nint display, nint window, int kind, int x, int y,
        nint rectangles, int count, int operation, int ordering);
}
