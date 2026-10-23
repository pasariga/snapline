using System.IO;
using System.Text.Json;
using static Snapline.Interop.Native;

namespace Snapline.Interop;

// Opt-in, local diagnostics. Never records window titles, URLs or image content.
internal static class WindowAudit
{
    public static int Run(string path)
    {
        var fg = GetForegroundWindow();
        GetWindowRect(fg, out var r);
        SHQueryUserNotificationState(out var shell);
        var own = new List<object>();
        EnumWindows((h, _) => {
            GetWindowThreadProcessId(h, out uint pid);
            if (System.Diagnostics.Process.GetProcessById((int)pid).ProcessName == "Snapline")
            {
                GetWindowRect(h, out var frame);
                own.Add(new { Class = ClassNameOf(h), Visible = IsWindowVisible(h),
                    ExStyle = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64(), Rect = frame.ToString() });
            }
            return true;
        }, IntPtr.Zero);
        var data = new { At = DateTimeOffset.Now, Class = ClassNameOf(fg),
            Maximized = IsZoomed(fg), Cloaked = IsCloaked(fg), WindowRect = r.ToString(),
            ShellState = shell.ToString(), Style = GetWindowLongPtr(fg, -16).ToInt64(),
            Displays = Displays.All().Select(d => new { Bounds = d.Bounds.ToString(),
                Work = d.Work.ToString(), Dpi = d.Dpi, OldPolicyBlocked = FullScreen.IsActive(d) }),
            SnaplineWindows = own };
        File.WriteAllText(path, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
}
