using System.Runtime.InteropServices;

namespace PaperTodo;

internal static class FullscreenForegroundWindowDetector
{
    private const uint MonitorDefaultToNearest = 2;
    private const uint GwHwndNext = 2;
    private const int FullscreenTolerance = 2;
    private const int MaxZOrderWindowsToProbe = 48;
    private static IntPtr _lastExternalForegroundWindow;

    public static bool IsForegroundFullscreen()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return false;
        }

        if (IsCurrentProcessWindow(foreground))
        {
            return IsTrackedExternalForegroundFullscreen() || IsTopExternalWindowFullscreen();
        }

        _lastExternalForegroundWindow = foreground;
        return IsFullscreenWindow(foreground);
    }

    private static bool IsTrackedExternalForegroundFullscreen()
    {
        if (_lastExternalForegroundWindow == IntPtr.Zero)
        {
            return false;
        }

        if (!IsWindow(_lastExternalForegroundWindow) || IsCurrentProcessWindow(_lastExternalForegroundWindow))
        {
            _lastExternalForegroundWindow = IntPtr.Zero;
            return false;
        }

        return IsFullscreenWindow(_lastExternalForegroundWindow);
    }

    private static bool IsTopExternalWindowFullscreen()
    {
        var hwnd = GetTopWindow(IntPtr.Zero);
        for (var i = 0; hwnd != IntPtr.Zero && i < MaxZOrderWindowsToProbe; i++)
        {
            if (!IsCurrentProcessWindow(hwnd) && IsVisibleWindow(hwnd))
            {
                if (IsFullscreenWindow(hwnd))
                {
                    _lastExternalForegroundWindow = hwnd;
                    return true;
                }

                return false;
            }

            hwnd = GetWindow(hwnd, GwHwndNext);
        }

        return false;
    }

    private static bool IsFullscreenWindow(IntPtr hwnd)
    {
        if (!IsVisibleWindow(hwnd) || !GetWindowRect(hwnd, out var windowRect) || windowRect.IsEmpty)
        {
            return false;
        }

        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
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

    private static bool IsVisibleWindow(IntPtr hwnd)
    {
        return IsWindowVisible(hwnd) && !IsIconic(hwnd);
    }

    private static bool IsCurrentProcessWindow(IntPtr hwnd)
    {
        _ = GetWindowThreadProcessId(hwnd, out var processId);
        return processId == Environment.ProcessId;
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
    private static extern IntPtr GetTopWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

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
