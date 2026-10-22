using System.Runtime.InteropServices;

namespace HunterPie.Linux.Overlay.Platform;

/// <summary>
/// Experimental. Marks an already mapped X11 window as override-redirect, so the window manager stops managing it.
/// Compositors stack such windows above everything, fullscreen games included, which is how menus and tooltips stay
/// visible. The cost: an unmanaged window never gets keyboard focus. Pointer input still works.
/// On Wayland this runs through XWayland, whose override-redirect windows the compositor treats the same way.
/// </summary>
internal static class X11OverrideRedirect
{
    private const long CWOverrideRedirect = 1L << 9;

    // Offset of override_redirect in XSetWindowAttributes on LP64, and the struct's total size
    private const int OverrideRedirectOffset = 88;
    private const int AttributesSize = 112;

    [DllImport("libX11.so.6")]
    private static extern IntPtr XOpenDisplay(IntPtr displayName);

    [DllImport("libX11.so.6")]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XSync(IntPtr display, int discard);

    [DllImport("libX11.so.6")]
    private static extern int XUnmapWindow(IntPtr display, IntPtr window);

    [DllImport("libX11.so.6")]
    private static extern int XMapRaised(IntPtr display, IntPtr window);

    [DllImport("libX11.so.6")]
    private static extern int XChangeWindowAttributes(IntPtr display, IntPtr window, long valueMask, IntPtr attributes);

    public static bool TryEnable(IntPtr window) => TrySet(window, true);

    public static bool TrySet(IntPtr window, bool enabled)
    {
        if (!OperatingSystem.IsLinux() || window == IntPtr.Zero || IntPtr.Size != 8)
            return false;

        IntPtr display = IntPtr.Zero;
        IntPtr attributes = IntPtr.Zero;

        try
        {
            display = XOpenDisplay(IntPtr.Zero);

            if (display == IntPtr.Zero)
                return false;

            attributes = Marshal.AllocHGlobal(AttributesSize);
            for (int i = 0; i < AttributesSize; i++)
                Marshal.WriteByte(attributes, i, 0);

            Marshal.WriteInt32(attributes, OverrideRedirectOffset, enabled ? 1 : 0);

            // The attribute can only be changed while the window is unmapped
            _ = XUnmapWindow(display, window);
            _ = XSync(display, 0);
            _ = XChangeWindowAttributes(display, window, CWOverrideRedirect, attributes);
            _ = XMapRaised(display, window);
            _ = XSync(display, 0);

            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
        finally
        {
            if (attributes != IntPtr.Zero)
                Marshal.FreeHGlobal(attributes);

            if (display != IntPtr.Zero)
                XCloseDisplay(display);
        }
    }
}
