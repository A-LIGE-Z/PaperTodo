using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace PaperTodo;

internal static class WindowWorkAreaHelper
{
    private const uint MonitorDefaultToNearest = 2;

    public static Rect WorkAreaFor(Window? window)
    {
        if (window == null)
        {
            return SystemParameters.WorkArea;
        }

        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return SystemParameters.WorkArea;
            }

            var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero)
            {
                return SystemParameters.WorkArea;
            }

            var info = new MonitorInfo
            {
                Size = Marshal.SizeOf<MonitorInfo>()
            };
            if (!GetMonitorInfo(monitor, ref info) || info.WorkArea.IsEmpty)
            {
                return SystemParameters.WorkArea;
            }

            return DeviceRectToDip(window, info.WorkArea);
        }
        catch
        {
            return SystemParameters.WorkArea;
        }
    }

    private static Rect DeviceRectToDip(Visual reference, NativeRect rect)
    {
        var source = PresentationSource.FromVisual(reference);
        if (source?.CompositionTarget != null)
        {
            var transform = source.CompositionTarget.TransformFromDevice;
            return new Rect(
                transform.Transform(new Point(rect.Left, rect.Top)),
                transform.Transform(new Point(rect.Right, rect.Bottom)));
        }

        var dpi = VisualTreeHelper.GetDpi(reference);
        var scaleX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
        var scaleY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0;
        return new Rect(
            rect.Left / scaleX,
            rect.Top / scaleY,
            (rect.Right - rect.Left) / scaleX,
            (rect.Bottom - rect.Top) / scaleY);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
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
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
    }
}
