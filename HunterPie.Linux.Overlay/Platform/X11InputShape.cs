using System.Runtime.InteropServices;

namespace HunterPie.Linux.Overlay.Platform;

/// <summary>
/// Makes an X11 window ignore the mouse and keyboard by giving it an empty input region (XShape extension), so clicks
/// go through to the game underneath. Works on X11 and on XWayland, which is what Avalonia uses on Wayland sessions.
/// </summary>
internal static class X11InputShape
{
    private const int ShapeSet = 0;
    private const int ShapeInput = 2;

    [DllImport("libX11.so.6")]
    private static extern IntPtr XOpenDisplay(IntPtr displayName);

    [DllImport("libX11.so.6")]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XFlush(IntPtr display);

    [DllImport("libXext.so.6")]
    private static extern void XShapeCombineRectangles(
        IntPtr display, IntPtr window, int destKind, int xOffset, int yOffset,
        IntPtr rectangles, int rectangleCount, int operation, int ordering);

    [DllImport("libXext.so.6")]
    private static extern void XShapeCombineMask(
        IntPtr display, IntPtr window, int destKind, int xOffset, int yOffset, IntPtr pixmap, int operation);

    /// <summary>
    /// True when the window is now click-through (<paramref name="clickThrough"/>) or interactive again
    /// </summary>
    public static bool TrySetClickThrough(IntPtr window, bool clickThrough)
    {
        if (!OperatingSystem.IsLinux() || window == IntPtr.Zero)
            return false;

        IntPtr display = IntPtr.Zero;

        try
        {
            // Our own connection: the window id is global on the X server, and Avalonia's connection stays untouched
            display = XOpenDisplay(IntPtr.Zero);

            if (display == IntPtr.Zero)
                return false;

            if (clickThrough)
                // No rectangles = empty input region
                XShapeCombineRectangles(display, window, ShapeInput, 0, 0, IntPtr.Zero, 0, ShapeSet, 0);
            else
                // A None (0) mask removes the custom input shape, the window takes input over its whole area again
                XShapeCombineMask(display, window, ShapeInput, 0, 0, IntPtr.Zero, ShapeSet);

            XFlush(display);

            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
        finally
        {
            if (display != IntPtr.Zero)
                XCloseDisplay(display);
        }
    }
}
