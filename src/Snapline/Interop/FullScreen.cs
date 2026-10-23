using static Snapline.Interop.Native;

namespace Snapline.Interop;

/// <summary>Knows when a screen is showing a full screen app, so the line stays away.</summary>
public static class FullScreen
{
    /// <summary>Our own windows never count as full screen apps.</summary>
    public static readonly HashSet<IntPtr> Own = new();

    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow",
    };

    public static bool IsActive(Display monitor)
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || Own.Contains(fg)) return false;
        if (MonitorFromWindow(fg, MONITOR_DEFAULTTONULL) != monitor.Handle) return false;
        if (ShellClasses.Contains(ClassNameOf(fg))) return false;
        if (IsIconic(fg) || IsCloaked(fg)) return false;
        if (!GetWindowRect(fg, out var r)) return false;
        long style = GetWindowLongPtr(fg, -16).ToInt64();
        SHQueryUserNotificationState(out var state);
        return Classify(r, monitor.Bounds, style, IsZoomed(fg), state);
    }

    internal static bool Classify(RECT frame, RECT bounds, long style, bool zoomed,
        QUERY_USER_NOTIFICATION_STATE state)
    {
        // Electron/Chromium can fill the monitor without WS_MAXIMIZE, with an
        // invisible resize border extending past it. That is not exclusive
        // fullscreen. This matters especially with an auto-hidden taskbar:
        // the work area then equals the whole monitor.
        const long caption = 0x00C00000;
        bool captioned = (style & caption) == caption;
        bool covers = frame.Left <= bounds.Left && frame.Top <= bounds.Top
            && frame.Right >= bounds.Right && frame.Bottom >= bounds.Bottom;
        if (!covers) return false; // shell notification state is system-wide
        if (captioned || zoomed) return false;
        // Real borderless fullscreen and shell-reported exclusive fullscreen
        // stay unobstructed. Shell state alone must not block ordinary apps.
        return true;
    }
}
