using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace PaperTodo;

internal static class WindowWorkAreaHelper
{
    private const uint MonitorDefaultToNearest = 2;

    public static Rect WorkAreaFor(Rect dipRect)
    {
        if (dipRect.IsEmpty ||
            double.IsNaN(dipRect.Left) ||
            double.IsNaN(dipRect.Top) ||
            double.IsInfinity(dipRect.Left) ||
            double.IsInfinity(dipRect.Top))
        {
            return SystemParameters.WorkArea;
        }

        try
        {
            var nativeRect = DipRectToDevice(dipRect);
            var monitor = MonitorFromRect(ref nativeRect, MonitorDefaultToNearest);
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

            return DeviceRectToDip(info.WorkArea);
        }
        catch
        {
            return SystemParameters.WorkArea;
        }
    }

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

    private static NativeRect DipRectToDevice(Rect rect)
    {
        var (scaleX, scaleY) = SystemDpiScale();
        return new NativeRect
        {
            Left = (int)Math.Floor(rect.Left * scaleX),
            Top = (int)Math.Floor(rect.Top * scaleY),
            Right = (int)Math.Ceiling(rect.Right * scaleX),
            Bottom = (int)Math.Ceiling(rect.Bottom * scaleY)
        };
    }

    private static Rect DeviceRectToDip(NativeRect rect)
    {
        var (scaleX, scaleY) = SystemDpiScale();
        return new Rect(
            rect.Left / scaleX,
            rect.Top / scaleY,
            (rect.Right - rect.Left) / scaleX,
            (rect.Bottom - rect.Top) / scaleY);
    }

    private static (double ScaleX, double ScaleY) SystemDpiScale()
    {
        var primaryProbe = new NativeRect
        {
            Left = 0,
            Top = 0,
            Right = 1,
            Bottom = 1
        };
        var monitor = MonitorFromRect(ref primaryProbe, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return (1.0, 1.0);
        }

        var info = new MonitorInfo
        {
            Size = Marshal.SizeOf<MonitorInfo>()
        };
        if (!GetMonitorInfo(monitor, ref info) || info.Monitor.IsEmpty)
        {
            return (1.0, 1.0);
        }

        var scaleX = info.Monitor.Width / Math.Max(1.0, SystemParameters.PrimaryScreenWidth);
        var scaleY = info.Monitor.Height / Math.Max(1.0, SystemParameters.PrimaryScreenHeight);
        return (ValidScale(scaleX), ValidScale(scaleY));
    }

    private static double ValidScale(double scale)
    {
        return double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0 ? 1.0 : scale;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref NativeRect lprc, uint dwFlags);

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
        public int Width => Right - Left;
        public int Height => Bottom - Top;
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
