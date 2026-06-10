using System.Runtime.InteropServices;

namespace PaperTodo;

internal static class FullscreenForegroundWindowDetector
{
    private const uint MonitorDefaultToNearest = 2;
    private const int FullscreenTolerance = 2;

    public static bool IsForegroundFullscreen()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return false;
        }

        _ = GetWindowThreadProcessId(foreground, out var processId);
        if (processId == Environment.ProcessId)
        {
            return false;
        }

        if (!GetWindowRect(foreground, out var windowRect) || windowRect.IsEmpty)
        {
            return false;
        }

        var monitor = MonitorFromWindow(foreground, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var monitorInfo = new MonitorInfo
        {
            Size = Marshal.SizeOf<MonitorInfo>()
        };
        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return false;
        }

        return CoversMonitor(windowRect, monitorInfo.Monitor);
    }

    private static bool CoversMonitor(Rectangle windowRect, Rectangle monitorRect)
    {
        return windowRect.Left <= monitorRect.Left + FullscreenTolerance &&
               windowRect.Top <= monitorRect.Top + FullscreenTolerance &&
               windowRect.Right >= monitorRect.Right - FullscreenTolerance &&
               windowRect.Bottom >= monitorRect.Bottom - FullscreenTolerance;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rectangle lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public bool IsEmpty => Right <= Left || Bottom <= Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rectangle Monitor;
        public Rectangle WorkArea;
        public uint Flags;
    }
}
